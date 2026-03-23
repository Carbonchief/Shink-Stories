using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Shink.Components;
using Shink.Components.Content;
using Shink.Services;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Xml.Linq;

const string AuthSessionIdClaimType = "shink:session_id";

var builder = WebApplication.CreateBuilder(args);
var postHogHostUrl = builder.Configuration["PostHog:HostUrl"];
var authSessionBootstrapOptions = builder.Configuration.GetSection(AuthSessionOptions.SectionName).Get<AuthSessionOptions>() ?? new AuthSessionOptions();
var authSessionLifetimeDays = NormalizeSessionLifetimeDays(authSessionBootstrapOptions.SessionLifetimeDays);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "shink.auth";
        options.LoginPath = "/teken-in";
        options.AccessDeniedPath = "/teken-in";
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(authSessionLifetimeDays);
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                if (!(context.Principal?.Identity?.IsAuthenticated ?? false))
                {
                    return;
                }

                var signedInEmail = context.Principal.FindFirst(ClaimTypes.Email)?.Value
                                   ?? context.Principal.Identity?.Name;
                var sessionIdValue = context.Principal.FindFirst(AuthSessionIdClaimType)?.Value;

                if (string.IsNullOrWhiteSpace(signedInEmail) ||
                    !Guid.TryParse(sessionIdValue, out var sessionId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                    return;
                }

                var authSessionService = context.HttpContext.RequestServices.GetRequiredService<IAuthSessionService>();
                var validationState = await authSessionService.ValidateSessionAsync(
                    signedInEmail,
                    sessionId,
                    context.HttpContext.RequestAborted);

                if (validationState == AuthSessionValidationState.Inactive)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                }
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAudioAccessService, AudioAccessService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<CloudflareR2Options>(builder.Configuration.GetSection(CloudflareR2Options.SectionName));
builder.Services.Configure<AuthSessionOptions>(builder.Configuration.GetSection(AuthSessionOptions.SectionName));
builder.Services.Configure<PayFastOptions>(builder.Configuration.GetSection(PayFastOptions.SectionName));
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection(PaystackOptions.SectionName));
builder.Services.AddHttpClient("audio-origin", client =>
{
    // Audio responses can be long-lived streams. Keep timeout above default 100 seconds.
    client.Timeout = TimeSpan.FromMinutes(30);
});
builder.Services.AddHttpClient<IContactEmailService, ResendContactEmailService>();
builder.Services.AddHttpClient<ISupabaseAuthService, SupabaseAuthService>();
builder.Services.AddHttpClient<IAuthSessionService, SupabaseAuthSessionService>();
builder.Services.AddHttpClient<IStoryCatalogService, SupabaseStoryCatalogService>();
builder.Services.AddHttpClient<PayFastCheckoutService>();
builder.Services.AddHttpClient<PaystackCheckoutService>();
builder.Services.AddHttpClient<ISubscriptionLedgerService, SupabaseSubscriptionLedgerService>();
builder.Services.AddHttpClient<IStoryTrackingService, SupabaseStoryTrackingService>();
builder.Services.AddSingleton<IContactFormProtectionService, ContactFormProtectionService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("audio-stream", httpContext =>
    {
        var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter(clientId, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 180,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("contact-submit", httpContext =>
    {
        var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter($"contact:{clientId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 12,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("auth-submit", httpContext =>
    {
        var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter($"auth:{clientId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 24,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    options.AddPolicy("story-tracking", httpContext =>
    {
        var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-client";
        return RateLimitPartition.GetFixedWindowLimiter($"story:{clientId}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 180,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (httpContext, next) =>
{
    if (HttpMethods.IsGet(httpContext.Request.Method) &&
        httpContext.Request.Path == "/" &&
        (httpContext.User.Identity?.IsAuthenticated ?? false) &&
        !HasHomeOverrideQuery(httpContext.Request.Query))
    {
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? httpContext.User.Identity?.Name;
        var subscriptionLedgerService = httpContext.RequestServices.GetRequiredService<ISubscriptionLedgerService>();
        var redirectPath = await ResolvePostAuthRedirectPathAsync(subscriptionLedgerService, email, httpContext.RequestAborted);
        httpContext.Response.Redirect(redirectPath);
        return;
    }

    await next();
});

app.Use(async (httpContext, next) =>
{
    if (HttpMethods.IsGet(httpContext.Request.Method) &&
        IsGratisPath(httpContext.Request.Path) &&
        (httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                    ?? httpContext.User.Identity?.Name;
        var subscriptionLedgerService = httpContext.RequestServices.GetRequiredService<ISubscriptionLedgerService>();
        var hasPaidSubscription = await subscriptionLedgerService.HasActivePaidSubscriptionAsync(email, httpContext.RequestAborted);
        if (hasPaidSubscription)
        {
            var redirectPath = ResolveGratisRedirectPath(httpContext.Request.Path, httpContext.Request.QueryString);
            httpContext.Response.Redirect(redirectPath);
            return;
        }
    }

    await next();
});

app.Use(async (httpContext, next) =>
{
    if (HttpMethods.IsGet(httpContext.Request.Method) &&
        TryResolveStoryPathTarget(httpContext.Request.Path, out var source, out var slug))
    {
        var requestedPath = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";

        if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
        {
            httpContext.Response.Redirect(BuildOpsiesStoryRedirectPath(requestedPath));
            return;
        }

        var storyCatalogService = httpContext.RequestServices.GetRequiredService<IStoryCatalogService>();
        var story = string.Equals(source, "gratis", StringComparison.OrdinalIgnoreCase)
            ? await storyCatalogService.FindFreeBySlugAsync(slug, httpContext.RequestAborted)
            : await storyCatalogService.FindLuisterBySlugAsync(slug, httpContext.RequestAborted);

        if (story is not null)
        {
            var requirement = StoryAccessPolicy.ResolveRequirement(source, story);
            var subscriptionLedgerService = httpContext.RequestServices.GetRequiredService<ISubscriptionLedgerService>();
            var email = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                        ?? httpContext.User.Identity?.Name;
            var hasAccess = await HasRequiredStoryAccessAsync(
                subscriptionLedgerService,
                email,
                requirement,
                httpContext.RequestAborted);

            if (!hasAccess)
            {
                httpContext.Response.Redirect(BuildOpsiesStoryRedirectPath(requestedPath));
                return;
            }
        }
    }

    await next();
});

app.Use(async (httpContext, next) =>
{
    httpContext.Response.OnStarting(() =>
    {
        var headers = httpContext.Response.Headers;
        var contentSecurityPolicy = BuildContentSecurityPolicy(app.Environment.IsDevelopment(), postHogHostUrl);

        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "SAMEORIGIN";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        headers["Permissions-Policy"] = "accelerometer=(), autoplay=(self), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()";
        headers["Content-Security-Policy"] = contentSecurityPolicy;
        return Task.CompletedTask;
    });

    await next();
});

app.UseAntiforgery();
app.UseRateLimiter();

app.Use(async (httpContext, next) =>
{
    if (IsBlockedPublicAudioPath(httpContext.Request.Path))
    {
        httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    await next();
});

// Fallback serving from physical wwwroot to avoid manifest drift issues during publish.
app.UseStaticFiles();

app.MapGet("/media/audio/{slug}", async (
    string slug,
    string? token,
    IAudioAccessService audioAccessService,
    IStoryCatalogService storyCatalogService,
    IWebHostEnvironment environment,
    IOptions<CloudflareR2Options> cloudflareR2Options,
    IHttpClientFactory httpClientFactory,
    HttpContext httpContext) =>
{
    if (!IsLikelySameSiteMediaRequest(httpContext))
    {
        return Results.Forbid();
    }

    if (!audioAccessService.IsTokenValid(slug, token))
    {
        return Results.Unauthorized();
    }

    var story = await storyCatalogService.FindAnyBySlugAsync(slug, httpContext.RequestAborted);
    if (story is null)
    {
        return Results.NotFound();
    }

    ApplyAudioResponseSecurityHeaders(httpContext);

    if (string.Equals(story.AudioProvider, "r2", StringComparison.OrdinalIgnoreCase))
    {
        if (!TryBuildR2AudioUri(
                cloudflareR2Options.Value.PublicBaseUrl,
                story.AudioFileName,
                out var sourceUri))
        {
            return Results.NotFound();
        }

        return await ProxyAudioFromOriginAsync(
            httpContext,
            httpClientFactory,
            sourceUri,
            story.AudioContentType,
            story.AudioFileName);
    }

    if (!string.Equals(story.AudioProvider, "local", StringComparison.OrdinalIgnoreCase))
    {
        return Results.NotFound();
    }

    if (!TryResolveLocalAudioPath(environment.ContentRootPath, story.AudioFileName, out var audioFilePath))
    {
        return Results.Forbid();
    }

    if (!File.Exists(audioFilePath))
    {
        return Results.NotFound();
    }

    var audioMimeType = ResolveAudioMimeType(story.AudioContentType, story.AudioFileName);
    return Results.File(audioFilePath, audioMimeType, enableRangeProcessing: true);
}).RequireRateLimiting("audio-stream");

app.MapGet("/betaal/payfast/{planSlug}", (string planSlug) =>
    Results.Redirect($"/betaal/{Uri.EscapeDataString(planSlug)}?provider=payfast"));

app.MapGet("/betaal/paystack/{planSlug}", (string planSlug) =>
    Results.Redirect($"/betaal/{Uri.EscapeDataString(planSlug)}?provider=paystack"));

app.MapGet("/betaal/{planSlug}", async (
    string planSlug,
    string? provider,
    string? returnUrl,
    PayFastCheckoutService payFastCheckoutService,
    PaystackCheckoutService paystackCheckoutService,
    ISubscriptionLedgerService subscriptionLedgerService,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        var checkoutPath = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
        var encodedReturnUrl = Uri.EscapeDataString(checkoutPath);
        return Results.Redirect($"/teken-in?returnUrl={encodedReturnUrl}");
    }

    var plan = PaymentPlanCatalog.FindBySlug(planSlug);
    if (plan is null)
    {
        return Results.NotFound();
    }

    var safeReturnUrl = GetSafeStoryReturnUrl(returnUrl);

    var signedInEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                       ?? httpContext.User.Identity?.Name;
    var hasActiveTierSubscription = await subscriptionLedgerService.HasActiveSubscriptionForTierAsync(
        signedInEmail,
        plan.TierCode,
        httpContext.RequestAborted);
    if (hasActiveTierSubscription)
    {
        logger.LogInformation(
            "Blocked duplicate checkout for active tier. plan={PlanSlug} tier={TierCode} email={Email}",
            plan.Slug,
            plan.TierCode,
            signedInEmail);

        var duplicateRedirectQuery = new Dictionary<string, string?>
        {
            ["betaling"] = "reeds-ingeteken",
            ["tier"] = plan.TierCode
        };
        if (!string.IsNullOrWhiteSpace(safeReturnUrl))
        {
            duplicateRedirectQuery["returnUrl"] = safeReturnUrl;
        }
        var duplicateRedirectPath = QueryHelpers.AddQueryString("/opsies", duplicateRedirectQuery);
        return Results.Redirect(duplicateRedirectPath);
    }

    if (!TryResolvePaymentProvider(provider, out var selectedProvider))
    {
        return Results.BadRequest(new { message = "Ongeldige betaalverskaffer. Gebruik 'paystack' of 'payfast'." });
    }

    if (string.Equals(selectedProvider, "paystack", StringComparison.OrdinalIgnoreCase))
    {
        var checkoutResult = await paystackCheckoutService.InitializeCheckoutAsync(plan, httpContext, safeReturnUrl, httpContext.RequestAborted);
        if (!checkoutResult.IsSuccess || string.IsNullOrWhiteSpace(checkoutResult.AuthorizationUrl))
        {
            return Results.Problem(
                title: "Kon nie betaling begin nie",
                detail: checkoutResult.ErrorMessage ?? "Paystack checkout kon nie begin nie.",
                statusCode: StatusCodes.Status500InternalServerError);
        }

        logger.LogInformation(
            "Paystack checkout initialized. plan={PlanSlug} reference={Reference}",
            plan.Slug,
            checkoutResult.Reference);

        return Results.Redirect(checkoutResult.AuthorizationUrl);
    }

    if (!payFastCheckoutService.TryBuildCheckout(plan, httpContext, safeReturnUrl, out var checkoutForm, out var errorMessage))
    {
        return Results.Problem(
            title: "Kon nie betaling begin nie",
            detail: errorMessage,
            statusCode: StatusCodes.Status500InternalServerError);
    }

    var html = payFastCheckoutService.BuildAutoSubmitFormHtml(checkoutForm, $"Jy betaal nou vir {plan.Name}.");
    httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.Expires = "0";
    httpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/api/payfast/notify", async (HttpContext httpContext, PayFastCheckoutService payFastCheckoutService, ISubscriptionLedgerService subscriptionLedgerService, ILogger<Program> logger) =>
{
    if (!httpContext.Request.HasFormContentType)
    {
        logger.LogWarning("PayFast ITN rejected: invalid content type.");
        return Results.Ok();
    }

    var form = await httpContext.Request.ReadFormAsync(httpContext.RequestAborted);
    var signatureValid = payFastCheckoutService.IsItnSignatureValid(form);
    var serverValidationPayload = payFastCheckoutService.BuildItnValidationPayload(form);
    var serverConfirmed = await payFastCheckoutService.ValidateServerConfirmationAsync(serverValidationPayload, httpContext.RequestAborted);
    var checksPassed = signatureValid && serverConfirmed;

    logger.LogInformation(
        "PayFast ITN received. valid={ChecksPassed}, signature_valid={SignatureValid}, server_confirmed={ServerConfirmed}, pf_payment_id={PayFastPaymentId}, payment_status={PaymentStatus}, m_payment_id={MerchantPaymentId}",
        checksPassed,
        signatureValid,
        serverConfirmed,
        form["pf_payment_id"].ToString(),
        form["payment_status"].ToString(),
        form["m_payment_id"].ToString());

    if (!checksPassed)
    {
        logger.LogWarning(
            "PayFast ITN failed validation. pf_payment_id={PayFastPaymentId}, m_payment_id={MerchantPaymentId}",
            form["pf_payment_id"].ToString(),
            form["m_payment_id"].ToString());
    }
    else
    {
        var persistResult = await subscriptionLedgerService.RecordPayFastEventAsync(form, httpContext.RequestAborted);
        if (!persistResult.IsSuccess)
        {
            logger.LogWarning(
                "PayFast subscription persistence failed. Error={Error} m_payment_id={MerchantPaymentId}",
                persistResult.ErrorMessage,
                form["m_payment_id"].ToString());
        }
        else
        {
            logger.LogInformation(
                "PayFast subscription persisted. subscription_id={SubscriptionId} m_payment_id={MerchantPaymentId}",
                persistResult.SubscriptionId,
                form["m_payment_id"].ToString());
        }
    }

    // Return 200 to avoid repeated retries; process failed validations via logs/monitoring.
    return Results.Ok();
}).DisableAntiforgery();

app.MapPost("/api/paystack/webhook", async (HttpContext httpContext, PaystackCheckoutService paystackCheckoutService, ISubscriptionLedgerService subscriptionLedgerService, ILogger<Program> logger) =>
{
    using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
    var payload = await reader.ReadToEndAsync(httpContext.RequestAborted);
    if (string.IsNullOrWhiteSpace(payload))
    {
        logger.LogWarning("Paystack webhook ignored: empty payload.");
        return Results.Ok();
    }

    var signature = httpContext.Request.Headers["x-paystack-signature"].ToString();
    var signatureValid = paystackCheckoutService.IsWebhookSignatureValid(payload, signature);
    if (!signatureValid)
    {
        logger.LogWarning("Paystack webhook rejected: invalid signature.");
        return Results.Ok();
    }

    var persistResult = await subscriptionLedgerService.RecordPaystackEventAsync(payload, httpContext.RequestAborted);
    if (!persistResult.IsSuccess)
    {
        logger.LogWarning(
            "Paystack subscription persistence failed. Error={Error}",
            persistResult.ErrorMessage);
    }
    else
    {
        logger.LogInformation(
            "Paystack subscription persisted. subscription_id={SubscriptionId}",
            persistResult.SubscriptionId);
    }

    // Return 200 to avoid repeated retries; process failed validations via logs/monitoring.
    return Results.Ok();
}).DisableAntiforgery();

app.MapPost("/api/auth/login", async (
    AuthSignInApiRequest request,
    ISupabaseAuthService supabaseAuthService,
    IAuthSessionService authSessionService,
    ISubscriptionLedgerService subscriptionLedgerService,
    HttpContext httpContext) =>
{
    request = request with
    {
        Email = request.Email?.Trim() ?? string.Empty,
        Password = request.Password ?? string.Empty
    };

    var validationError = ValidateSignInRequest(request.Email, request.Password);
    if (validationError is not null)
    {
        return Results.BadRequest(new { message = validationError });
    }

    var signInResult = await supabaseAuthService.SignInWithPasswordAsync(request.Email!, request.Password!, httpContext.RequestAborted);
    if (!signInResult.IsSuccess)
    {
        return Results.BadRequest(new
        {
            message = signInResult.ErrorMessage ?? "Kon nie teken in nie. Probeer asseblief weer."
        });
    }

    var signedInEmail = signInResult.UserEmail ?? request.Email!;
    var signInCookieResult = await SignInUserAsync(httpContext, signedInEmail, authSessionService);
    if (!signInCookieResult.IsSuccess)
    {
        return Results.Json(
            new { message = signInCookieResult.ErrorMessage ?? "Kon nie nou jou sessie begin nie. Probeer asseblief weer." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var redirectPath = await ResolvePostAuthRedirectPathAsync(subscriptionLedgerService, signedInEmail, httpContext.RequestAborted);
    return Results.Ok(new
    {
        message = "Welkom terug! Jy is nou ingeteken.",
        redirectPath
    });
}).RequireRateLimiting("auth-submit").DisableAntiforgery();

app.MapPost("/api/auth/signup", async (
    AuthSignUpApiRequest request,
    ISupabaseAuthService supabaseAuthService,
    IAuthSessionService authSessionService,
    ISubscriptionLedgerService subscriptionLedgerService,
    HttpContext httpContext) =>
{
    request = request with
    {
        FirstName = request.FirstName?.Trim() ?? string.Empty,
        LastName = request.LastName?.Trim() ?? string.Empty,
        DisplayName = request.DisplayName?.Trim() ?? string.Empty,
        Email = request.Email?.Trim() ?? string.Empty,
        MobileNumber = request.MobileNumber?.Trim() ?? string.Empty,
        Password = request.Password ?? string.Empty
    };

    var validationError = ValidateSignUpRequest(request);
    if (validationError is not null)
    {
        return Results.BadRequest(new { message = validationError });
    }

    var signUpResult = await supabaseAuthService.SignUpWithPasswordAsync(
        request.Email!,
        request.Password!,
        new SignUpProfileData(
            request.FirstName,
            request.LastName,
            request.DisplayName,
            request.MobileNumber),
        httpContext.RequestAborted);
    if (!signUpResult.IsSuccess)
    {
        return Results.BadRequest(new
        {
            message = signUpResult.ErrorMessage ?? "Kon nie nou registreer nie. Probeer asseblief weer."
        });
    }

    var signInResult = await supabaseAuthService.SignInWithPasswordAsync(request.Email!, request.Password!, httpContext.RequestAborted);
    if (!signInResult.IsSuccess)
    {
        return Results.BadRequest(new
        {
            message = "Jou rekening is geskep. Bevestig asseblief jou e-posadres en teken daarna in."
        });
    }

    var signedInEmail = signInResult.UserEmail ?? request.Email!;
    var profileStored = await subscriptionLedgerService.UpsertSubscriberProfileAsync(
        signedInEmail,
        request.FirstName,
        request.LastName,
        request.DisplayName,
        request.MobileNumber,
        httpContext.RequestAborted);
    var gratisProvisioned = await subscriptionLedgerService.EnsureGratisAccessAsync(
        signedInEmail,
        request.FirstName,
        request.LastName,
        request.DisplayName,
        request.MobileNumber,
        httpContext.RequestAborted);

    var signInCookieResult = await SignInUserAsync(httpContext, signedInEmail, authSessionService);
    if (!signInCookieResult.IsSuccess)
    {
        return Results.Json(
            new { message = signInCookieResult.ErrorMessage ?? "Jou rekening is geskep, maar ons kon nie nou jou sessie begin nie. Probeer asseblief weer teken in." },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var redirectPath = await ResolvePostAuthRedirectPathAsync(subscriptionLedgerService, signedInEmail, httpContext.RequestAborted);
    return Results.Ok(new
    {
        message =
            profileStored && gratisProvisioned
                ? "Welkom! Jou rekening is geskep en jy is nou ingeteken."
                : profileStored
                    ? "Welkom! Jou rekening is geskep en jy is nou ingeteken. Ons kon nie jou gratis toegang nou aktiveer nie, maar jy kan steeds probeer luister."
                    : gratisProvisioned
                        ? "Welkom! Jou rekening is geskep en jy is nou ingeteken. Ons kon nie al jou profielbesonderhede nou stoor nie."
                        : "Welkom! Jou rekening is geskep en jy is nou ingeteken. Ons kon nie al jou profiel- en gratis toegangbesonderhede nou voltooi nie.",
        redirectPath
    });
}).RequireRateLimiting("auth-submit").DisableAntiforgery();

app.MapPost("/api/auth/logout", async (HttpContext httpContext, IAuthSessionService authSessionService) =>
{
    await RevokeCurrentUserSessionAsync(httpContext, authSessionService);
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Jy is nou afgeteken." });
}).RequireRateLimiting("auth-submit").DisableAntiforgery();

app.MapGet("/teken-uit", async (HttpContext httpContext, IAuthSessionService authSessionService) =>
{
    await RevokeCurrentUserSessionAsync(httpContext, authSessionService);
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireRateLimiting("auth-submit");

app.MapPost("/api/stories/{slug}/view", async (
    string slug,
    StoryViewTrackApiRequest request,
    IStoryTrackingService storyTrackingService,
    HttpContext httpContext) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        return Results.Unauthorized();
    }

    if (!IsLikelySameSiteRequest(httpContext))
    {
        return Results.Forbid();
    }

    if (!TryNormalizeStorySlug(slug, out var normalizedStorySlug))
    {
        return Results.BadRequest(new { message = "Ongeldige storie-identifiseerder." });
    }

    var signedInEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                       ?? httpContext.User.Identity?.Name;
    var source = NormalizeStoryTrackingSource(request.Source, request.StoryPath);
    var storyPath = ResolveStoryTrackingPath(request.StoryPath, normalizedStorySlug, source);
    var referrerPath = ResolveOptionalTrackingPath(request.ReferrerPath);

    var tracked = await storyTrackingService.RecordStoryViewAsync(
        signedInEmail,
        new StoryViewTrackingRequest(
            StorySlug: normalizedStorySlug,
            StoryPath: storyPath,
            Source: source,
            ReferrerPath: referrerPath),
        httpContext.RequestAborted);

    return Results.Ok(new { tracked });
}).RequireRateLimiting("story-tracking").DisableAntiforgery();

app.MapPost("/api/stories/{slug}/listen", async (
    string slug,
    StoryListenTrackApiRequest request,
    IStoryTrackingService storyTrackingService,
    HttpContext httpContext) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        return Results.Unauthorized();
    }

    if (!IsLikelySameSiteRequest(httpContext))
    {
        return Results.Forbid();
    }

    if (!TryNormalizeStorySlug(slug, out var normalizedStorySlug))
    {
        return Results.BadRequest(new { message = "Ongeldige storie-identifiseerder." });
    }

    if (!Guid.TryParse(request.SessionId, out var sessionId))
    {
        return Results.BadRequest(new { message = "Ongeldige sessie-identifiseerder." });
    }

    var listenedSeconds = NormalizeListenSeconds(request.ListenedSeconds);
    if (listenedSeconds <= 0)
    {
        return Results.Ok(new { tracked = false });
    }

    var signedInEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                       ?? httpContext.User.Identity?.Name;
    var source = NormalizeStoryTrackingSource(request.Source, request.StoryPath);
    var storyPath = ResolveStoryTrackingPath(request.StoryPath, normalizedStorySlug, source);
    var eventType = NormalizeStoryListenEventType(request.EventType);

    var tracked = await storyTrackingService.RecordStoryListenAsync(
        signedInEmail,
        new StoryListenTrackingRequest(
            StorySlug: normalizedStorySlug,
            StoryPath: storyPath,
            SessionId: sessionId,
            EventType: eventType,
            ListenedSeconds: listenedSeconds,
            PositionSeconds: NormalizeOptionalSeconds(request.PositionSeconds),
            DurationSeconds: NormalizeOptionalDuration(request.DurationSeconds),
            Source: source,
            IsCompleted: request.IsCompleted ?? false),
        httpContext.RequestAborted);

    return Results.Ok(new { tracked });
}).RequireRateLimiting("story-tracking").DisableAntiforgery();

app.MapGet("/api/search/suggest", async (string? q, int? limit, IStoryCatalogService storyCatalogService, HttpContext httpContext) =>
{
    var query = (q ?? string.Empty).Trim();
    if (query.Length < 2)
    {
        return Results.Ok(new { query, results = Array.Empty<object>() });
    }

    var resultLimit = Math.Clamp(limit ?? 8, 1, 12);

    var freeStoriesTask = storyCatalogService.GetFreeStoriesAsync(httpContext.RequestAborted);
    var luisterStoriesTask = storyCatalogService.GetLuisterStoriesAsync(httpContext.RequestAborted);
    await Task.WhenAll(freeStoriesTask, luisterStoriesTask);

    var candidates = BuildSearchStaticCandidates()
        .Concat(freeStoriesTask.Result.Select(story => new SearchSiteCandidate(
            Title: story.Title,
            Description: story.Description,
            Url: $"/gratis/{Uri.EscapeDataString(story.Slug)}",
            Kind: "Gratis storie",
            Keywords: "gratis luister kinders afrikaans oudiostorie",
            ThumbnailPath: story.ThumbnailPath,
            IsSitePage: false)))
        .Concat(luisterStoriesTask.Result.Select(story => new SearchSiteCandidate(
            Title: story.Title,
            Description: story.Description,
            Url: $"/luister/{Uri.EscapeDataString(story.Slug)}",
            Kind: "Alle stories",
            Keywords: "luister stories intekening afrikaans oudiostorie",
            ThumbnailPath: story.ThumbnailPath,
            IsSitePage: false)));

    var results = SearchSiteCandidates(candidates, query)
        .Take(resultLimit)
        .Select(result => new
        {
            title = result.Title,
            url = result.Url,
            kind = result.Kind,
            thumbnailPath = result.ThumbnailPath
        })
        .ToArray();

    return Results.Ok(new { query, results });
}).DisableAntiforgery();

app.MapPost("/api/contact", async (ContactApiRequest request, IContactEmailService contactEmailService, IContactFormProtectionService protectionService, HttpContext httpContext) =>
{
    request = request with
    {
        Name = request.Name?.Trim() ?? string.Empty,
        Email = request.Email?.Trim() ?? string.Empty,
        Subject = request.Subject?.Trim() ?? string.Empty,
        Message = request.Message?.Trim() ?? string.Empty,
        Website = request.Website?.Trim() ?? string.Empty
    };

    if (!string.IsNullOrWhiteSpace(request.Website))
    {
        return Results.Ok(new { message = "Dankie! Jou boodskap is gestuur." });
    }

    var validationErrors = ValidateContactRequest(request);
    if (validationErrors.Count > 0)
    {
        return Results.ValidationProblem(validationErrors);
    }

    var clientId = httpContext.Connection.RemoteIpAddress?.ToString() ?? request.Email.ToLowerInvariant();
    if (!protectionService.TryValidateSubmission(clientId, request.Email, request.Subject, request.Message, out var protectionMessage))
    {
        return Results.BadRequest(new { message = protectionMessage });
    }

    await contactEmailService.SendContactEmailAsync(new ContactFormSubmission(request.Name, request.Email, request.Subject, request.Message), httpContext.RequestAborted);
    return Results.Ok(new { message = "Dankie! Jou boodskap is gestuur." });
}).RequireRateLimiting("contact-submit").DisableAntiforgery();

app.MapGet("/robots.txt", (HttpContext httpContext) =>
{
    var sitemapUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/sitemap.xml";
    var robots = $$"""
                   User-agent: *
                   Allow: /

                   Sitemap: {{sitemapUrl}}
                   """;

    return Results.Text(robots, "text/plain; charset=utf-8");
});

app.MapGet("/sitemap.xml", async (HttpContext httpContext, IStoryCatalogService storyCatalogService) =>
{
    var baseUri = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

    var paths = new List<string>
    {
        "/",
        "/gratis",
        "/luister",
        "/intekening-en-betaling",
        "/my-stories",
        "/opsies",
        "/meer-oor-ons",
        "/soek",
        "/teken-in",
        "/teken-op"
    };

    var freeStoriesTask = storyCatalogService.GetFreeStoriesAsync(httpContext.RequestAborted);
    var luisterStoriesTask = storyCatalogService.GetLuisterStoriesAsync(httpContext.RequestAborted);
    await Task.WhenAll(freeStoriesTask, luisterStoriesTask);

    paths.AddRange(freeStoriesTask.Result.Select(story => $"/gratis/{Uri.EscapeDataString(story.Slug)}"));
    paths.AddRange(luisterStoriesTask.Result.Select(story => $"/luister/{Uri.EscapeDataString(story.Slug)}"));

    XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
    var document = new XDocument(
        new XElement(ns + "urlset",
            paths.Select(path => new XElement(ns + "url",
                new XElement(ns + "loc", $"{baseUri}{path}")))));

    return Results.Content(document.ToString(SaveOptions.DisableFormatting), "application/xml; charset=utf-8");
});

var legacyFreeStorySlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
using (var scope = app.Services.CreateScope())
{
    var storyCatalogService = scope.ServiceProvider.GetRequiredService<IStoryCatalogService>();
    var freeStories = await storyCatalogService.GetFreeStoriesAsync();
    foreach (var story in freeStories)
    {
        if (!CanMapLegacyStorySlug(story.Slug))
        {
            continue;
        }

        legacyFreeStorySlugs.Add(story.Slug);
    }
}

foreach (var slug in legacyFreeStorySlugs)
{
    app.MapGet($"/{slug}", () => Results.Redirect($"/gratis/{slug}", permanent: false));
}

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

static string BuildContentSecurityPolicy(bool isDevelopment, string? postHogHostUrl)
{
    var postHogHostOrigin = TryGetCspHostOrigin(postHogHostUrl);
    var postHogAssetsOrigin = TryGetPostHogAssetsOrigin(postHogHostOrigin);
    var scriptSources = BuildScriptSources(postHogHostOrigin, postHogAssetsOrigin);
    var connectSources = isDevelopment
        ? "'self' https: http://localhost:* http://127.0.0.1:* ws://localhost:* ws://127.0.0.1:* wss:"
        : "'self' https: wss:";

    return $"default-src 'self'; base-uri 'self'; form-action 'self' https://sandbox.payfast.co.za https://www.payfast.co.za; object-src 'none'; frame-ancestors 'self'; img-src 'self' data: https:; media-src 'self' blob:; font-src 'self' data:; connect-src {connectSources}; script-src {scriptSources}; script-src-elem {scriptSources}; style-src 'self' 'unsafe-inline';";
}

static string? TryGetCspHostOrigin(string? url)
{
    if (string.IsNullOrWhiteSpace(url))
    {
        return null;
    }

    if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
        (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
    {
        return null;
    }

    return uri.IsDefaultPort ? $"{uri.Scheme}://{uri.Host}" : $"{uri.Scheme}://{uri.Host}:{uri.Port}";
}

static string BuildScriptSources(string? postHogHostOrigin, string? postHogAssetsOrigin)
{
    var sources = new List<string>
    {
        "'self'",
        "https://static.cloudflareinsights.com"
    };

    if (!string.IsNullOrWhiteSpace(postHogHostOrigin))
    {
        sources.Add(postHogHostOrigin);
    }

    if (!string.IsNullOrWhiteSpace(postHogAssetsOrigin) &&
        !string.Equals(postHogAssetsOrigin, postHogHostOrigin, StringComparison.OrdinalIgnoreCase))
    {
        sources.Add(postHogAssetsOrigin);
    }

    sources.Add("'unsafe-inline'");
    sources.Add("'unsafe-eval'");

    return string.Join(' ', sources);
}

static string? TryGetPostHogAssetsOrigin(string? postHogHostOrigin)
{
    if (string.IsNullOrWhiteSpace(postHogHostOrigin) ||
        !Uri.TryCreate(postHogHostOrigin, UriKind.Absolute, out var uri))
    {
        return null;
    }

    const string postHogHostSuffix = ".i.posthog.com";
    if (!uri.Host.EndsWith(postHogHostSuffix, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var hostPrefix = uri.Host[..^postHogHostSuffix.Length];
    if (string.IsNullOrWhiteSpace(hostPrefix))
    {
        return null;
    }

    if (hostPrefix.EndsWith("-assets", StringComparison.OrdinalIgnoreCase))
    {
        return postHogHostOrigin;
    }

    var assetsHost = $"{hostPrefix}-assets{postHogHostSuffix}";
    return uri.IsDefaultPort ? $"{uri.Scheme}://{assetsHost}" : $"{uri.Scheme}://{assetsHost}:{uri.Port}";
}

static bool IsLikelySameSiteRequest(HttpContext httpContext)
{
    var secFetchSite = httpContext.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.IsNullOrWhiteSpace(secFetchSite) &&
        !string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(secFetchSite, "same-site", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var refererValue = httpContext.Request.Headers.Referer.ToString();
    if (string.IsNullOrWhiteSpace(refererValue))
    {
        return true;
    }

    if (!Uri.TryCreate(refererValue, UriKind.Absolute, out var refererUri))
    {
        return false;
    }

    return string.Equals(refererUri.Host, httpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
}

static bool IsLikelySameSiteMediaRequest(HttpContext httpContext)
{
    var secFetchSite = httpContext.Request.Headers["Sec-Fetch-Site"].ToString();
    if (!string.IsNullOrWhiteSpace(secFetchSite))
    {
        return string.Equals(secFetchSite, "same-origin", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(secFetchSite, "same-site", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(secFetchSite, "none", StringComparison.OrdinalIgnoreCase);
    }

    var originValue = httpContext.Request.Headers.Origin.ToString();
    if (string.IsNullOrWhiteSpace(originValue))
    {
        return true;
    }

    if (!Uri.TryCreate(originValue, UriKind.Absolute, out var originUri))
    {
        return false;
    }

    return string.Equals(originUri.Host, httpContext.Request.Host.Host, StringComparison.OrdinalIgnoreCase);
}

static bool IsBlockedPublicAudioPath(PathString path)
{
    var value = path.Value;
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    if (!value.StartsWith("/stories/", StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    var extension = Path.GetExtension(value);
    return string.Equals(extension, ".mpeg", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".m4a", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase)
        || string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase);
}

static void ApplyAudioResponseSecurityHeaders(HttpContext httpContext)
{
    httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.Expires = "0";
    httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
    httpContext.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    httpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
    httpContext.Response.Headers["Content-Disposition"] = "inline";
}

static async Task<IResult> ProxyAudioFromOriginAsync(
    HttpContext httpContext,
    IHttpClientFactory httpClientFactory,
    Uri sourceUri,
    string? configuredContentType,
    string? audioObjectKey)
{
    try
    {
        using var upstreamRequest = new HttpRequestMessage(HttpMethod.Get, sourceUri);
        var requestRange = httpContext.Request.Headers.Range.ToString();
        if (!string.IsNullOrWhiteSpace(requestRange) &&
            RangeHeaderValue.TryParse(requestRange, out var parsedRange))
        {
            upstreamRequest.Headers.Range = parsedRange;
        }

        upstreamRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("audio/*"));

        using var upstreamResponse = await httpClientFactory.CreateClient("audio-origin")
            .SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, httpContext.RequestAborted);
        if (upstreamResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        {
            return Results.NotFound();
        }

        if (upstreamResponse.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            httpContext.Response.StatusCode = StatusCodes.Status416RangeNotSatisfiable;
            if (upstreamResponse.Content.Headers.ContentRange is not null)
            {
                httpContext.Response.Headers.ContentRange = upstreamResponse.Content.Headers.ContentRange.ToString();
            }

            return Results.Empty;
        }

        if (!upstreamResponse.IsSuccessStatusCode)
        {
            return Results.StatusCode(StatusCodes.Status502BadGateway);
        }

        httpContext.Response.StatusCode = (int)upstreamResponse.StatusCode;
        if (upstreamResponse.Headers.AcceptRanges.Count > 0)
        {
            httpContext.Response.Headers.AcceptRanges = string.Join(", ", upstreamResponse.Headers.AcceptRanges);
        }
        else
        {
            httpContext.Response.Headers.AcceptRanges = "bytes";
        }

        if (upstreamResponse.Content.Headers.ContentRange is not null)
        {
            httpContext.Response.Headers.ContentRange = upstreamResponse.Content.Headers.ContentRange.ToString();
        }

        if (upstreamResponse.Content.Headers.ContentLength.HasValue)
        {
            httpContext.Response.ContentLength = upstreamResponse.Content.Headers.ContentLength.Value;
        }

        var upstreamContentType = upstreamResponse.Content.Headers.ContentType?.MediaType;
        httpContext.Response.ContentType = ResolveAudioMimeType(
            string.IsNullOrWhiteSpace(configuredContentType) ? upstreamContentType : configuredContentType,
            audioObjectKey);

        await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(httpContext.RequestAborted);
        await upstreamStream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        return Results.Empty;
    }
    catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
    {
        // Client disconnected while stream was in progress.
        return Results.Empty;
    }
    catch (IOException) when (httpContext.RequestAborted.IsCancellationRequested)
    {
        // Browser dropped the connection; treat as expected cancellation.
        return Results.Empty;
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException)
    {
        return Results.StatusCode(StatusCodes.Status502BadGateway);
    }
}

static bool TryResolveStoryPathTarget(PathString path, out string source, out string slug)
{
    source = string.Empty;
    slug = string.Empty;
    var value = path.Value;
    return StoryAccessPolicy.TryParseStoryPath(value, out source, out slug);
}

static async Task<bool> HasRequiredStoryAccessAsync(
    ISubscriptionLedgerService subscriptionLedgerService,
    string? email,
    StoryAccessRequirement requirement,
    CancellationToken cancellationToken = default)
{
    if (!StoryAccessPolicy.RequiresPaidSubscription(requirement))
    {
        return true;
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        return false;
    }

    var tierCodes = StoryAccessPolicy.GetAllowedTierCodes(requirement);
    if (tierCodes.Count == 0)
    {
        return false;
    }

    var checks = tierCodes
        .Select(tierCode => subscriptionLedgerService.HasActiveSubscriptionForTierAsync(email, tierCode, cancellationToken))
        .ToArray();

    var results = await Task.WhenAll(checks);
    return results.Any(result => result);
}

static string BuildOpsiesStoryRedirectPath(string requestedPath)
{
    var query = new Dictionary<string, string?>
    {
        ["returnUrl"] = requestedPath
    };

    return QueryHelpers.AddQueryString("/opsies", query);
}

static string? GetSafeStoryReturnUrl(string? returnUrl)
{
    var normalized = NormalizeReturnPathCandidate(returnUrl);
    if (string.IsNullOrWhiteSpace(normalized))
    {
        return null;
    }

    if (!normalized.StartsWith("/", StringComparison.Ordinal) ||
        normalized.StartsWith("//", StringComparison.Ordinal))
    {
        return null;
    }

    return StoryAccessPolicy.TryParseStoryPath(normalized, out _, out _)
        ? normalized
        : null;
}

static string? NormalizeReturnPathCandidate(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var candidate = value.Trim();
    for (var index = 0; index < 2; index++)
    {
        if (candidate.StartsWith("/", StringComparison.Ordinal))
        {
            break;
        }

        if (!candidate.Contains('%', StringComparison.Ordinal))
        {
            break;
        }

        try
        {
            candidate = Uri.UnescapeDataString(candidate);
        }
        catch
        {
            break;
        }
    }

    return candidate;
}

static bool IsGratisPath(PathString path)
{
    var value = path.Value;
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    return string.Equals(value, "/gratis", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("/gratis/", StringComparison.OrdinalIgnoreCase);
}

static string ResolveGratisRedirectPath(PathString path, QueryString queryString)
{
    var value = path.Value;
    if (string.IsNullOrWhiteSpace(value))
    {
        return "/luister";
    }

    var mappedPath = string.Equals(value, "/gratis", StringComparison.OrdinalIgnoreCase)
        ? "/luister"
        : value.StartsWith("/gratis/", StringComparison.OrdinalIgnoreCase)
            ? $"/luister/{value["/gratis/".Length..]}"
            : "/luister";

    var query = queryString.HasValue ? queryString.Value : string.Empty;
    return $"{mappedPath}{query}";
}

static bool HasHomeOverrideQuery(IQueryCollection query)
{
    if (!query.TryGetValue("home", out var value))
    {
        return false;
    }

    var token = value.ToString();
    return string.Equals(token, "1", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(token, "true", StringComparison.OrdinalIgnoreCase);
}

static bool TryNormalizeStorySlug(string? slug, out string normalizedSlug)
{
    normalizedSlug = string.Empty;
    if (string.IsNullOrWhiteSpace(slug))
    {
        return false;
    }

    var candidate = slug.Trim().ToLowerInvariant();
    if (candidate.Length is < 3 or > 180)
    {
        return false;
    }

    if (candidate[0] == '-' || candidate[^1] == '-')
    {
        return false;
    }

    var previousWasDash = false;
    foreach (var character in candidate)
    {
        if (character == '-')
        {
            if (previousWasDash)
            {
                return false;
            }

            previousWasDash = true;
            continue;
        }

        previousWasDash = false;
        if (!char.IsAsciiLetterOrDigit(character) || char.IsUpper(character))
        {
            return false;
        }
    }

    normalizedSlug = candidate;
    return true;
}

static string NormalizeStoryTrackingSource(string? source, string? storyPath)
{
    if (!string.IsNullOrWhiteSpace(source))
    {
        var normalizedSource = source.Trim().ToLowerInvariant();
        if (normalizedSource is "gratis" or "luister")
        {
            return normalizedSource;
        }
    }

    var normalizedPath = ResolveOptionalTrackingPath(storyPath);
    if (normalizedPath is not null)
    {
        if (normalizedPath.StartsWith("/gratis/", StringComparison.OrdinalIgnoreCase))
        {
            return "gratis";
        }

        if (normalizedPath.StartsWith("/luister/", StringComparison.OrdinalIgnoreCase))
        {
            return "luister";
        }
    }

    return "unknown";
}

static string ResolveStoryTrackingPath(string? storyPath, string storySlug, string source)
{
    var normalizedPath = ResolveOptionalTrackingPath(storyPath);
    if (normalizedPath is not null)
    {
        return normalizedPath;
    }

    return source switch
    {
        "gratis" => $"/gratis/{Uri.EscapeDataString(storySlug)}",
        "luister" => $"/luister/{Uri.EscapeDataString(storySlug)}",
        _ => $"/luister/{Uri.EscapeDataString(storySlug)}"
    };
}

static string? ResolveOptionalTrackingPath(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    var candidate = value.Trim();
    if (!candidate.StartsWith("/", StringComparison.Ordinal) ||
        candidate.StartsWith("//", StringComparison.Ordinal))
    {
        return null;
    }

    if (candidate.Length > 256)
    {
        candidate = candidate[..256];
    }

    return candidate;
}

static decimal NormalizeListenSeconds(decimal? listenedSeconds)
{
    var value = listenedSeconds ?? 0m;
    if (value <= 0m)
    {
        return 0m;
    }

    return decimal.Round(Math.Clamp(value, 0m, 3600m), 3, MidpointRounding.AwayFromZero);
}

static decimal? NormalizeOptionalSeconds(decimal? value)
{
    if (value is null || value <= 0m)
    {
        return null;
    }

    return decimal.Round(Math.Clamp(value.Value, 0m, 43200m), 3, MidpointRounding.AwayFromZero);
}

static decimal? NormalizeOptionalDuration(decimal? value)
{
    if (value is null || value <= 0m)
    {
        return null;
    }

    return decimal.Round(Math.Clamp(value.Value, 0m, 43200m), 3, MidpointRounding.AwayFromZero);
}

static string NormalizeStoryListenEventType(string? eventType)
{
    if (string.IsNullOrWhiteSpace(eventType))
    {
        return "progress";
    }

    return eventType.Trim().ToLowerInvariant() switch
    {
        "progress" => "progress",
        "pause" => "pause",
        "ended" => "ended",
        "pagehide" => "pagehide",
        "visibilityhidden" => "visibilityhidden",
        _ => "progress"
    };
}

static bool TryResolvePaymentProvider(string? provider, out string resolvedProvider)
{
    if (string.IsNullOrWhiteSpace(provider))
    {
        resolvedProvider = "paystack";
        return true;
    }

    if (string.Equals(provider, "paystack", StringComparison.OrdinalIgnoreCase))
    {
        resolvedProvider = "paystack";
        return true;
    }

    if (string.Equals(provider, "payfast", StringComparison.OrdinalIgnoreCase))
    {
        resolvedProvider = "payfast";
        return true;
    }

    resolvedProvider = string.Empty;
    return false;
}

static bool CanMapLegacyStorySlug(string? slug)
{
    if (string.IsNullOrWhiteSpace(slug))
    {
        return false;
    }

    if (slug.IndexOf('/') >= 0)
    {
        return false;
    }

    if (slug.Contains('.', StringComparison.Ordinal))
    {
        return false;
    }

    var normalized = slug.Trim().ToLowerInvariant();
    return normalized switch
    {
        "api" => false,
        "betaal" => false,
        "error" => false,
        "gratis" => false,
        "intekening-en-betaling" => false,
        "intekening-en-betaaling" => false,
        "luister" => false,
        "media" => false,
        "meer-oor-ons" => false,
        "not-found" => false,
        "opsies" => false,
        "robots.txt" => false,
        "sitemap.xml" => false,
        "soek" => false,
        "teken-in" => false,
        "teken-op" => false,
        "teken-uit" => false,
        _ => true
    };
}

static bool TryResolveLocalAudioPath(string contentRootPath, string? audioObjectKey, out string audioFilePath)
{
    audioFilePath = string.Empty;
    if (string.IsNullOrWhiteSpace(audioObjectKey))
    {
        return false;
    }

    var storiesRoot = Path.GetFullPath(Path.Combine(contentRootPath, "Stories"));
    var storiesRootWithSeparator = storiesRoot.EndsWith(Path.DirectorySeparatorChar)
        ? storiesRoot
        : $"{storiesRoot}{Path.DirectorySeparatorChar}";
    var combinedPath = Path.Combine(storiesRoot, audioObjectKey.Replace('/', Path.DirectorySeparatorChar));
    var fullPath = Path.GetFullPath(combinedPath);
    var isInsideStoriesRoot = fullPath.StartsWith(storiesRootWithSeparator, StringComparison.OrdinalIgnoreCase)
        || string.Equals(fullPath, storiesRoot, StringComparison.OrdinalIgnoreCase);
    if (!isInsideStoriesRoot)
    {
        return false;
    }

    audioFilePath = fullPath;
    return true;
}

static bool TryBuildR2AudioUri(string? publicBaseUrl, string? audioObjectKey, out Uri sourceUri)
{
    sourceUri = default!;
    if (string.IsNullOrWhiteSpace(publicBaseUrl) ||
        string.IsNullOrWhiteSpace(audioObjectKey))
    {
        return false;
    }

    if (!Uri.TryCreate(publicBaseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
        baseUri is null)
    {
        return false;
    }

    if (!string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    if (Uri.TryCreate(audioObjectKey.Trim(), UriKind.Absolute, out var absoluteUri) &&
        absoluteUri is not null)
    {
        if (!string.Equals(absoluteUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(absoluteUri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sourceUri = absoluteUri;
        return true;
    }

    var normalizedPath = audioObjectKey
        .Replace('\\', '/')
        .Trim()
        .TrimStart('/');
    if (string.IsNullOrWhiteSpace(normalizedPath))
    {
        return false;
    }

    var segments = normalizedPath
        .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (segments.Any(segment => string.Equals(segment, "..", StringComparison.Ordinal)))
    {
        return false;
    }

    var encodedPath = string.Join('/', segments.Select(Uri.EscapeDataString));
    sourceUri = new Uri(baseUri, encodedPath);
    return true;
}

static string ResolveAudioMimeType(string? configuredContentType, string? audioObjectKey)
{
    if (!string.IsNullOrWhiteSpace(configuredContentType))
    {
        return configuredContentType.Trim();
    }

    var extension = Path.GetExtension(audioObjectKey ?? string.Empty).ToLowerInvariant();
    return extension switch
    {
        ".mp3" => "audio/mpeg",
        ".mpeg" => "audio/mpeg",
        ".m4a" => "audio/mp4",
        ".wav" => "audio/wav",
        ".ogg" => "audio/ogg",
        _ => "audio/mpeg"
    };
}

static async Task<AuthCookieSignInResult> SignInUserAsync(HttpContext httpContext, string email, IAuthSessionService authSessionService)
{
    var sessionIssueResult = await authSessionService.IssueSessionAsync(
        email,
        httpContext.Request.Headers.UserAgent.ToString(),
        httpContext.Connection.RemoteIpAddress?.ToString(),
        httpContext.RequestAborted);
    if (!sessionIssueResult.IsSuccess || sessionIssueResult.SessionId == Guid.Empty)
    {
        return new AuthCookieSignInResult(
            false,
            sessionIssueResult.ErrorMessage ?? "Kon nie nou jou sessie begin nie. Probeer asseblief weer.");
    }

    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, email),
        new(ClaimTypes.Name, email),
        new(ClaimTypes.Email, email),
        new(AuthSessionIdClaimType, sessionIssueResult.SessionId.ToString("D"))
    };

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    var authProperties = new AuthenticationProperties
    {
        IsPersistent = true,
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(sessionIssueResult.SessionLifetimeDays)
    };

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
    return new AuthCookieSignInResult(true);
}

static async Task RevokeCurrentUserSessionAsync(HttpContext httpContext, IAuthSessionService authSessionService)
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        return;
    }

    var signedInEmail = httpContext.User.FindFirst(ClaimTypes.Email)?.Value
                       ?? httpContext.User.Identity?.Name;
    var sessionIdValue = httpContext.User.FindFirst(AuthSessionIdClaimType)?.Value;
    if (string.IsNullOrWhiteSpace(signedInEmail) ||
        !Guid.TryParse(sessionIdValue, out var sessionId))
    {
        return;
    }

    await authSessionService.RevokeSessionAsync(signedInEmail, sessionId, httpContext.RequestAborted);
}

static int NormalizeSessionLifetimeDays(int sessionLifetimeDays)
{
    return Math.Clamp(sessionLifetimeDays, 1, 90);
}

static async Task<string> ResolvePostAuthRedirectPathAsync(
    ISubscriptionLedgerService subscriptionLedgerService,
    string? email,
    CancellationToken cancellationToken)
{
    var hasPaidSubscription = await subscriptionLedgerService.HasActivePaidSubscriptionAsync(email, cancellationToken);
    return hasPaidSubscription ? "/luister" : "/gratis";
}

static string? ValidateSignInRequest(string? email, string? password)
{
    if (string.IsNullOrWhiteSpace(email) || email.Length > 254 || !IsValidEmail(email))
    {
        return "Gebruik asseblief 'n geldige e-posadres.";
    }

    if (string.IsNullOrWhiteSpace(password) || password.Length is < 6 or > 200)
    {
        return "Jou wagwoord moet tussen 6 en 200 karakters wees.";
    }

    return null;
}

static string? ValidateSignUpRequest(AuthSignUpApiRequest request)
{
    if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 80)
    {
        return "Vul asseblief jou voornaam in.";
    }

    if (!string.IsNullOrWhiteSpace(request.LastName) && request.LastName.Length > 80)
    {
        return "Jou van mag nie langer as 80 karakters wees nie.";
    }

    if (!string.IsNullOrWhiteSpace(request.DisplayName) && request.DisplayName.Length > 120)
    {
        return "Jou publieke naam mag nie langer as 120 karakters wees nie.";
    }

    if (!string.IsNullOrWhiteSpace(request.MobileNumber) && !IsValidMobileNumber(request.MobileNumber))
    {
        return "Gebruik asseblief 'n geldige selfoonnommer.";
    }

    return ValidateSignInRequest(request.Email, request.Password);
}

static IReadOnlyList<SearchSiteCandidate> BuildSearchStaticCandidates() =>
[
    new(
        Title: "Tuis",
        Description: "Schink Stories tuisblad met gratis stories, nuutste stories en kontak.",
        Url: "/",
        Kind: "Bladsy",
        Keywords: "tuis huis kontak planne afrikaans oudiostories",
        ThumbnailPath: "/branding/schink-logo-green.png",
        IsSitePage: true),
    new(
        Title: "Gratis stories",
        Description: "Drie gratis Afrikaanse stories vir jou gesin.",
        Url: "/gratis",
        Kind: "Bladsy",
        Keywords: "gratis luister probeer stories",
        ThumbnailPath: "/stories/Schink_Stories_Gratis_Blad_Banner.webp",
        IsSitePage: true),
    new(
        Title: "Alle stories",
        Description: "Volledige versameling stories op Schink Stories.",
        Url: "/luister",
        Kind: "Bladsy",
        Keywords: "alle stories intekening luister",
        ThumbnailPath: "/branding/DIS_STORIETYD.png",
        IsSitePage: true),
    new(
        Title: "My stories",
        Description: "Jou persoonlike lys van stories wat jy reeds geluister het of nog besig is.",
        Url: "/my-stories",
        Kind: "Bladsy",
        Keywords: "my stories geluister vordering voortgaan",
        ThumbnailPath: "/branding/schink-logo-green.png",
        IsSitePage: true),
    new(
        Title: "Meer oor Ons",
        Description: "Lees oor Schink Stories se missie, visie, waardes en belofte.",
        Url: "/meer-oor-ons",
        Kind: "Bladsy",
        Keywords: "wie ons is missie visie waardes belofte ouers",
        ThumbnailPath: "/branding/Schink_Die_Ware_Wenner_Schink_Stories_600x600.png",
        IsSitePage: true),
    new(
        Title: "Intekening en betaling",
        Description: "Bestuur jou Schink Stories intekening en betaalopsies.",
        Url: "/intekening-en-betaling",
        Kind: "Bladsy",
        Keywords: "intekening betaling planne opsies rekening",
        ThumbnailPath: "/branding/schink-logo-green.png",
        IsSitePage: true),
    new(
        Title: "Opsies",
        Description: "Vergelyk planne en kies die beste opsie vir jou gesin.",
        Url: "/opsies",
        Kind: "Bladsy",
        Keywords: "pryse planne betaal intekening",
        ThumbnailPath: "/branding/Schink_Stories_Home_Banner_White.png",
        IsSitePage: true),
    new(
        Title: "Teken in",
        Description: "Teken in op jou Schink Stories rekening.",
        Url: "/teken-in",
        Kind: "Bladsy",
        Keywords: "login rekening registreer",
        ThumbnailPath: "/branding/schink-logo-text.png",
        IsSitePage: true),
    new(
        Title: "Teken op",
        Description: "Skep jou Schink Stories rekening.",
        Url: "/teken-op",
        Kind: "Bladsy",
        Keywords: "signup registreer skep rekening",
        ThumbnailPath: "/branding/schink-logo-text.png",
        IsSitePage: true)
];

static IReadOnlyList<SearchSiteResult> SearchSiteCandidates(IEnumerable<SearchSiteCandidate> candidates, string query)
{
    var normalizedQuery = NormalizeForSearch(query);
    if (string.IsNullOrWhiteSpace(normalizedQuery))
    {
        return Array.Empty<SearchSiteResult>();
    }

    var queryTerms = normalizedQuery
        .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.Ordinal)
        .ToArray();
    if (queryTerms.Length == 0)
    {
        return Array.Empty<SearchSiteResult>();
    }

    var results = new List<SearchSiteResult>();
    foreach (var candidate in candidates)
    {
        var normalizedTitle = NormalizeForSearch(candidate.Title);
        var normalizedBody = NormalizeForSearch($"{candidate.Description} {candidate.Keywords}");
        var normalizedContent = $"{normalizedTitle} {normalizedBody}";
        if (!queryTerms.All(term => normalizedContent.Contains(term, StringComparison.Ordinal)))
        {
            continue;
        }

        var score = 0;
        if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 140;
        }

        if (normalizedBody.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            score += 70;
        }

        foreach (var term in queryTerms)
        {
            if (normalizedTitle.Contains(term, StringComparison.Ordinal))
            {
                score += 24;
            }

            if (normalizedBody.Contains(term, StringComparison.Ordinal))
            {
                score += 10;
            }
        }

        if (candidate.IsSitePage)
        {
            score += 4;
        }

        results.Add(new SearchSiteResult(
            Title: candidate.Title,
            Description: candidate.Description,
            Url: candidate.Url,
            Kind: candidate.Kind,
            ThumbnailPath: candidate.ThumbnailPath,
            Score: score));
    }

    return results
        .OrderByDescending(result => result.Score)
        .ThenBy(result => result.Title, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static string NormalizeForSearch(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    var normalized = value.Normalize(NormalizationForm.FormD);
    var builder = new StringBuilder(normalized.Length);

    foreach (var character in normalized)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
        {
            builder.Append(char.ToLowerInvariant(character));
        }
    }

    return builder.ToString().Normalize(NormalizationForm.FormC);
}

static Dictionary<string, string[]> ValidateContactRequest(ContactApiRequest request)
{
    var errors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Length > 120)
    {
        errors["name"] = ["Vul asseblief jou naam in."];
    }

    if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !IsValidEmail(request.Email))
    {
        errors["email"] = ["Gebruik asseblief 'n geldige e-posadres."];
    }

    if (string.IsNullOrWhiteSpace(request.Subject) || request.Subject.Length > 140)
    {
        errors["subject"] = ["Vul asseblief 'n onderwerp in."];
    }

    if (string.IsNullOrWhiteSpace(request.Message) || request.Message.Length > 4000)
    {
        errors["message"] = ["Vul asseblief jou boodskap in."];
    }

    return errors;
}

static bool IsValidEmail(string value)
{
    try
    {
        _ = new MailAddress(value);
        return true;
    }
    catch
    {
        return false;
    }
}

static bool IsValidMobileNumber(string value)
{
    var sanitized = value.Replace(" ", string.Empty)
        .Replace("-", string.Empty)
        .Replace("(", string.Empty)
        .Replace(")", string.Empty);

    return Regex.IsMatch(sanitized, @"^\+?[0-9]{7,20}$", RegexOptions.CultureInvariant);
}

sealed record ContactApiRequest(string? Name, string? Email, string? Subject, string? Message, string? Website);
sealed record AuthSignInApiRequest(string? Email, string? Password);
sealed record AuthSignUpApiRequest(
    string? FirstName,
    string? LastName,
    string? DisplayName,
    string? Email,
    string? MobileNumber,
    string? Password);
sealed record StoryViewTrackApiRequest(string? StoryPath, string? Source, string? ReferrerPath);
sealed record StoryListenTrackApiRequest(
    string? StoryPath,
    string? Source,
    string? SessionId,
    string? EventType,
    decimal? ListenedSeconds,
    decimal? PositionSeconds,
    decimal? DurationSeconds,
    bool? IsCompleted);
sealed record SearchSiteCandidate(
    string Title,
    string Description,
    string Url,
    string Kind,
    string Keywords,
    string ThumbnailPath,
    bool IsSitePage);
sealed record SearchSiteResult(
    string Title,
    string Description,
    string Url,
    string Kind,
    string ThumbnailPath,
    int Score);
sealed record AuthCookieSignInResult(bool IsSuccess, string? ErrorMessage = null);
