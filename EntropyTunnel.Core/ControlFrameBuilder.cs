using System.Text;
using System.Text.Json;

namespace EntropyTunnel.Core;

/// <summary>
/// Builds and parses binary control frames for the EntropyTunnel wire protocol.
/// Shared between the Server (sends 0x20/0x22 to agents) and the Client
/// TunnelMultiplexer (sends 0x21 to the server).
///
/// Wire format: [16B: Guid.Empty][1B: type][4B: jsonLen][N B: UTF-8 camelCase JSON]
/// </summary>
public static class ControlFrameBuilder
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>Serializes <paramref name="payload"/> and wraps it in a control frame packet.</summary>
    public static byte[] Build<T>(byte frameType, T payload)
    {
        string json = JsonSerializer.Serialize(payload, JsonOptions);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(json);

        var packet = new byte[16 + 1 + 4 + jsonBytes.Length];
        // bytes 0-15: Guid.Empty (all zeros - signals this is a control frame)
        packet[16] = frameType;
        BitConverter.GetBytes(jsonBytes.Length).CopyTo(packet, 17);
        jsonBytes.CopyTo(packet, 21);
        return packet;
    }

    /// <summary>Deserializes a JSON string (previously extracted from a control frame) into <typeparamref name="T"/>.</summary>
    public static T? Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, JsonOptions);
}
