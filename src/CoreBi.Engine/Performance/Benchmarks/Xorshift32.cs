using System.Runtime.CompilerServices;

namespace CoreBi.Engine.Performance.Benchmarks;

/// <summary>PRNG xorshift32 rápido e determinístico compartilhado pelos benchmarks de CPU e cena.</summary>
internal static class Xorshift32
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float NextFloat(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state >> 8) * (1f / 16777216f);
    }
}
