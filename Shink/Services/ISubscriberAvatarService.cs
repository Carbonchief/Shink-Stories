namespace Shink.Services;

public interface ISubscriberAvatarService
{
    Task<bool> UpdateAsync(string email, ProfileAvatar avatar, CancellationToken cancellationToken = default);
}
