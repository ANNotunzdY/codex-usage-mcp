namespace CodexUsageMcp;
public sealed class AppServerOptions
{
    public string Executable { get; set; } = "codex";
    public string[] Arguments { get; set; } = [];
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxResponseChars { get; set; } = 1_048_576;
    public void Validate()
    {
        if (Arguments is { Length: 0 }) Arguments = ["app-server", "--listen", "stdio://"];
        if (string.IsNullOrWhiteSpace(Executable) || Arguments is null || Arguments.Any(x => x is null) ||
            TimeoutSeconds is < 1 or > 120 || MaxResponseChars is < 1024 or > 8_388_608)
            throw new InvalidOperationException("Invalid Codex configuration.");
    }
}
