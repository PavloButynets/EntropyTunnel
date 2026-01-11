using System;

namespace EntropyTunnel.Core
{
    public static class MathUtils
    {
        public static double NextGaussian(double mean, double stdDev)
        {
            // Генеруємо нормальний розподіл
            double u1 = 1.0 - Random.Shared.NextDouble();
            double u2 = 1.0 - Random.Shared.NextDouble();

            double randStdNormal = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);

            // Масштабуємо
            return mean + stdDev * randStdNormal;
        }
    }
}