using System.Text.Json;
namespace CodexUsageMcp;

public static class UsageParser
{
    public static UsageStatus Parse(JsonElement result, DateTimeOffset fetchedAt)
    {
        if (result.ValueKind != JsonValueKind.Object) throw new JsonException("Expected result object.");
        var buckets = new List<LimitBucket>();
        if (Get(result, "rateLimitsByLimitId") is { ValueKind: JsonValueKind.Object } many)
            foreach (var bucket in many.EnumerateObject())
                if (bucket.Value.ValueKind == JsonValueKind.Object) buckets.Add(ParseBucket(bucket.Value, bucket.Name));
        if (buckets.Count == 0 && Get(result, "rateLimits") is { ValueKind: JsonValueKind.Object } single)
            buckets.Add(ParseBucket(single, Text(single, "limitId") ?? "unknown"));
        ResetCredits? resets = null;
        if (Get(result, "rateLimitResetCredits") is { ValueKind: JsonValueKind.Object } reset)
        {
            var count = LongInteger(reset, "availableCount");
            if (count < 0) count = null;
            List<ResetCredit>? credits = null;
            if (Get(reset, "credits") is { ValueKind: JsonValueKind.Array } rows)
                credits = rows.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
                    .Select(x => new ResetCredit(Text(x,"id"), Text(x,"resetType"), Text(x,"status"),
                        Timestamp(x,"grantedAt"), Timestamp(x,"expiresAt"), ExpiryState(x), Text(x,"title"), Text(x,"description"))).ToList();
            resets = new(count, count.HasValue ? count > 0 : null, credits);
        }
        return new(buckets.Count == 0 && resets is null ? "unavailable" : "ok", fetchedAt, buckets, resets, OrdinaryUsageAllowed: Get(result,"ordinaryUsageAllowed") is { ValueKind: JsonValueKind.True } ? true : Get(result,"ordinaryUsageAllowed") is { ValueKind: JsonValueKind.False } ? false : null);
    }
    private static LimitBucket ParseBucket(JsonElement bucket, string key)
    {
        var windows = new List<QuotaWindow>();
        foreach (var slot in new[] { "primary", "secondary" })
        {
            if (Get(bucket, slot) is not { ValueKind: JsonValueKind.Object } window) continue;
            var duration = Integer(window, "windowDurationMins");
            if (duration <= 0) duration = null;
            double? used = Get(window,"usedPercent") is { ValueKind: JsonValueKind.Number } n &&
                n.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 ? value : null;
            // Over-limit values retain the reported usage but remaining quota cannot be negative.
            double? remaining = used.HasValue ? Math.Clamp(100 - used.Value, 0, 100) : null;
            windows.Add(new(slot, duration switch { 10080 => "weekly", 300 => "five_hour", _ => "other_or_unknown" },
                used, remaining, duration, Timestamp(window,"resetsAt")));
        }
        return new(Text(bucket,"limitId") ?? key, Text(bucket,"limitName"), Text(bucket,"planType"),
            Text(bucket,"rateLimitReachedType"), windows);
    }
    private static JsonElement? Get(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out var v) ? v : null;
    private static string? Text(JsonElement e, string key) => Get(e,key) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;
    private static string ExpiryState(JsonElement e) => Get(e,"expiresAt") is { ValueKind: JsonValueKind.Null } ? "no_expiry" : Timestamp(e,"expiresAt") is not null ? "expires_at" : "unknown";
    private static long? LongInteger(JsonElement e, string key) => Get(e,key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt64(out var n) ? n : null;
    private static int? Integer(JsonElement e, string key) => Get(e,key) is { ValueKind: JsonValueKind.Number } v && v.TryGetInt32(out var n) ? n : null;
    private static DateTimeOffset? Timestamp(JsonElement e, string key)
    {
        if (Get(e,key) is not { ValueKind: JsonValueKind.Number } v || !v.TryGetInt64(out var seconds)) return null;
        try { return DateTimeOffset.FromUnixTimeSeconds(seconds); } catch (ArgumentOutOfRangeException) { return null; }
    }
}
