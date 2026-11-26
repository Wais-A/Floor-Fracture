using System;
using System.Collections.Generic;

namespace FloorFracture
{
    // Visual hazards: each level's fog and each wave's lights and lightning. Rolled from their own random streams so break patterns never change.
    public sealed class WaveHazards
    {
        public static readonly WaveHazards None = new WaveHazards(0, 0, 0, Array.Empty<double>(), 0);

        public float FogDensity { get; }
        public int FogPatches { get; }
        public int Spotlights { get; }
        public IReadOnlyList<double> Strikes { get; }
        public int VisualSeed { get; }
        public bool Any => FogDensity > 0 || FogPatches > 0 || Spotlights > 0 || Strikes.Count > 0;

        public WaveHazards(float fogDensity, int fogPatches, int spotlights, IReadOnlyList<double> strikes, int visualSeed)
        {
            FogDensity = fogDensity;
            FogPatches = fogPatches;
            Spotlights = spotlights;
            Strikes = strikes;
            VisualSeed = visualSeed;
        }

        // The level's fog, the same for every wave of one level in one run: density from L2, patches from L3.
        public static WaveHazards LevelFog(int level, int seed)
        {
            if (level < 2) return None;
            var random = new WavePlanner.Generator(unchecked(seed ^ level * (int)0x9E3779B1));
            return new WaveHazards(.35f + .65f * Math.Min(1f, Math.Max(0f, (level - 2) / 8f)),
                level < 3 ? 0 : 1 + (level >= 7 ? 1 : 0) + (level >= 10 ? 1 : 0), 0, Array.Empty<double>(), random.Next(1 << 30));
        }

        // Per-wave kinds, in the order they unlock: spotlights (L4), lightning (L5). Fog belongs to the level.
        private static readonly int[] UnlockLevel = { 4, 5 };

        public static WaveHazards Roll(int level, int number, int seed, double startsAt, double duration)
        {
            if (level < UnlockLevel[0]) return None;
            var random = new WavePlanner.Generator(unchecked(seed ^ 0x5F3759DF));
            int count = level >= 10 ? 1 + random.Next(2) : level >= 7 ? 1 : random.Next(2);
            var pool = new List<int>();
            for (int kind = 0; kind < UnlockLevel.Length; ++kind) if (level >= UnlockLevel[kind]) pool.Add(kind);
            bool lights = false, lightning = false;
            for (int i = 0; i < count && pool.Count > 0; ++i)
            {
                int pick = random.Next(pool.Count);
                if (pool[pick] == 0) lights = true; else lightning = true;
                pool.RemoveAt(pick);
            }
            var strikes = new List<double>();
            if (lightning)
            {
                double low = startsAt + .25, high = startsAt + duration + 1.5, first = Between(ref random, low, high);
                strikes.Add(first);
                if (level >= 9)
                {
                    double second = Between(ref random, low, high);
                    // shortcut: bounded redraw, then use the window's two ends (always >= 1.25 s apart).
                    for (int tries = 0; Math.Abs(second - first) < 1.2 && tries < 32; ++tries) second = Between(ref random, low, high);
                    if (Math.Abs(second - first) < 1.2) { first = low; second = high; strikes[0] = first; }
                    strikes.Add(second);
                    strikes.Sort();
                }
            }
            return new WaveHazards(0, 0, lights ? 1 + (level >= 8 ? 1 : 0) + (level >= 11 ? 1 : 0) : 0,
                strikes.AsReadOnly(), random.Next(1 << 30));
        }

        private static double Between(ref WavePlanner.Generator random, double low, double high) =>
            low + (high - low) * random.Next(1 << 20) / ((1 << 20) - 1);

        // 0-1 sky brightness at `sinceStrike` seconds after a strike.
        public static float FlashIntensity(double sinceStrike, bool reduced)
        {
            double t = sinceStrike;
            if (t < 0) return 0;
            if (reduced)
            {
                if (t >= .6) return 0;
                return (float)(t < .1 ? .35 * Math.Sin(Math.PI / 2 * t / .1) : .35 * .5 * (1 + Math.Cos(Math.PI * (t - .1) / .5)));
            }
            if (t < .06) return 1f;
            if (t < .16) return 0f;
            if (t < .22) return .7f;
            return t < .5 ? (float)(.7 * (1 - (t - .22) / .28)) : 0f;
        }

        public static double ThunderDelay(int visualSeed, int strike)
        {
            var random = new WavePlanner.Generator(unchecked(visualSeed ^ strike * (int)0x9E3779B1));
            return .3 + .9 * random.Next(1000) / 999.0;
        }

        public static float Fade(double now, double shownAt, double hiddenAt)
        {
            double rise = Math.Min(1, Math.Max(0, (now - shownAt) / .35));
            double fall = double.IsPositiveInfinity(hiddenAt) ? 1 : Math.Min(1, Math.Max(0, 1 - (now - hiddenAt) / .6));
            double fade = Math.Min(rise, fall);
            return fade < 1e-9 ? 0f : (float)fade; // absorbs double rounding at the endpoints
        }

        // Level fog rolls in over 3 s and out over 1.5 s, inside the 1.8 s level-clear pause.
        public static float FogFade(double now, double shownAt, double hiddenAt)
        {
            static double Smooth(double x) { x = Math.Min(1, Math.Max(0, x)); return x * x * (3 - 2 * x); }
            double fade = Math.Min(Smooth((now - shownAt) / 3), 1 - Smooth((now - hiddenAt) / 1.5));
            return fade < 1e-9 ? 0f : (float)fade;
        }

        public static float LampHeight(float floorY, float? ceilingY) =>
            Math.Min(floorY + 2.6f, Math.Max(floorY + 2.0f, (ceilingY ?? floorY + 2.55f) - .15f));
    }
}
