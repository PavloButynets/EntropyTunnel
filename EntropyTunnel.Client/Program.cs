using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Models;
using EntropyTunnel.Client.Multiplexer;
using EntropyTunnel.Client.Pipeline;
using EntropyTunnel.Client.Services;
using EntropyTunnel.Client.Stages;

// Parse positional CLI args: <port> <client-id>
int localPort = 0;
string? clientId = null;
string[] aspNetArgs = args;

if (args.Length >= 2 && int.TryParse(args[0], out int parsedPort))
{
    localPort = parsedPort;
    clientId = args[1];
    aspNetArgs = args[2..];
}
else if (args.Length != 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Usage  : EntropyTunnel.Client <local-port> <client-id>");
    Console.WriteLine("Example: dotnet run -- 5173 app1");
    Console.ResetColor();
    return;
}

var preConfig = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .Build();


int startPort = preConfig.GetValue("TunnelSettings:DashboardPort", 4040);
int dashboardPort = FindAvailablePort(startPort);

// Determine role: first process to claim startPort is primary.
bool isPrimary = dashboardPort == startPort;
string myApiUrl = $"http://localhost:{dashboardPort}";
string primaryApiUrl = $"http://localhost:{startPort}";

var builder = WebApplication.CreateBuilder(aspNetArgs);
builder.WebHost.UseUrls(myApiUrl);

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.StaticFiles", LogLevel.Warning);

var settings = builder.Configuration
    .GetSection("TunnelSettings")
    .Get<TunnelSettings>() ?? new TunnelSettings();

if (localPort > 0) settings.LocalPort = localPort;
if (clientId is not null) settings.ClientId = clientId;
settings.DashboardPort = dashboardPort;

builder.Services.AddSingleton(settings);

var dashInfo = new DashboardInfo(dashboardPort, startPort, isPrimary, myApiUrl, primaryApiUrl);
builder.Services.AddSingleton(dashInfo);

builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(p => p
        .SetIsOriginAllowed(o =>
        {
            if (!Uri.TryCreate(o, UriKind.Absolute, out var uri)) return false;
            return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);
        })
        .AllowAnyMethod()
        .AllowAnyHeader()));

builder.Services.AddSingleton<RuleStore>();
builder.Services.AddSingleton<AgentRegistry>();
builder.Services.AddSingleton<TunnelStatusService>();
builder.Services.AddSingleton<TunnelMultiplexer>();

builder.Services.AddSingleton<MockEngine>();
builder.Services.AddSingleton<ChaosEngine>();
builder.Services.AddSingleton<RequestRouter>();
builder.Services.AddSingleton<LocalForwarder>();
builder.Services.AddSingleton<RequestPipeline>();

