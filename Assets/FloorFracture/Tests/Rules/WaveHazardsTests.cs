using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace FloorFracture.Tests
{
    public sealed class WaveHazardsTests
    {
        [Test] public void LevelOneHasNoHazards()
        {
            for (int s = 0; s < 500; s++) for (int n = 1; n < 8; n++)
                Assert.That(WaveHazards.Roll(1, n, s, 10, 2).Any, Is.False);
        }

        static int Kinds(WaveHazards h) => (h.Spotlights > 0 ? 1 : 0) + (h.Strikes.Count > 0 ? 1 : 0);

        [Test] public void HazardsUnlockByLevel()
        {
            for (int s = 0; s < 500; s++)
            {
                Assert.That(WaveHazards.Roll(3, 3, s, 10, 2).Any, Is.False);
                Assert.That(WaveHazards.Roll(4, 3, s, 10, 2).Strikes.Count, Is.Zero);
                Assert.That(Kinds(WaveHazards.Roll(6, 3, s, 10, 2)), Is.InRange(0, 1));
                Assert.That(Kinds(WaveHazards.Roll(8, 3, s, 10, 2)), Is.EqualTo(1));
            }
        }

        [Test] public void HigherLevelsCombineHazards()
        {
            int lights = 0, strikes = 0;
            for (int s = 0; s < 1000; s++)
            {
                var h = WaveHazards.Roll(12, 2, s, 10, 1);
                Assert.That(Kinds(h), Is.GreaterThanOrEqualTo(1));
                if (h.Spotlights > 0) lights++; if (h.Strikes.Count > 0) strikes++;
            }
            Assert.That(new[] { lights, strikes }, Has.All.GreaterThan(300));
        }

        [Test] public void LevelFogIsStableAndUnlocksByLevel()
        {
            Assert.That(WaveHazards.LevelFog(1, 7).FogDensity, Is.Zero);
            Assert.That(WaveHazards.LevelFog(2, 7).FogDensity, Is.EqualTo(.35f).Within(1e-4f));
            Assert.That(WaveHazards.LevelFog(2, 7).FogPatches, Is.Zero);
            Assert.That(WaveHazards.LevelFog(3, 7).FogPatches, Is.EqualTo(1));
            Assert.That(WaveHazards.LevelFog(7, 7).FogPatches, Is.EqualTo(2));
            Assert.That(WaveHazards.LevelFog(10, 7).FogPatches, Is.EqualTo(3));
            Assert.That(WaveHazards.LevelFog(6, 7).VisualSeed, Is.EqualTo(WaveHazards.LevelFog(6, 7).VisualSeed));
            Assert.That(WaveHazards.LevelFog(6, 7).Spotlights + WaveHazards.LevelFog(6, 7).Strikes.Count, Is.Zero);
        }

        [Test] public void WavesNoLongerRollFog()
        {
            for (int level = 1; level <= 14; level++) for (int n = 1; n <= 8; n++) for (int s = 0; s < 200; s++)
            {
                var h = WaveHazards.Roll(level, n, s, 10, 2);
                Assert.That(h.FogDensity, Is.Zero);
                Assert.That(h.FogPatches, Is.Zero);
                if (level <= 3) Assert.That(h.Any, Is.False);
            }
        }

        [Test] public void FogFadesInSlowlyAndOutWithinTheLevelClear()
        {
            Assert.That(WaveHazards.FogFade(0, 0, double.PositiveInfinity), Is.Zero);
            Assert.That(WaveHazards.FogFade(1.5, 0, double.PositiveInfinity), Is.InRange(.4f, .6f));
            Assert.That(WaveHazards.FogFade(3.0, 0, double.PositiveInfinity), Is.EqualTo(1f));
            Assert.That(WaveHazards.FogFade(10.0, 0, 10.0), Is.EqualTo(1f));
            Assert.That(WaveHazards.FogFade(11.5, 0, 10.0), Is.Zero); // 1.5 s < the 1.8 s level-clear pause
        }

        [Test] public void RollIsDeterministic() =>
            Assert.That(WaveHazards.Roll(9, 4, 379, 3.5, 1.2).Strikes, Is.EqualTo(WaveHazards.Roll(9, 4, 379, 3.5, 1.2).Strikes));

        [Test] public void StrikesStayInTheirWindowAndApart()
        {
            for (int s = 0; s < 500; s++)
            {
                var h = WaveHazards.Roll(10, 2, s, 20, 1);
                foreach (double t in h.Strikes) Assert.That(t, Is.InRange(20.25, 22.5));
                for (int i = 1; i < h.Strikes.Count; i++) Assert.That(h.Strikes[i] - h.Strikes[i - 1], Is.GreaterThanOrEqualTo(1.2));
            }
        }

        // Counts flash pulses (rises past half brightness) at 1 ms steps around the strikes.
        static void AssertAtMostThreeFlashesPerSecond(IReadOnlyList<double> strikes)
        {
            if (strikes.Count == 0) return;
            var rises = new List<double>();
            float prev = 0;
            for (long ms = (long)(strikes.Min() * 1000) - 100; ms <= (long)(strikes.Max() * 1000) + 1000; ms++)
            {
                double t = ms / 1000.0; float level = 0;
                foreach (double strike in strikes) level = System.Math.Max(level, WaveHazards.FlashIntensity(t - strike, false));
                if (prev < .5f && level >= .5f) rises.Add(t);
                prev = level;
            }
            for (int i = 0; i + 3 < rises.Count; i++)
                Assert.That(rises[i + 3] - rises[i], Is.GreaterThanOrEqualTo(1.0), "More than three flashes in one second: " + string.Join(", ", strikes));
        }

        [Test] public void FlashesNeverExceedThreePerSecond()
        {
            var waves = new List<IReadOnlyList<double>> { new[] { 0.0, WaveHazards.StrikeSpacing } };
            for (int s = 0; s < 40; s++) waves.Add(WaveHazards.Roll(10, 2, s, 0, 1).Strikes);
            foreach (var strikes in waves) AssertAtMostThreeFlashesPerSecond(strikes);
        }

        [Test] public void BackToBackWavesNeverFlashMoreThanThreeTimesPerSecond()
        {
            for (int level = 5; level <= 14; level++) for (int number = 1; number <= 4; number++) for (int s = 0; s < 50; s++)
            {
                double duration = WavePacing.SwitchSeconds(level, number), next = 10 + duration + WavePacing.RecoveryAt(level);
                var old = WaveHazards.Roll(level, number, s, 10, duration).Strikes;
                var current = WaveHazards.Roll(level, number + 1, s + 1000, next, WavePacing.SwitchSeconds(level, number + 1)).Strikes;
                // As Lightning does: the old wave's strikes show until the next wave replaces them, and each flash is spaced from the last.
                double shown = double.NegativeInfinity;
                var flashed = new List<double>();
                foreach (double strike in old.Where(t => t < next).Concat(current))
                    if (WaveHazards.MayFlash(shown, strike)) flashed.Add(shown = strike);
                AssertAtMostThreeFlashesPerSecond(flashed);
            }
        }

        [Test] public void ReducedMotionStrikeIsOneSoftGlow()
        {
            float max = 0, prev = 0; int peaks = 0; bool rising = false;
            for (int ms = 0; ms <= 1000; ms++)
            {
                float v = WaveHazards.FlashIntensity(ms / 1000.0, true);
                max = System.Math.Max(max, v);
                if (v > prev) rising = true;
                else if (v < prev && rising) { peaks++; rising = false; }
                if (ms >= 600) Assert.That(v, Is.Zero);
                prev = v;
            }
            Assert.That(max, Is.LessThanOrEqualTo(.35f + 1e-5f));
            Assert.That(max, Is.GreaterThan(.3f));
            Assert.That(peaks, Is.EqualTo(1));
        }

        [Test] public void ThunderFollowsTheFlash()
        {
            for (int s = 0; s < 200; s++) for (int k = 0; k < 2; k++) Assert.That(WaveHazards.ThunderDelay(s, k), Is.InRange(0.3, 1.2));
        }

        [Test] public void FadeRisesThenFalls()
        {
            Assert.That(WaveHazards.Fade(0, 0, double.PositiveInfinity), Is.Zero);
            Assert.That(WaveHazards.Fade(.35, 0, double.PositiveInfinity), Is.EqualTo(1f));
            Assert.That(WaveHazards.Fade(5.3, 0, 5), Is.EqualTo(.5f).Within(.01f));
            Assert.That(WaveHazards.Fade(5.6, 0, 5), Is.Zero);
        }

        [Test] public void LampHeightStaysUnderTheCeiling()
        {
            Assert.That(WaveHazards.LampHeight(0, null), Is.EqualTo(2.4f).Within(1e-4f));
            Assert.That(WaveHazards.LampHeight(0, 2.5f), Is.EqualTo(2.35f).Within(1e-4f));
            Assert.That(WaveHazards.LampHeight(0, 3.5f), Is.EqualTo(2.6f).Within(1e-4f));
            Assert.That(WaveHazards.LampHeight(0, 2.1f), Is.EqualTo(2.0f).Within(1e-4f));
            Assert.That(WaveHazards.LampHeight(-1.2f, null), Is.EqualTo(1.2f).Within(1e-4f));
        }
    }
}
