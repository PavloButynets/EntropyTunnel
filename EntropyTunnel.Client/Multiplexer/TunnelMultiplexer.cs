using System.Buffers;
using System.Net.WebSockets;
using System.Text;

namespace EntropyTunnel.Client.Multiplexer;

/// <summary>
/// Owns the active WebSocket connection and handles all outbound framing.
///
/// Wire format:
///   Header  (0x01): [16B RequestId][0x01][4B statusCode][4B contentTypeLen][contentType]
///   Chunk   (0x02): [16B RequestId][0x02][N bytes body chunk]
///   EOF     (0x03): [16B RequestId][0x03]
///   Ping    (0x00): [0x00]
/// </summary>
public sealed class TunnelMultiplexer
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ClientWebSocket? _ws;

    public WebSocketState State => _ws?.State ?? WebSocketState.None;

    public void AttachWebSocket(ClientWebSocket ws) => _ws = ws;
    public void DetachWebSocket() => _ws = null;


    public Task SendPingAsync(CancellationToken ct) =>
        SendRawAsync([0x00], ct);

    /// <summary>
    /// Frames and sends a complete response for <paramref name="requestId"/>:
    /// one 0x01 header, zero-or-more 0x02 body chunks, one 0x03 EOF.
    /// </summary>
    public async Task SendResponseAsync(
        Guid requestId,
        int statusCode,
        string contentType,
        Stream body,
        Dictionary<string, string[]> headers,
        CancellationToken ct)
    {
        byte[] idBytes = requestId.ToByteArray();

        // ── 1. Header packet (0x01)
        byte[] typeBytes = Encoding.UTF8.GetBytes(contentType);
        byte[] typeLenBytes = BitConverter.GetBytes(typeBytes.Length);
        byte[] statusBytes = BitConverter.GetBytes(statusCode);

        string headersJson = System.Text.Json.JsonSerializer.Serialize(headers);
        byte[] headersBytes = Encoding.UTF8.GetBytes(headersJson);
        byte[] headersLenBytes = BitConverter.GetBytes(headersBytes.Length);

        var headerPacket = new byte[16 + 1 + 4 + 4 + typeBytes.Length + 4 + headersBytes.Length];
        Array.Copy(idBytes, 0, headerPacket, 0, 16);
        headerPacket[16] = 0x01;
        Array.Copy(statusBytes, 0, headerPacket, 17, 4);
        Array.Copy(typeLenBytes, 0, headerPacket, 21, 4);
        Array.Copy(typeBytes, 0, headerPacket, 25, typeBytes.Length);
        Array.Copy(headersLenBytes, 0, headerPacket, 25 + typeBytes.Length, 4);
        Array.Copy(headersBytes, 0, headerPacket, 29 + typeBytes.Length, headersBytes.Length);

        await SendRawAsync(headerPacket, ct);

        // ── 2. Data chunks (0x02)
        const int ChunkSize = 16 * 1024; // 16 KB - matches original
        byte[] localBuffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
        try
        {
            int bytesRead;
            while ((bytesRead = await body.ReadAsync(localBuffer.AsMemory(0, ChunkSize), ct)) > 0)
            {
                var chunkPacket = new byte[16 + 1 + bytesRead];
                Array.Copy(idBytes, 0, chunkPacket, 0, 16);
                chunkPacket[16] = 0x02;
                Array.Copy(localBuffer, 0, chunkPacket, 17, bytesRead);

                await SendRawAsync(chunkPacket, ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(localBuffer);
        }

        // - 3. EOF packet (0x03)
        var eofPacket = new byte[17];
        Array.Copy(idBytes, 0, eofPacket, 0, 16);
        eofPacket[16] = 0x03;

        await SendRawAsync(eofPacket, ct);
    }

    private async Task SendRawAsync(byte[] packet, CancellationToken ct)
    {
        if (_ws is null || _ws.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync(ct);
        try
        {
            if (_ws.State == WebSocketState.Open)
                await _ws.SendAsync(
                    new ArraySegment<byte>(packet),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }
}
