using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EntropyTunnel.Core
{
    // Цей клас прикидається звичайним потоком (Stream), 
    // але всередині робить капості
    public class ChaosStream : Stream
    {
        private readonly Stream _innerStream;
        private readonly ChaosConfig _config;

        public ChaosStream(Stream innerStream, ChaosConfig config)
        {
            _innerStream = innerStream;
            _config = config;
        }

        // Перехоплюємо запис даних
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_config.IsEnabled)
            {
                // 1. Втрата пакетів
                if (_config.PacketLossRate > 0 && Random.Shared.NextDouble() < _config.PacketLossRate)
                {
                    Console.WriteLine("❌ Packet LOST!");
                    return; // Просто виходимо, не записуючи дані (імітація втрати)
                }

                // 2. Затримка (Latency + Jitter)
                if (_config.LatencyMs > 0)
                {
                    double delay = MathUtils.NextGaussian(_config.LatencyMs, _config.JitterMs);
                    int finalDelay = Math.Max(0, (int)delay);

                    if (finalDelay > 0)
                    {
                        // Console.WriteLine($"⏳ Delay: {finalDelay}ms");
                        await Task.Delay(finalDelay, cancellationToken);
                    }
                }
            }

            // Якщо пакет вижив — передаємо далі
            await _innerStream.WriteAsync(buffer, offset, count, cancellationToken);
        }

        // --- Обов'язкові методи Stream (просто прокидаємо їх далі) ---
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => await _innerStream.ReadAsync(buffer, offset, count, cancellationToken);

        public override bool CanRead => _innerStream.CanRead;
        public override bool CanSeek => _innerStream.CanSeek;
        public override bool CanWrite => _innerStream.CanWrite;
        public override long Length => _innerStream.Length;
        public override long Position { get => _innerStream.Position; set => _innerStream.Position = value; }
        public override void Flush() => _innerStream.Flush();
        public override long Seek(long offset, SeekOrigin origin) => _innerStream.Seek(offset, origin);
        public override void SetLength(long value) => _innerStream.SetLength(value);
        public override int Read(byte[] buffer, int offset, int count) => _innerStream.Read(buffer, offset, count);
        public override void Write(byte[] buffer, int offset, int count) => _innerStream.Write(buffer, offset, count);
    }
}