builder.Services.AddHttpClient("tunnel", c => c.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient("registration", c => c.Timeout = TimeSpan.FromSeconds(10));

builder.Services.AddHostedService<TunnelService>();
builder.Services.AddHostedService<AgentRegistrationService>();

builder.Services.ConfigureHttpJsonOptions(opts =>
    opts.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

var app = builder.Build();

// Primary hosts the React SPA; secondary skips static files (just an API server)
if (isPrimary)
{
    app.UseDefaultFiles();
    app.UseStaticFiles();
}

app.UseCors();

var api = app.MapGroup("/api");

api.MapGet("/ping", () => new { app = "EntropyTunnel.Dashboard", version = "1.0" });

api.MapGet("/status", (TunnelStatusService svc) => svc.GetStatus());

api.MapGet("/agents", (
    AgentRegistry registry,
    TunnelStatusService status,
    TunnelSettings cfg,
    DashboardInfo di) =>
{
    var self = new AgentInfo
    {
        ClientId = cfg.ClientId,
        LocalPort = cfg.LocalPort,
        ApiUrl = di.MyApiUrl,
        IsPrimary = di.IsPrimary,
        IsConnected = status.IsConnected,
        PublicUrl = status.PublicUrl,
        LastSeen = DateTimeOffset.UtcNow,
    };

    // Prune agents that haven't re-registered in 90 s
    registry.PruneStale(TimeSpan.FromSeconds(90));

    var all = new[] { self }.Concat(registry.GetAll()).ToList();
    return Results.Ok(all);
});

api.MapPost("/agents/register", (AgentInfo agent, AgentRegistry registry) =>
{
    registry.Register(agent);
    return Results.Ok();
});

api.MapDelete("/agents/{clientId}", (string clientId, AgentRegistry registry) =>
    registry.Unregister(clientId) ? Results.NoContent() : Results.NotFound());

api.MapGet("/rules/chaos", (RuleStore store) =>
    Results.Ok(store.GetChaosRules()));

api.MapPost("/rules/chaos", (ChaosRule rule, RuleStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.AddChaosRule(rule);
    return Results.Created($"/api/rules/chaos/{rule.Id}", rule);
});

api.MapPut("/rules/chaos/{id:guid}", (Guid id, ChaosRule rule, RuleStore store) =>
{
    rule = rule with { Id = id };
    return store.UpdateChaosRule(rule) ? Results.Ok(rule) : Results.NotFound();
});

api.MapDelete("/rules/chaos/{id:guid}", (Guid id, RuleStore store) =>
    store.RemoveChaosRule(id) ? Results.NoContent() : Results.NotFound());

api.MapPatch("/rules/chaos/{id:guid}/toggle", (Guid id, RuleStore store) =>
{
    var updated = store.ToggleChaosRule(id);
    return updated is not null ? Results.Ok(updated) : Results.NotFound();
});

api.MapGet("/rules/mocks", (RuleStore store) =>
    Results.Ok(store.GetMockRules()));

api.MapPost("/rules/mocks", (MockRule rule, RuleStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.AddMockRule(rule);
    return Results.Created($"/api/rules/mocks/{rule.Id}", rule);
});

api.MapPut("/rules/mocks/{id:guid}", (Guid id, MockRule rule, RuleStore store) =>
{
    rule = rule with { Id = id };
    return store.UpdateMockRule(rule) ? Results.Ok(rule) : Results.NotFound();
});

api.MapDelete("/rules/mocks/{id:guid}", (Guid id, RuleStore store) =>
    store.RemoveMockRule(id) ? Results.NoContent() : Results.NotFound());

api.MapGet("/rules/routing", (RuleStore store) =>
    Results.Ok(store.GetRoutingRules()));

api.MapPost("/rules/routing", (RoutingRule rule, RuleStore store) =>
{
    rule = rule with { Id = Guid.NewGuid() };
    store.AddRoutingRule(rule);
    return Results.Created($"/api/rules/routing/{rule.Id}", rule);
});

api.MapPut("/rules/routing/{id:guid}", (Guid id, RoutingRule rule, RuleStore store) =>
{
    rule = rule with { Id = id };
    return store.UpdateRoutingRule(rule) ? Results.Ok(rule) : Results.NotFound();
});

api.MapDelete("/rules/routing/{id:guid}", (Guid id, RuleStore store) =>
    store.RemoveRoutingRule(id) ? Results.NoContent() : Results.NotFound());

api.MapGet("/log", (RuleStore store) =>
    Results.Ok(store.GetRequestLog()));

api.MapDelete("/log", (RuleStore store) =>
{
    store.ClearRequestLog();
    return Results.NoContent();
});

api.MapPost("/replay", async (
    ReplayRequestDto req,
    TunnelSettings cfg,
    IHttpClientFactory factory,
    CancellationToken ct) =>
{
    try
    {
        var client = factory.CreateClient("tunnel");
        var targetUrl = $"http://localhost:{cfg.LocalPort}{req.Path}";
        var message = new HttpRequestMessage(new HttpMethod(req.Method), targetUrl);

        var contentHeaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Content-Type", "Content-Length", "Content-Encoding",
            "Content-Language", "Content-Location", "Content-MD5",
            "Content-Range", "Content-Disposition"
        };

        if (req.Headers is not null)
            foreach (var (key, value) in req.Headers)
                if (!contentHeaderNames.Contains(key))
                    message.Headers.TryAddWithoutValidation(key, value);

        bool hasBody = !string.IsNullOrEmpty(req.Body)
            && req.Method.ToUpperInvariant() is not ("GET" or "DELETE" or "HEAD" or "OPTIONS");

        if (hasBody)
        {
            var mediaType = (req.Headers?.GetValueOrDefault("Content-Type") ?? "application/json")
                            .Split(';')[0].Trim();
            message.Content = new StringContent(req.Body!, Encoding.UTF8, mediaType);
        }

        var sw = Stopwatch.StartNew();
        var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct);
        sw.Stop();

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        var allHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers) allHeaders[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers) allHeaders[h.Key] = string.Join(", ", h.Value);

        return Results.Ok(new
        {
            statusCode = (int)response.StatusCode,
            durationMs = sw.ElapsedMilliseconds,
            headers = allHeaders,
            body = responseBody
        });
    }
    catch (HttpRequestException ex)
    {
        return Results.Ok(new
        {
            statusCode = 502,
            durationMs = 0L,
            headers = new Dictionary<string, string>(),
            body = $"Connection failed: {ex.Message}"
        });
    }
});

if (isPrimary)
    app.MapFallbackToFile("index.html");

await app.RunAsync();


/// <summary>
/// Tries ports [start, start+49] and returns the first one that is not bound.
/// Uses TcpListener.Start/Stop as a reliable cross-platform availability probe.
/// </summary>
static int FindAvailablePort(int start, int range = 50)
{
    for (int port = start; port < start + range; port++)
    {
        try
        {
            using var l = new TcpListener(System.Net.IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return port;
        }
        catch (SocketException) { /* port in use — try next */ }
    }

    throw new InvalidOperationException(
        $"No available port found in range {start}–{start + range - 1}.");
}


record ReplayRequestDto(
    string Method,
    string Path,
    Dictionary<string, string>? Headers,
    string? Body
);

/// <summary>
/// Topology facts about the current process, computed at startup.
/// Injected as a singleton so services can query their role.
/// </summary>
public record DashboardInfo(
    int DashboardPort,
    int StartPort,
    bool IsPrimary,
    string MyApiUrl,
    string PrimaryApiUrl
);
