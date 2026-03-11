using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Shink.Services;

public sealed class ContactFormProtectionService(IMemoryCache cache) : IContactFormProtectionService
{
    private static readonly TimeSpan RateWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan MinimumInterval = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan DuplicateWindow = TimeSpan.FromMinutes(30);
    private const int MaxSubmissionsPerWindow = 5;
    private readonly IMemoryCache _cache = cache;

    public bool TryValidateSubmission(string clientId, string email, string subject, string message, out string errorMessage)
    {
        var normalizedClientId = string.IsNullOrWhiteSpace(clientId)
            ? "unknown-client"
            : clientId.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        var stateKey = $"contact:state:{normalizedClientId}";
        var state = _cache.GetOrCreate(stateKey, entry =>
        {
            entry.SlidingExpiration = TimeSpan.FromHours(2);
            return new ContactSubmissionState();
        })!;

        lock (state)
        {
            state.Attempts.RemoveAll(timestamp => now - timestamp > RateWindow);

            if (state.LastAttemptUtc.HasValue && now - state.LastAttemptUtc.Value < MinimumInterval)
            {
                errorMessage = "Wag asseblief 'n paar sekondes voordat jy weer stuur.";
                return false;
            }

            if (state.Attempts.Count >= MaxSubmissionsPerWindow)
            {
                errorMessage = "Jy het te veel boodskappe in 'n kort tyd gestuur. Probeer asseblief later weer.";
                return false;
            }
        }

        var contentHash = ComputeHash($"{email}|{subject}|{message}".Trim().ToLowerInvariant());
        var duplicateKey = $"contact:dup:{normalizedClientId}:{contentHash}";
        if (_cache.TryGetValue(duplicateKey, out _))
        {
            errorMessage = "Hierdie boodskap is reeds ontvang. Probeer asseblief later weer.";
            return false;
        }

        lock (state)
        {
            state.Attempts.Add(now);
            state.LastAttemptUtc = now;
        }

        _cache.Set(duplicateKey, true, DuplicateWindow);
        errorMessage = string.Empty;
        return true;
    }

    private static string ComputeHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private sealed class ContactSubmissionState
    {
        public List<DateTimeOffset> Attempts { get; } = [];
        public DateTimeOffset? LastAttemptUtc { get; set; }
    }
}
