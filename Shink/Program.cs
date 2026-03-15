using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Shink.Components;
using Shink.Components.Content;
using Shink.Services;
using System.Globalization;
using System.Net.Mail;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Xml.Linq;

var builder = WebApplication.CreateBuilder(args);

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
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAudioAccessService, AudioAccessService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.Configure<ResendOptions>(builder.Configuration.GetSection(ResendOptions.SectionName));
builder.Services.Configure<SupabaseOptions>(builder.Configuration.GetSection(SupabaseOptions.SectionName));
builder.Services.Configure<PayFastOptions>(builder.Configuration.GetSection(PayFastOptions.SectionName));
builder.Services.Configure<PaystackOptions>(builder.Configuration.GetSection(PaystackOptions.SectionName));
builder.Services.AddHttpClient<IContactEmailService, ResendContactEmailService>();
builder.Services.AddHttpClient<ISupabaseAuthService, SupabaseAuthService>();
builder.Services.AddHttpClient<IStoryCatalogService, SupabaseStoryCatalogService>();
builder.Services.AddHttpClient<PayFastCheckoutService>();
builder.Services.AddHttpClient<PaystackCheckoutService>();
builder.Services.AddHttpClient<ISubscriptionLedgerService, SupabaseSubscriptionLedgerService>();
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
    if (IsStoryDetailPath(httpContext.Request.Path) &&
        !(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        var requestedPath = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
        var returnUrl = Uri.EscapeDataString(requestedPath);
        httpContext.Response.Redirect($"/teken-in?returnUrl={returnUrl}");
        return;
    }

    await next();
});

app.Use(async (httpContext, next) =>
{
    httpContext.Response.OnStarting(() =>
    {
        var headers = httpContext.Response.Headers;
        var contentSecurityPolicy = app.Environment.IsDevelopment()
            ? "default-src 'self'; base-uri 'self'; form-action 'self' https://sandbox.payfast.co.za https://www.payfast.co.za; object-src 'none'; frame-ancestors 'self'; img-src 'self' data: https:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self' https: http://localhost:* http://127.0.0.1:* ws://localhost:* ws://127.0.0.1:* wss:; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline';"
            : "default-src 'self'; base-uri 'self'; form-action 'self' https://sandbox.payfast.co.za https://www.payfast.co.za; object-src 'none'; frame-ancestors 'self'; img-src 'self' data: https:; media-src 'self' blob:; font-src 'self' data:; connect-src 'self' https: wss:; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline';";

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

app.MapGet("/media/audio/{slug}", async (
    string slug,
    string? token,
    IAudioAccessService audioAccessService,
    IStoryCatalogService storyCatalogService,
    IWebHostEnvironment environment,
    HttpContext httpContext) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        return Results.Unauthorized();
    }

    if (!IsLikelySameSiteAudioRequest(httpContext))
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

    if (!string.Equals(story.AudioProvider, "local", StringComparison.OrdinalIgnoreCase))
    {
        // R2 streaming will be resolved from the same metadata table in a follow-up step.
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

    httpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
    httpContext.Response.Headers.Pragma = "no-cache";
    httpContext.Response.Headers.Expires = "0";
    httpContext.Response.Headers["X-Content-Type-Options"] = "nosniff";
    httpContext.Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    httpContext.Response.Headers["X-Robots-Tag"] = "noindex, nofollow, noarchive, nosnippet";
    httpContext.Response.Headers["Content-Disposition"] = "inline";

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
    PayFastCheckoutService payFastCheckoutService,
    PaystackCheckoutService paystackCheckoutService,
    ISubscriptionLedgerService subscriptionLedgerService,
    HttpContext httpContext,
    ILogger<Program> logger) =>
{
    if (!(httpContext.User.Identity?.IsAuthenticated ?? false))
    {
        var checkoutPath = $"{httpContext.Request.Path}{httpContext.Request.QueryString}";
        var returnUrl = Uri.EscapeDataString(checkoutPath);
        return Results.Redirect($"/teken-in?returnUrl={returnUrl}");
    }

    var plan = PaymentPlanCatalog.FindBySlug(planSlug);
    if (plan is null)
    {
        return Results.NotFound();
    }

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

        var duplicateRedirectPath = $"/opsies?betaling=reeds-ingeteken&tier={Uri.EscapeDataString(plan.TierCode)}";
        return Results.Redirect(duplicateRedirectPath);
    }

    if (!TryResolvePaymentProvider(provider, out var selectedProvider))
    {
        return Results.BadRequest(new { message = "Ongeldige betaalverskaffer. Gebruik 'paystack' of 'payfast'." });
    }

    if (string.Equals(selectedProvider, "paystack", StringComparison.OrdinalIgnoreCase))
    {
        var checkoutResult = await paystackCheckoutService.InitializeCheckoutAsync(plan, httpContext, httpContext.RequestAborted);
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

    if (!payFastCheckoutService.TryBuildCheckout(plan, httpContext, out var checkoutForm, out var errorMessage))
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
    await SignInUserAsync(httpContext, signedInEmail);
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

    await SignInUserAsync(httpContext, signedInEmail);
    var redirectPath = await ResolvePostAuthRedirectPathAsync(subscriptionLedgerService, signedInEmail, httpContext.RequestAborted);
    return Results.Ok(new
    {
        message = profileStored
            ? "Welkom! Jou rekening is geskep en jy is nou ingeteken."
            : "Welkom! Jou rekening is geskep en jy is nou ingeteken. Ons kon nie al jou profielbesonderhede nou stoor nie.",
        redirectPath
    });
}).RequireRateLimiting("auth-submit").DisableAntiforgery();

app.MapPost("/api/auth/logout", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Ok(new { message = "Jy is nou afgeteken." });
}).RequireRateLimiting("auth-submit").DisableAntiforgery();

app.MapGet("/teken-uit", async (HttpContext httpContext) =>
{
    await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/");
}).RequireRateLimiting("auth-submit");

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

static bool IsLikelySameSiteAudioRequest(HttpContext httpContext)
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

static bool IsStoryDetailPath(PathString path)
{
    var value = path.Value;
    if (string.IsNullOrWhiteSpace(value))
    {
        return false;
    }

    var prefix = value.StartsWith("/gratis/", StringComparison.OrdinalIgnoreCase)
        ? "/gratis/"
        : value.StartsWith("/luister/", StringComparison.OrdinalIgnoreCase)
            ? "/luister/"
            : null;

    if (prefix is null)
    {
        return false;
    }

    var remainder = value[prefix.Length..].Trim('/');
    return !string.IsNullOrWhiteSpace(remainder);
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
    var combinedPath = Path.Combine(storiesRoot, audioObjectKey.Replace('/', Path.DirectorySeparatorChar));
    var fullPath = Path.GetFullPath(combinedPath);
    if (!fullPath.StartsWith(storiesRoot, StringComparison.OrdinalIgnoreCase))
    {
        return false;
    }

    audioFilePath = fullPath;
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

static async Task SignInUserAsync(HttpContext httpContext, string email)
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.NameIdentifier, email),
        new(ClaimTypes.Name, email),
        new(ClaimTypes.Email, email)
    };

    var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    var authProperties = new AuthenticationProperties
    {
        IsPersistent = true,
        AllowRefresh = true,
        ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14)
    };

    await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, authProperties);
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
        ThumbnailPath: "/branding/Schink_Stories_Kom_Luister_Saam.webp",
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
