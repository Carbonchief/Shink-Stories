using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;

namespace Shink.Services;

public static class ProfileAvatarEndpoints
{
    public static async Task<IResult> SaveMobileAsync(
        HttpContext context, MobileAvatarSelectionRequest request, ISubscriberAvatarService avatarService)
    {
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(email))
            return Results.Unauthorized();

        if (!string.Equals(email, request.ExpectedAccountEmail?.Trim(), StringComparison.OrdinalIgnoreCase))
            return Results.Conflict(new { message = "Jou rekening het verander. Maak die profielprentjies weer oop." });

        var avatar = ProfileAvatarCatalog.Find(request.AvatarId);
        if (avatar is null && request.UseRandomIfUnselected && string.IsNullOrWhiteSpace(request.AvatarId))
            avatar = ProfileAvatarCatalog.All[Random.Shared.Next(ProfileAvatarCatalog.All.Count)];
        if (avatar is null)
            return Results.BadRequest(new { message = "Kies asseblief 'n profielprentjie." });

        var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null)
            return Results.Unauthorized();

        if (!await avatarService.UpdateAsync(email, avatar, context.RequestAborted))
            return Results.Json(new { message = "Ons kon nie jou prentjie stoor nie. Probeer asseblief weer." }, statusCode: 503);

        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            WithAvatar(auth.Principal, avatar), auth.Properties);
        return Results.Ok(new MobileAvatarSelectionResponse("Profielprentjie gestoor.", avatar.Id, avatar.ImageUrl));
    }

    public static void MapProfileAvatarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/account/avatar", SaveAsync)
            .RequireAuthorization()
            .RequireRateLimiting("auth-submit");
    }

    public static async Task<IResult> SaveAsync(
        HttpContext context, IAntiforgery antiforgery, ISubscriberAvatarService avatarService)
    {
        var email = context.User.FindFirst(ClaimTypes.Email)?.Value;
        if (context.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(email))
        {
            return Results.Unauthorized();
        }

        if (!context.Request.HasFormContentType)
        {
            return Results.BadRequest();
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("Die blad het verval. Herlaai dit en probeer weer.");
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var isEditing = string.Equals(form["mode"], "edit", StringComparison.Ordinal);
        var returnPath = isEditing ? ProfileAvatarCatalog.SafeReturnPath(form["returnUrl"]) : "/luister";
        var requestedAvatarId = form["avatarId"].ToString();
        var avatar = ProfileAvatarCatalog.Find(requestedAvatarId);
        if (!isEditing && string.IsNullOrWhiteSpace(requestedAvatarId))
        {
            avatar = ProfileAvatarCatalog.All[Random.Shared.Next(ProfileAvatarCatalog.All.Count)];
        }

        var selectedId = avatar?.Id;
        var auth = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!auth.Succeeded || auth.Principal is null)
        {
            return Results.Unauthorized();
        }

        if (avatar is null || !await avatarService.UpdateAsync(email, avatar, context.RequestAborted))
        {
            return Results.LocalRedirect(QueryHelpers.AddQueryString(ProfileAvatarCatalog.PagePath,
                new Dictionary<string, string?>
                {
                    ["returnUrl"] = returnPath,
                    ["mode"] = isEditing ? "edit" : null,
                    ["error"] = avatar is null ? "selection" : "save",
                    ["selected"] = selectedId
                }));
        }

        // Refresh the existing cookie: keep its session, roles, persistence and expiry.
        var principal = WithAvatar(auth.Principal, avatar);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, auth.Properties);
        return Results.LocalRedirect(returnPath);
    }

    public static ClaimsPrincipal WithAvatar(ClaimsPrincipal principal, ProfileAvatar avatar)
    {
        var updated = new ClaimsPrincipal(principal.Identities.Select(identity => identity.Clone()));
        foreach (var identity in updated.Identities)
        {
            foreach (var claim in identity.Claims.Where(claim =>
                         claim.Type is "profile_image_url" or "avatar_url" or "picture" or "photo_url").ToArray())
            {
                identity.RemoveClaim(claim);
            }
        }

        var primaryIdentity = (ClaimsIdentity)updated.Identity!;
        primaryIdentity.AddClaim(new Claim("profile_image_url", avatar.ImageUrl));
        primaryIdentity.AddClaim(new Claim("avatar_url", avatar.ImageUrl));
        return updated;
    }
}

public sealed record MobileAvatarSelectionRequest(string? AvatarId, bool UseRandomIfUnselected, string? ExpectedAccountEmail);
public sealed record MobileAvatarSelectionResponse(string Message, string AvatarId, string ProfileImageUrl);
