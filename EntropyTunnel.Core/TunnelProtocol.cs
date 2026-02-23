using System;

namespace EntropyTunnel.Core
{
    public enum FrameType : byte
    {
        Open = 0x01,
        Data = 0x02,
        Close = 0x03
    }

    // Структура нашого пакета в байтах:
    // [Guid (16 bytes)] [Type (1 byte)] [Payload (N bytes)]
    public static class TunnelProtocol
    {
        public const int HeaderSize = 17; // 16 bytes ID + 1 byte Type

        public static byte[] Wrap(Guid connectionId, FrameType type, byte[] data, int count)
        {
            var frame = new byte[HeaderSize + count];

            // 1. ID (16 байт)
            Array.Copy(connectionId.ToByteArray(), 0, frame, 0, 16);

            // 2. Type (1 байт)
            frame[16] = (byte)type;

            // 3. Payload (N bites)
            if (count > 0 && data != null)
            {
                Array.Copy(data, 0, frame, HeaderSize, count);
            }

            return frame;
        }

        public static (Guid Id, FrameType Type, byte[] Payload) Unwrap(byte[] buffer, int count)
        {
            if (count < HeaderSize) throw new Exception("Invalid frame size");

            var idBytes = new byte[16];
            Array.Copy(buffer, 0, idBytes, 0, 16);
            var id = new Guid(idBytes);

            var type = (FrameType)buffer[16];

            var payloadSize = count - HeaderSize;
            var payload = new byte[payloadSize];
            Array.Copy(buffer, HeaderSize, payload, 0, payloadSize);

            return (id, type, payload);
        }
    }
}