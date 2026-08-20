namespace Shink.Services;

public interface IReferralService
{
    Task<AdminReferralSnapshot> GetAdminReferralsAsync(
        string? adminEmail,
        CancellationToken cancellationToken = default);

    Task<ReferralOperationResult> CreateReferralAsync(
        string? adminEmail,
        ReferralCreateRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ReferralCreateRequest(string? ReferrerName, string? ReferrerEmail);

public sealed record AdminReferralSnapshot(
    int TotalReferrals,
    int TotalSignups,
    IReadOnlyList<AdminReferralCodeRecord> Referrals)
{
    public static AdminReferralSnapshot Empty { get; } = new(0, 0, Array.Empty<AdminReferralCodeRecord>());
}

public sealed record AdminReferralCodeRecord(
    string Code,
    string ReferrerName,
    string? ReferrerEmail,
    DateTimeOffset CreatedAt,
    int SignupCount,
    DateTimeOffset? LastSignupAt);

public sealed record ReferralOperationResult(bool IsSuccess, string? ReferralCode = null, string? ErrorMessage = null)
{
    public static ReferralOperationResult Success(string referralCode) => new(true, referralCode);

    public static ReferralOperationResult Failure(string errorMessage) => new(false, null, errorMessage);
}
