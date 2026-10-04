namespace CodexUsageMcp;

public sealed record UsageStatus(string Status, DateTimeOffset FetchedAtUtc,
    IReadOnlyList<LimitBucket> Buckets, ResetCredits? FreeResetCredits, string? ErrorCode = null,
    string? ErrorMessage = null, bool? OrdinaryUsageAllowed = null)
{
    public string Measurement => "quota-window percentage, not tokens or monetary balance";
    public static UsageStatus Failure(string code, string message) =>
        new("error", DateTimeOffset.UtcNow, [], null, code, message);
}
public sealed record LimitBucket(string LimitId, string? LimitName, string? PlanType,
    string? RateLimitReachedType, IReadOnlyList<QuotaWindow> Windows);
public sealed record QuotaWindow(string SourceSlot, string WindowKind, double? UsedPercent,
    double? RemainingPercent, int? WindowDurationMins, DateTimeOffset? ResetsAtUtc);
public sealed record ResetCredits(long? AvailableCount, bool? HasAvailableReset,
    IReadOnlyList<ResetCredit>? Credits)
{
    public string CountSemantics => "AvailableCount is authoritative; detail rows may be capped.";
}
public sealed record ResetCredit(string? Id, string? ResetType, string? Status,
    DateTimeOffset? GrantedAtUtc, DateTimeOffset? ExpiresAtUtc, string ExpiryState, string? Title, string? Description);
