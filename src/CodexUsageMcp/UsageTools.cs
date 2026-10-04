using System.ComponentModel;
using ModelContextProtocol.Server;
namespace CodexUsageMcp;
[McpServerToolType]
public sealed class UsageTools(AppServerClient client)
{
    [McpServerTool(Name = "get_codex_usage_status", ReadOnly = true, Destructive = false, Idempotent = true, UseStructuredContent = true)]
    [Description("Read current locally authenticated Codex account quota windows, weekly remaining percentage, and service-provided free/earned reset count and expiry. No resets or purchases. Null means unavailable, never zero. This is not a token or monetary balance.")]
    public Task<UsageStatus> GetCodexUsageStatus(CancellationToken cancellationToken) => client.ReadAsync(cancellationToken);
}
