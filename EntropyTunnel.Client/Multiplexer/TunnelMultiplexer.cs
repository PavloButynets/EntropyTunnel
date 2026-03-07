using System.Buffers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using EntropyTunnel.Core;
using EntropyTunnel.Core.Models;

namespace EntropyTunnel.Client.Multiplexer;

/// <summary>
/// Owns the active WebSocket connection and handles all outbound framing.
///
/// Request-bound wire format (identified by a non-zero 16-byte request ID):
///   Header  (0x01): [16B RequestId][0x01][4B statusCode][4B contentTypeLen][contentType][4B headersLen][headersJSON]
///   Chunk   (0x02): [16B RequestId][0x02][N bytes body chunk]
///   EOF     (0x03): [16B RequestId][0x03]
///   Ping    (0x00): [0x00]
///
/// Control frame wire format (identified by Guid.Empty as the first 16 bytes):
///   [16B: Guid.Empty][1B: type][4B: jsonLen][N B: UTF-8 JSON]
///   Types: 0x20 SyncRules (Server->Client), 0x21 LogEvent (Client->Server), 0x22 SessionAuth (Server->Client)
/// </summary>
public sealed class TunnelMultiplexer
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

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

    // Control Frames

    /// <summary>
    /// Sends a 0x21 LogEvent control frame to the server carrying the completed
    /// <paramref name="entry"/> serialized as camelCase JSON.
    /// RequestBodyPreview is truncated to 2 KB before serialization to cap frame size.
    /// </summary>
    public Task SendLogEventAsync(RequestLogEntry entry, CancellationToken ct)
    {
        if (entry.RequestBodyPreview?.Length > 2048)
            entry = entry with { RequestBodyPreview = entry.RequestBodyPreview[..2048] };

        return SendControlFrameAsync(ControlFrame.LogEvent, entry, ct);
    }

    /// <summary>
    /// Tries to parse an inbound packet as a control frame (0x20 SyncRules or 0x22 SessionAuth).
    /// Returns true when successful; <paramref name="frameType"/> and <paramref name="jsonPayload"/>
    /// are set only on success.
    ///
    /// A control frame is recognized by bytes 0–15 being all zero (Guid.Empty).
    /// </summary>
    public static bool TryParseControlFrame(byte[] packet, out byte frameType, out string jsonPayload)
    {
        frameType = 0;
        jsonPayload = string.Empty;

        // Minimum: 16B Guid.Empty + 1B type + 4B jsonLen = 21 bytes
        if (packet.Length < 21) return false;

        // All 16 request-ID bytes must be zero to qualify as a control frame
        for (int i = 0; i < 16; i++)
            if (packet[i] != 0) return false;

        byte type = packet[16];
        if (type != ControlFrame.SyncRules && type != ControlFrame.SessionAuth)
            return false;

        int jsonLen = BitConverter.ToInt32(packet, 17);
        if (jsonLen < 0 || packet.Length < 21 + jsonLen) return false;

        frameType = type;
        jsonPayload = Encoding.UTF8.GetString(packet, 21, jsonLen);
        return true;
    }

    /// <summary>
    /// Deserializes a JSON payload previously extracted by <see cref="TryParseControlFrame"/>
    /// into the requested payload type.
    /// </summary>
    public static T? DeserializePayload<T>(string jsonPayload) =>
        JsonSerializer.Deserialize<T>(jsonPayload, _jsonOptions);

    private Task SendControlFrameAsync<T>(byte frameType, T payload, CancellationToken ct)
    {
        string json = JsonSerializer.Serialize(payload, _jsonOptions);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        // [16B Guid.Empty][1B type][4B jsonLen][N B JSON]
        var packet = new byte[16 + 1 + 4 + jsonBytes.Length];
        // bytes 0-15 stay zero (Guid.Empty)
        packet[16] = frameType;
        BitConverter.GetBytes(jsonBytes.Length).CopyTo(packet, 17);
        jsonBytes.CopyTo(packet, 21);

        return SendRawAsync(packet, ct);
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
