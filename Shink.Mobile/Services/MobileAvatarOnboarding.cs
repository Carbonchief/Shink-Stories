using Shink.Mobile.Models;

namespace Shink.Mobile.Services;

public static class MobileAvatarOnboarding
{
    public static MobileSession Merge(MobileSession current, MobileSession incoming, bool completed = false)
    {
        var sameAccount = current.IsSignedIn &&
            (string.IsNullOrWhiteSpace(current.Email) || string.Equals(current.Email, incoming.Email, StringComparison.OrdinalIgnoreCase));
        return incoming with
        {
            RequiresAvatarSelection = incoming.IsSignedIn && incoming.SupportsProfileAvatars && !completed &&
                (incoming.RequiresAvatarSelection || sameAccount && current.RequiresAvatarSelection)
        };
    }

    public static bool IsAvatarRedirect(string? path) =>
        path?.Split('?', '#')[0] == "/profielprentjie";
}
