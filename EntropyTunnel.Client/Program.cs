using EntropyTunnel.Core;
using System.Net.WebSockets;
using System.Text;
using Microsoft.Extensions.Configuration;

var builder = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

IConfiguration config = builder.Build();

string serverBaseUrl = config["TunnelSettings:ServerUrl"] ?? "ws://localhost:8080/tunnel";
string publicDomainBase = config["TunnelSettings:PublicDomain"] ?? "localhost:8080";

var chaosConfig = new ChaosConfig();
config.GetSection("ChaosSettings").Bind(chaosConfig);

if (args.Length < 2 || !int.TryParse(args[0], out int localPort))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️  Usage: EntropyTunnel.Client <port> <client-id>");
    Console.WriteLine("   Example: dotnet run -- 5173 app1");
    Console.ResetColor();
    return;
}

string clientId = args[1];

Console.WriteLine($"--- TUNNEL AGENT v0.8 MULTIPLEXED (Port: {localPort}, ID: {clientId}) ---");
Console.WriteLine($"--- Config loaded: Server -> {serverBaseUrl} ---");

string serverUrl = $"{serverBaseUrl}?clientId={clientId}";
string localBaseUrl = $"http://localhost:{localPort}";

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);

try
{
    await httpClient.GetAsync(localBaseUrl);
}
catch
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine($"[Warning] Local service at {localBaseUrl} seems down. Is it running?");
    Console.ResetColor();
}

while (true)
{
    try { await RunAgent(); }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Wait... {ex.Message}");
        Console.ResetColor();
        await Task.Delay(3000);
    }
}

async Task RunAgent()
{
    using var ws = new ClientWebSocket();
    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);

    Console.WriteLine($"Connecting to {serverUrl}...");
    await ws.ConnectAsync(new Uri(serverUrl), CancellationToken.None);

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"✅ Tunnel established!");
    Console.WriteLine($"🌍 Public URL: http://{clientId}.{publicDomainBase}/");
    Console.ResetColor();

    var buffer = new byte[1024 * 64];
    var sendLock = new SemaphoreSlim(1, 1);

    // Heartbeat (Ping)
    _ = Task.Run(async () =>
    {
        var pingPacket = new byte[] { 0x00 };
        while (ws.State == WebSocketState.Open)
        {
            await Task.Delay(5000);
            await sendLock.WaitAsync();
            try
            {
                if (ws.State == WebSocketState.Open)
                    await ws.SendAsync(new ArraySegment<byte>(pingPacket), WebSocketMessageType.Binary, true, CancellationToken.None);
            }
            catch { /* Ignore */ }
            finally { sendLock.Release(); }
        }
    });

    // Main Loop
    while (ws.State == WebSocketState.Open)
    {
        var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (result.MessageType == WebSocketMessageType.Close) break;

        var packet = new byte[result.Count];
        Array.Copy(buffer, packet, result.Count);

        _ = Task.Run(async () =>
        {
            Stream? bodyStream = null;
            try
            {
                var idBytes = new byte[16];
                Array.Copy(packet, 0, idBytes, 0, 16);

                string command = Encoding.UTF8.GetString(packet, 16, packet.Length - 16);
                var parts = command.Split(' ', 2);

                if (parts.Length < 2) return;

                string method = parts[0];
                string path = parts[1];
                string targetUrl = $"{localBaseUrl}{path}";

                Console.WriteLine($"[📥 IN] {method} {path}");

                if (chaosConfig.LatencyMs > 0) await Task.Delay(chaosConfig.LatencyMs);

                HttpResponseMessage response;
                int statusCode;
                string contentType;

                try
                {
                    response = await httpClient.GetAsync(targetUrl, HttpCompletionOption.ResponseHeadersRead);
                    statusCode = (int)response.StatusCode;
                    contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                    bodyStream = await response.Content.ReadAsStreamAsync();
                }
                catch (HttpRequestException)
                {
                    statusCode = 502;
                    contentType = "text/plain";
                    bodyStream = new MemoryStream(Encoding.UTF8.GetBytes("Local server error"));
                }

                var color = statusCode == 200 ? ConsoleColor.Gray : ConsoleColor.Yellow;
                if (statusCode >= 400) color = ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.WriteLine($"   [📤 OUT] {statusCode} {contentType} (Multiplexing...)");
                Console.ResetColor();

                byte[] typeBytes = Encoding.UTF8.GetBytes(contentType);
                byte[] typeLenBytes = BitConverter.GetBytes(typeBytes.Length);
                byte[] statusBytes = BitConverter.GetBytes(statusCode);

                // --- 1. HEADER PACKET (Type = 0x01) ---
                var headerPacket = new byte[16 + 1 + 4 + 4 + typeBytes.Length];
                Array.Copy(idBytes, 0, headerPacket, 0, 16);
                headerPacket[16] = 0x01; // <--- Type: Header
                Array.Copy(statusBytes, 0, headerPacket, 17, 4);
                Array.Copy(typeLenBytes, 0, headerPacket, 21, 4);
                Array.Copy(typeBytes, 0, headerPacket, 25, typeBytes.Length);

                await sendLock.WaitAsync();
                try { if (ws.State == WebSocketState.Open) await ws.SendAsync(new ArraySegment<byte>(headerPacket), WebSocketMessageType.Binary, true, CancellationToken.None); }
                finally { sendLock.Release(); }

                // --- 2. DATA PACKET (Type = 0x02) ---
                var localBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(1024 * 16); // 16 KB буфер
                try
                {
                    int bytesRead;
                    while ((bytesRead = await bodyStream.ReadAsync(localBuffer)) > 0)
                    {
                        var chunkPacket = new byte[16 + 1 + bytesRead];
                        Array.Copy(idBytes, 0, chunkPacket, 0, 16);
                        chunkPacket[16] = 0x02; // <--- Type: Chunk
                        Array.Copy(localBuffer, 0, chunkPacket, 17, bytesRead);

                        await sendLock.WaitAsync(); // Block only for a moment while send the chunk
                        try { if (ws.State == WebSocketState.Open) await ws.SendAsync(new ArraySegment<byte>(chunkPacket), WebSocketMessageType.Binary, true, CancellationToken.None); }
                        finally { sendLock.Release(); }
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(localBuffer);
                }

                // --- 3. EOF PACKET (Type = 0x03) ---
                var eofPacket = new byte[17];
                Array.Copy(idBytes, 0, eofPacket, 0, 16);
                eofPacket[16] = 0x03; // <--- Type: EOF

                await sendLock.WaitAsync();
                try { if (ws.State == WebSocketState.Open) await ws.SendAsync(new ArraySegment<byte>(eofPacket), WebSocketMessageType.Binary, true, CancellationToken.None); }
                finally { sendLock.Release(); }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Err] {ex.Message}");
                Console.ResetColor();
            }
            finally
            {
                if (bodyStream != null) await bodyStream.DisposeAsync();
            }
        });
    }
}