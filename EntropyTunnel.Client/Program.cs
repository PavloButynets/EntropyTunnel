using EntropyTunnel.Core;
using System.Net.WebSockets;
using System.Text;

// --- Parse Port and ClientID from arguments ---
if (args.Length < 2 || !int.TryParse(args[0], out int localPort))
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("⚠️  Usage: EntropyTunnel.Client <port> <client-id>");
    Console.WriteLine("   Example: dotnet run -- 5173 app1");
    Console.ResetColor();
    return;
}

string clientId = args[1];
// ----------------------------------

Console.WriteLine($"--- TUNNEL AGENT v7.0 (Port: {localPort}, ID: {clientId}) ---");

// Added clientId to the query string
string serverUrl = $"ws://13.60.182.126:8080/tunnel?clientId={clientId}";
string localBaseUrl = $"http://localhost:{localPort}";

var config = new ChaosConfig { LatencyMs = 20, JitterMs = 5, PacketLossRate = 0.0 };

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
    // Display the correct public URL with the client ID
    Console.WriteLine($"🌍 Public URL: http://{clientId}.13.60.182.126.nip.io:8080/");
    //  Console.WriteLine($"👉 Local:      {localBaseUrl}");
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

                if (config.LatencyMs > 0) await Task.Delay(config.LatencyMs);

                HttpResponseMessage response;
                byte[] data;
                int statusCode;
                string contentType;

                try
                {
                    response = await httpClient.GetAsync(targetUrl);
                    data = await response.Content.ReadAsByteArrayAsync();
                    statusCode = (int)response.StatusCode;
                    contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
                }
                catch (HttpRequestException)
                {
                    // Local server error handling
                    statusCode = 502; // Bad Gateway
                    data = Encoding.UTF8.GetBytes("Local server error");
                    contentType = "text/plain";
                }

                var color = statusCode == 200 ? ConsoleColor.Gray : ConsoleColor.Yellow;
                if (statusCode >= 400) color = ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.WriteLine($"   [📤 OUT] {statusCode} {contentType} ({data.Length} bytes)");
                Console.ResetColor();

                byte[] typeBytes = Encoding.UTF8.GetBytes(contentType);
                byte[] typeLenBytes = BitConverter.GetBytes(typeBytes.Length);
                byte[] statusBytes = BitConverter.GetBytes(statusCode);

                // Protocol v2: [ID 16] [Status 4] [TypeLen 4] [Type N] [Body M]
                var responsePacket = new byte[16 + 4 + 4 + typeBytes.Length + data.Length];

                int offset = 0;
                Array.Copy(idBytes, 0, responsePacket, offset, 16); offset += 16;
                Array.Copy(statusBytes, 0, responsePacket, offset, 4); offset += 4;
                Array.Copy(typeLenBytes, 0, responsePacket, offset, 4); offset += 4;
                Array.Copy(typeBytes, 0, responsePacket, offset, typeBytes.Length); offset += typeBytes.Length;
                Array.Copy(data, 0, responsePacket, offset, data.Length);

                await sendLock.WaitAsync();
                try
                {
                    if (ws.State == WebSocketState.Open)
                        await ws.SendAsync(new ArraySegment<byte>(responsePacket), WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                finally { sendLock.Release(); }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[Err] {ex.Message}");
                Console.ResetColor();
            }
        });
    }
}