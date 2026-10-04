using CodexUsageMcp;
using ModelContextProtocol.AspNetCore;
using System.Net;
using System.Text.Json;

if (args.Contains("--once", StringComparer.Ordinal))
{
    var config = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true).AddEnvironmentVariables()
        .AddCommandLine(args.Where(x => x != "--once").ToArray()).Build();
    var options = config.GetSection("Codex").Get<AppServerOptions>() ?? new();
    options.Validate();
    using var client = new AppServerClient(options);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
    var result = await client.ReadAsync(cancellation.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    Environment.ExitCode = result.Status == "error" ? 1 : 0;
    return;
}
var stdio = args.Contains("--stdio", StringComparer.Ordinal);
var hostArgs = args.Where(x => x != "--stdio").ToArray();
if (stdio)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = hostArgs, ContentRootPath = AppContext.BaseDirectory });
    Configure(builder.Services, builder.Configuration);
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<UsageTools>();
    await builder.Build().RunAsync();
}
else
{
    var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = hostArgs, ContentRootPath = AppContext.BaseDirectory });
    Configure(builder.Services, builder.Configuration);
    var port = builder.Configuration.GetValue("Http:Port", 5078);
    if (port is < 1024 or > 65535) throw new InvalidOperationException("Http:Port must be 1024..65535.");
    // Reject extra endpoints before configuring the only supported loopback listener.
    if (builder.Configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        throw new InvalidOperationException("Kestrel:Endpoints overrides are not supported; use Http:Port.");
    builder.WebHost.ConfigureKestrel(o => { o.Configure(new ConfigurationBuilder().Build(), reloadOnChange: false); o.Listen(IPAddress.Loopback, port); });
    builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<UsageTools>();
    var app = builder.Build();
    app.Use(async (context, next) =>
    {
        // Refuse browser-origin requests and DNS-rebinding hosts. No CORS is enabled.
        var host = context.Request.Host.Host;
        if (context.Request.Headers.ContainsKey("Origin") ||
            (host != "127.0.0.1" && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)))
        { context.Response.StatusCode = 403; return; }
        await next(context);
    });
    app.MapGet("/healthz", () => Results.Ok(new { status = "ok", scope = "process-only", version = "1.0.0" }));
    app.MapMcp("/mcp");
    await app.RunAsync();
}
static void Configure(IServiceCollection services, IConfiguration config)
{
    var options = config.GetSection("Codex").Get<AppServerOptions>() ?? new();
    options.Validate();
    services.AddSingleton(options);
    services.AddSingleton<AppServerClient>();
}
