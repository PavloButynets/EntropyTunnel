using EntropyTunnel.Client.Configuration;
using EntropyTunnel.Client.Dashboard;
using EntropyTunnel.Client.Multiplexer;
using EntropyTunnel.Client.Pipeline;
using EntropyTunnel.Client.Services;
using EntropyTunnel.Client.Stages;

// Parse positional CLI args: <port> <client-id>
int localPort = 0;
string? clientId = null;

if (args.Length >= 2 && int.TryParse(args[0], out int parsedPort))
{
    localPort = parsedPort;
    clientId = args[1];
}
else if (args.Length != 0)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Usage  : EntropyTunnel.Client <local-port> <client-id>");
    Console.WriteLine("Example: dotnet run -- 5173 app1");
    Console.ResetColor();
    return;
}

var host = Host.CreateDefaultBuilder(args)
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

        services.AddSingleton(settings);
        services.AddSingleton<RuleStore>();
        services.AddSingleton<TunnelMultiplexer>();

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
