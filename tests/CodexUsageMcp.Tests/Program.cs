using System.Text.Json;
using CodexUsageMcp;

if (args.FirstOrDefault() == "--mock")
{
    await MockAsync(args[1]);
    return;
}
var passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name); passed++;
}
UsageStatus Parse(string json) { using var doc = JsonDocument.Parse(json); return UsageParser.Parse(doc.RootElement, DateTimeOffset.UnixEpoch); }
var status = Parse("""{"rateLimits":{"primary":{"usedPercent":12,"windowDurationMins":10080,"resetsAt":1730947200},"secondary":{"usedPercent":50,"windowDurationMins":300}},"ordinaryUsageAllowed":false,"rateLimitResetCredits":{"availableCount":2,"credits":null}}""");
Check(status.Buckets[0].Windows[0].WindowKind == "weekly" && status.Buckets[0].Windows[0].RemainingPercent == 88, "weekly primary classified by duration");
Check(status.Buckets[0].Windows[1].WindowKind == "five_hour", "secondary not assumed weekly");
Check(status.OrdinaryUsageAllowed == false, "usage permission preserved independently");
Check(status.FreeResetCredits is { AvailableCount: 2, HasAvailableReset: true, Credits: null }, "count only preserves null details");
Check(status.Buckets[0].Windows[0].ResetsAtUtc == DateTimeOffset.FromUnixTimeSeconds(1730947200), "timestamp uses Unix seconds");
Check(Parse("{}").FreeResetCredits is null, "missing resets not zero");
Check(Parse("""{"rateLimitResetCredits":null}""").Status == "unavailable", "unavailable service data explicit");
var multi = Parse("""{"rateLimits":{"limitId":"legacy"},"rateLimitsByLimitId":{"codex":{"secondary":{"usedPercent":101,"windowDurationMins":10080}},"other":{"primary":{"usedPercent":15,"windowDurationMins":60}}},"rateLimitResetCredits":{"availableCount":3000000000,"credits":[]}}""");
Check(multi.Buckets.Count == 2 && multi.Buckets[0].LimitId == "codex", "multiple buckets take precedence over legacy");
Check(multi.Buckets[0].Windows[0].RemainingPercent == 0, "over-limit remaining is zero");
Check(multi.FreeResetCredits is { AvailableCount: 3000000000, Credits.Count: 0 }, "authoritative 64-bit count independent of rows");
var expiry = Parse("""{"rateLimitResetCredits":{"availableCount":2,"credits":[{"expiresAt":null},{},{"expiresAt":1784246400}]}}""").FreeResetCredits!.Credits!;
Check(expiry[0].ExpiryState == "no_expiry" && expiry[1].ExpiryState == "unknown" && expiry[2].ExpiryState == "expires_at", "explicit no expiry distinguished from unknown");
var malformed = Parse("""{"rateLimits":{"primary":{"usedPercent":-1,"windowDurationMins":null,"resetsAt":9223372036854775807}}}""").Buckets[0].Windows[0];
Check(malformed.RemainingPercent is null && malformed.ResetsAtUtc is null && malformed.WindowKind == "other_or_unknown", "invalid values never invent remaining or timestamps");

AppServerClient Client(string scenario, int seconds = 5, int maximum = 1048576) => new(new AppServerOptions
{
    Executable = Environment.ProcessPath!,
    Arguments = [System.Reflection.Assembly.GetExecutingAssembly().Location, "--mock", scenario],
    TimeoutSeconds = seconds, MaxResponseChars = maximum
});
// Running with dotnet <dll> gives Environment.ProcessPath == dotnet (see README).
using (var client = Client("ok"))
{
    var response = await client.ReadAsync(default);
    Check(response.Status == "ok" && response.Buckets[0].Windows[0].RemainingPercent == 75, "real subprocess handshake, notifications, request rejection, stderr drain and result");
}
foreach (var (scenario, code) in new[] { ("error", "upstream_error"), ("invalid", "invalid_response"), ("eof", "transport_closed"), ("oversized", "invalid_response"), ("truncated", "invalid_response") })
{
    using var client = Client(scenario, maximum: 1024);
    var response = await client.ReadAsync(default);
    Check(response.ErrorCode == code, "subprocess " + scenario);
    Check(!(response.ErrorMessage ?? "").Contains("SECRET"), "no raw upstream disclosure " + scenario);
}
using (var client = Client("delay", seconds: 1))
    Check((await client.ReadAsync(default)).ErrorCode == "timeout", "timeout kills stalled child");
using (var client = Client("delay"))
using (var cancel = new CancellationTokenSource(100))
{
    var canceled = false;
    try { await client.ReadAsync(cancel.Token); } catch (OperationCanceledException) { canceled = true; }
    Check(canceled, "caller cancellation propagated");
}
using (var client = new AppServerClient(new AppServerOptions { Executable = "/nonexistent/codex-usage-test" }))
    Check((await client.ReadAsync(default)).ErrorCode == "start_failed", "missing executable clean error");
Console.WriteLine($"All {passed} checks passed; no actual Codex account accessed.");

static async Task MockAsync(string scenario)
{
    static async Task<JsonElement> Read() => JsonDocument.Parse(await Console.In.ReadLineAsync() ?? throw new Exception("Missing request")).RootElement.Clone();
    static void Require(bool condition) { if (!condition) throw new Exception("Mock protocol assertion failed."); }
    var init = await Read();
    Require(init.GetProperty("method").GetString() == "initialize" && !init.TryGetProperty("jsonrpc", out _));
    Require(init.GetProperty("params").GetProperty("clientInfo").GetProperty("name").GetString() == "codex_usage_mcp");
    Console.WriteLine("{\"id\":1,\"result\":{}}");
    var initialized = await Read();
    Require(initialized.GetProperty("method").GetString() == "initialized" && !initialized.TryGetProperty("id", out _));
    var read = await Read();
    Require(read.GetProperty("method").GetString() == "account/rateLimits/read");
    if (scenario == "delay") { await Task.Delay(30000); return; }
    if (scenario == "eof") return;
    if (scenario == "error") { Console.WriteLine("{\"id\":2,\"error\":{\"code\":-1,\"message\":\"SECRET\"}}"); return; }
    if (scenario == "invalid") { Console.WriteLine("invalid SECRET"); return; }
    if (scenario == "oversized") { Console.WriteLine(new string('a', 2048)); return; }
    if (scenario == "truncated") { Console.Write("{\"id\":2"); return; }
    Console.Error.Write(new string('s', 100000));
    Console.WriteLine("{\"method\":\"account/updated\",\"params\":{}}");
    Console.WriteLine("{\"method\":\"account/chatgptAuthTokens/refresh\",\"id\":\"server-1\",\"params\":{}}");
    var rejection = await Read();
    Require(rejection.GetProperty("id").GetString() == "server-1" && rejection.GetProperty("error").GetProperty("code").GetInt32() == -32601);
    Console.WriteLine("{\"id\":2,\"result\":{\"rateLimits\":{\"secondary\":{\"usedPercent\":25,\"windowDurationMins\":10080}}}}");
}
