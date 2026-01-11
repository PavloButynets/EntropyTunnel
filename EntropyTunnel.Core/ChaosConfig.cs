namespace EntropyTunnel.Core
{
    public class ChaosConfig
    {
        public bool IsEnabled { get; set; } = true;

        // Середня затримка (мс)
        public int LatencyMs { get; set; } = 0;

        // Джитер (мс) — наскільки затримка "скаче"
        public int JitterMs { get; set; } = 0;

        // Відсоток втрачених пакетів (0.05 = 5%)
        public double PacketLossRate { get; set; } = 0.0;
    }
}