using EntropyTunnel.Core;
using System.Net.WebSockets;
using System.Text;

Console.WriteLine("--- TUNNEL AGENT v6.1 (With Logs) ---");

// УВАГА: Перевір порти! Зазвичай Server=5073, LocalApp=5174.
// У твоєму коді вони були навпаки, я повернув стандартні, 
// але якщо ти змінив порти у запуску - поправ тут.
string serverUrl = "ws://localhost:5174/tunnel";
// Перевір адресу!
string localBaseUrl = "http://localhost:5073";

var config = new ChaosConfig { LatencyMs = 20, JitterMs = 5, PacketLossRate = 0.0 };

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(30);

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
    await ws.ConnectAsync(new Uri(serverUrl), CancellationToken.None);
    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Connected to Relay ({serverUrl})! ✅");
    Console.ResetColor();

    var buffer = new byte[1024 * 64];
    var sendLock = new SemaphoreSlim(1, 1);

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
                string method = parts[0];
                string path = parts[1];
                string targetUrl = $"{localBaseUrl}{path}";

                // ЛОГ ЗАПИТУ
                Console.WriteLine($"[📥 IN] {method} {path}");

                if (config.LatencyMs > 0) await Task.Delay(config.LatencyMs);

                // Робмо запит
                var response = await httpClient.GetAsync(targetUrl);
                byte[] data = await response.Content.ReadAsByteArrayAsync();
                int statusCode = (int)response.StatusCode;

                // ДІСТАЄМО СПРАВЖНІЙ ТИП
                string contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";

                // ЛОГ ВІДПОВІДІ
                var color = statusCode == 200 ? ConsoleColor.Gray : ConsoleColor.Yellow;
                if (statusCode >= 400) color = ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.WriteLine($"   [📤 OUT] {statusCode} {contentType} ({data.Length} bytes)");
                Console.ResetColor();

                byte[] typeBytes = Encoding.UTF8.GetBytes(contentType);
                byte[] typeLenBytes = BitConverter.GetBytes(typeBytes.Length);
                byte[] statusBytes = BitConverter.GetBytes(statusCode);

                // ПАКУЄМО
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