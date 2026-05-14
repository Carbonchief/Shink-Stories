namespace Shink.Services;

public interface ISchoolManagementService
{
    Task<bool> HasSchoolAdminAccessAsync(string? adminEmail, CancellationToken cancellationToken = default);
    Task<SchoolDashboardSnapshot> GetDashboardAsync(string? adminEmail, CancellationToken cancellationToken = default);
    Task<SchoolOperationResult> UpdateSchoolNameAsync(string? adminEmail, string? schoolName, CancellationToken cancellationToken = default);
    Task<SchoolOperationResult> UpdateAdminSeatUsageAsync(string? adminEmail, bool adminUsesSlot, CancellationToken cancellationToken = default);
    Task<SchoolOperationResult> InviteTeacherAsync(string? adminEmail, SchoolInviteTeacherRequest request, CancellationToken cancellationToken = default);
    Task<SchoolOperationResult> RemoveSeatAsync(string? adminEmail, Guid seatId, CancellationToken cancellationToken = default);
    Task<SchoolSeatStatsSnapshot?> GetSeatStatsAsync(string? adminEmail, Guid seatId, CancellationToken cancellationToken = default);
}

public sealed record SchoolOperationResult(bool IsSuccess, string? ErrorMessage = null, Guid? EntityId = null);

public sealed record SchoolInviteTeacherRequest(string? Email, string? DisplayName);

public sealed record SchoolDashboardSnapshot(
    bool HasSchoolAccess,
    SchoolAccountRecord? Account,
    IReadOnlyList<SchoolSeatRecord> Seats,
    IReadOnlyList<SchoolPlanRecord> AvailablePlans)
{
    public int SlotLimit => Account?.SlotLimit ?? 0;
    public int UsedSlots => Seats.Count(seat => seat.CountsTowardSlot);
    public int AvailableSlots => Math.Max(0, SlotLimit - UsedSlots);
    public int InvitedSeats => Seats.Count(seat => string.Equals(seat.Status, "invited", StringComparison.OrdinalIgnoreCase));
    public int AcceptedSeats => Seats.Count(seat => string.Equals(seat.Status, "accepted", StringComparison.OrdinalIgnoreCase));
}

public sealed record SchoolAccountRecord(
    Guid SchoolAccountId,
    string SchoolName,
    string AdminEmail,
    string PlanTierCode,
    string PlanName,
    int SlotLimit,
    bool AdminUsesSlot,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record SchoolSeatRecord(
    Guid SchoolSeatId,
    Guid SchoolAccountId,
    string Email,
    string? DisplayName,
    string Role,
    string Status,
    bool CountsTowardSlot,
    DateTimeOffset InvitedAt,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RemovedAt);

public sealed record SchoolSeatStatsSnapshot(
    SchoolSeatRecord Seat,
    bool HasSubscriberAccount,
    DateTimeOffset? AccessExpiresAt,
    int TotalViews,
    int UniqueViewedStories,
    int TotalListenSessions,
    decimal TotalListenedSeconds,
    int UniqueListenedStories,
    DateTimeOffset? LastActivityAt,
    IReadOnlyList<SchoolSeatStoryActivityRecord> RecentStories);

public sealed record SchoolSeatStoryActivityRecord(
    string StorySlug,
    string StoryTitle,
    int Views,
    decimal ListenedSeconds,
    DateTimeOffset LastActivityAt);

public sealed record SchoolPlanRecord(
    string Slug,
    string Name,
    string TierCode,
    decimal Amount,
    int SlotLimit);
