using System.Text.Json;
using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Multiplexer;
using EntropyTunnel.Client.Pipeline;
using EntropyTunnel.Client.Services;
using EntropyTunnel.Client.Stages;

// Strip named flags first, then parse positional args: <port> <client-id>
int localPort = 0;
string? clientId = null;
string? tunnelPassword = null;

var positional = new List<string>();
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--password" && i + 1 < args.Length)
        tunnelPassword = args[++i];
    else
        positional.Add(args[i]);
}

if (positional.Count >= 2 && int.TryParse(positional[0], out int parsedPort))
{
    localPort = parsedPort;
    clientId = positional[1];
}
else if (positional.Count != 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Usage  : EntropyTunnel.Client [--password <pwd>] [<local-port> <client-id>]");
    Console.WriteLine("Example: dotnet run -- --password secret 5173 app1");
    Console.ResetColor();
    return;
}

var accountDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".entropytunnel");
var accountFile = Path.Combine(accountDir, "account.json");

string accountId;
if (File.Exists(accountFile))
{
    try
    {
        var cfg = JsonSerializer.Deserialize<AccountConfig>(File.ReadAllText(accountFile));
        accountId = cfg?.AccountId is { Length: > 0 } id ? id : Guid.NewGuid().ToString();
    }
    catch { accountId = Guid.NewGuid().ToString(); }
}
else
{
    accountId = Guid.NewGuid().ToString();
    Directory.CreateDirectory(accountDir);
    File.WriteAllText(accountFile, JsonSerializer.Serialize(new AccountConfig(accountId)));
}


var host = Host.CreateDefaultBuilder(args)
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
    })
    .ConfigureAppConfiguration((ctx, cfg) =>
    {
        var env = ctx.HostingEnvironment;

        cfg.SetBasePath(AppContext.BaseDirectory)
           .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
           .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
           .AddEnvironmentVariables();
    })
    .ConfigureServices((ctx, services) =>
    {
        var settings = ctx.Configuration
            .GetSection("TunnelSettings")
            .Get<TunnelSettings>() ?? new TunnelSettings();

        if (localPort > 0) settings.LocalPort = localPort;
        if (clientId is not null) settings.ClientId = clientId;
        if (tunnelPassword is not null) settings.TunnelPassword = tunnelPassword;
        settings.AccountId = accountId;

        services.AddSingleton(settings);
        services.AddSingleton<RuleStore>();
        services.AddSingleton<TunnelMultiplexer>();

        services.AddSingleton<AuthGateStage>();
        services.AddSingleton<MockEngine>();
        services.AddSingleton<ChaosEngine>();
        services.AddSingleton<RequestRouter>();
        services.AddSingleton<LocalForwarder>();
        services.AddSingleton<RequestPipeline>();

        services.AddHttpClient("tunnel", c => c.Timeout = TimeSpan.FromSeconds(30));

        services.AddHostedService<TunnelService>();
    })
    .Build();

await host.RunAsync();

record AccountConfig(string AccountId);
