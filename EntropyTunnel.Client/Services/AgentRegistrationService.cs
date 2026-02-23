using System.Net.Http.Json;
using System.Text.Json;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Models;

namespace EntropyTunnel.Client.Services;

/// <summary>
/// Runs only on secondary agents (IsPrimary == false).
/// Registers this agent with the primary dashboard every 30 s and de-registers on shutdown.
/// The primary becomes aware of this agent and exposes it in GET /api/agents so the React
/// dashboard can show an agent switcher.
/// </summary>
public sealed class AgentRegistrationService : BackgroundService
{
    private readonly DashboardInfo _dashInfo;
    private readonly TunnelSettings _settings;
    private readonly TunnelStatusService _status;
    private readonly HttpClient _http;
    private readonly ILogger<AgentRegistrationService> _logger;

    private static readonly JsonSerializerOptions _json =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AgentRegistrationService(
        DashboardInfo dashInfo,
        TunnelSettings settings,
        TunnelStatusService status,
        IHttpClientFactory factory,
        ILogger<AgentRegistrationService> logger)
    {
        _dashInfo = dashInfo;
        _settings = settings;
        _status = status;
        _http = factory.CreateClient("registration");
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Primary doesn't need to register with anyone
        if (_dashInfo.IsPrimary) return;

        // Give our own Kestrel time to start accepting connections
        await Task.Delay(2_500, stoppingToken);

        _logger.LogInformation(
            "Secondary agent — registering with primary dashboard at {PrimaryUrl}",
            _dashInfo.PrimaryApiUrl);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n📊  Primary dashboard: {_dashInfo.PrimaryApiUrl}/");
        Console.ResetColor();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RegisterAsync(stoppingToken);
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning("Could not reach primary dashboard: {Msg}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_dashInfo.IsPrimary)
        {
            try
            {
                await _http.DeleteAsync(
                    $"{_dashInfo.PrimaryApiUrl}/api/agents/{Uri.EscapeDataString(_settings.ClientId)}",
                    CancellationToken.None);
            }
            catch { /* best-effort unregister */ }
        }

        await base.StopAsync(cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task RegisterAsync(CancellationToken ct)
    {
        var payload = new AgentInfo
        {
            ClientId = _settings.ClientId,
            LocalPort = _settings.LocalPort,
            ApiUrl = _dashInfo.MyApiUrl,
            IsPrimary = false,
            IsConnected = _status.IsConnected,
            PublicUrl = _status.PublicUrl,
        };

        var content = JsonContent.Create(payload, options: _json);
        return _http.PostAsync($"{_dashInfo.PrimaryApiUrl}/api/agents/register", content, ct);
    }
}
