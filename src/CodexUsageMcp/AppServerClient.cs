using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
namespace CodexUsageMcp;

// Each read gets a fresh process/handshake. Calls are serialized to bound resource usage.
public sealed class AppServerClient(AppServerOptions options) : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<UsageStatus> ReadAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
        var entered = false;
        try
        {
            await gate.WaitAsync(timeout.Token);
            entered = true;
            return await ReadCoreAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return UsageStatus.Failure("timeout", "Codex App Server did not respond within the configured timeout."); }
        catch (Win32Exception) { return UsageStatus.Failure("start_failed", "Cannot start Codex. Check the configured executable and local CLI installation."); }
        catch (JsonException) { return UsageStatus.Failure("invalid_response", "Codex returned an invalid or incompatible response. Check the CLI version."); }
        catch (IOException) { return UsageStatus.Failure("transport_closed", "Codex App Server closed its stream unexpectedly."); }
        catch (AppServerException ex) { return UsageStatus.Failure(ex.Code, ex.Message); }
        finally { if (entered) gate.Release(); }
    }
    private async Task<UsageStatus> ReadCoreAsync(CancellationToken ct)
    {
        var start = new ProcessStartInfo(options.Executable)
        {
            UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var arg in options.Arguments) start.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new AppServerException("start_failed", "Cannot start Codex App Server.");
        using var drainCancellation = new CancellationTokenSource();
        var stderr = DrainAsync(process.StandardError, drainCancellation.Token);
        try
        {
            await SendAsync(process, new { id = 1, method = "initialize", @params = new {
                clientInfo = new { name = "codex_usage_mcp", title = "Codex Usage MCP", version = "1.0.0" } } }, ct);
            _ = await ResponseAsync(process, 1, ct);
            await SendAsync(process, new { method = "initialized", @params = new { } }, ct);
            await SendAsync(process, new { id = 2, method = "account/rateLimits/read" }, ct);
            var result = await ResponseAsync(process, 2, ct);
            return UsageParser.Parse(result, DateTimeOffset.UtcNow);
        }
        finally
        {
            // Never leave an orphan process after a timeout, cancellation or malformed response.
            try { process.StandardInput.Close(); } catch (IOException) { }
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { } catch (Win32Exception) { }
            drainCancellation.Cancel();
            try { await stderr; } catch (OperationCanceledException) { } catch (IOException) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (TimeoutException) { }
        }
    }
    private static async Task SendAsync(Process process, object message, CancellationToken ct)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);
    }
    private async Task<JsonElement> ResponseAsync(Process process, int id, CancellationToken ct)
    {
        var reader = new BoundedLineReader(process.StandardOutput, options.MaxResponseChars);
        while (true)
        {
            var line = await reader.ReadAsync(ct) ?? throw new IOException("End of stream.");
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            // Do not execute server-initiated requests (including login/credential refresh requests).
            if (root.TryGetProperty("method", out _) && root.TryGetProperty("id", out var requestId))
            {
                await SendAsync(process, new { id = requestId.Clone(), error = new { code = -32601, message = "Read-only client: method unsupported." } }, ct);
                continue;
            }
            if (!root.TryGetProperty("id", out var responseId) || responseId.ValueKind != JsonValueKind.Number ||
                !responseId.TryGetInt32(out var received) || received != id) continue;
            if (root.TryGetProperty("error", out _))
                throw new AppServerException("upstream_error", "Codex could not read rate limits. Verify your existing ChatGPT CLI login and supported plan/version. Raw upstream details are intentionally withheld.");
            if (!root.TryGetProperty("result", out var result)) throw new JsonException();
            return result.Clone();
        }
    }
    private static async Task DrainAsync(StreamReader stderr, CancellationToken ct)
    {
        var buffer = new char[4096];
        while (await stderr.ReadAsync(buffer.AsMemory(), ct) != 0) { } // Discard: never log tokens or private diagnostics.
    }
    public void Dispose() => gate.Dispose();
    private sealed class AppServerException(string code, string message) : Exception(message)
    { public string Code { get; } = code; }
}

internal sealed class BoundedLineReader(StreamReader reader, int maximum)
{
    // StreamReader retains buffered bytes between instances; one-character reads enforce a hard per-line bound.
    public async Task<string?> ReadAsync(CancellationToken ct)
    {
        var text = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), ct) != 0)
        {
            if (character[0] == '\n') return text.ToString().TrimEnd('\r');
            if (text.Length >= maximum) throw new JsonException("Response exceeds configured size limit.");
            text.Append(character[0]);
        }
        return text.Length == 0 ? null : throw new JsonException("Truncated JSONL frame.");
    }
}
