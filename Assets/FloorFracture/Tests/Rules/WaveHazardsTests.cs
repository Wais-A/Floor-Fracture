using System.Collections.Generic;
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

        [Test] public void HazardsUnlockByLevel()
        {
            for (int s = 0; s < 500; s++)
            {
                var l2 = WaveHazards.Roll(2, 3, s, 10, 2); var l3 = WaveHazards.Roll(3, 3, s, 10, 2); var l4 = WaveHazards.Roll(4, 3, s, 10, 2);
                Assert.That(l2.FogPatches + l2.Spotlights + l2.Strikes.Count, Is.Zero);
                Assert.That(l3.Spotlights + l3.Strikes.Count, Is.Zero);
                Assert.That(l4.Strikes.Count, Is.Zero);
            }
        }

        [Test] public void HigherLevelsCombineHazards()
        {
            int combined = 0, fog = 0, patches = 0, lights = 0, strikes = 0;
            for (int s = 0; s < 1000; s++)
            {
                var h = WaveHazards.Roll(12, 2, s, 10, 1);
                int kinds = (h.FogDensity > 0 ? 1 : 0) + (h.FogPatches > 0 ? 1 : 0) + (h.Spotlights > 0 ? 1 : 0) + (h.Strikes.Count > 0 ? 1 : 0);
                if (kinds >= 2) combined++;
                if (h.FogDensity > 0) fog++; if (h.FogPatches > 0) patches++; if (h.Spotlights > 0) lights++; if (h.Strikes.Count > 0) strikes++;
            }
            Assert.That(combined, Is.EqualTo(1000));
            Assert.That(new[] { fog, patches, lights, strikes }, Has.All.GreaterThan(200));
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

        [Test] public void FlashesNeverExceedThreePerSecond()
        {
            var waves = new List<IReadOnlyList<double>> { new[] { 0.0, 1.2 } };
            for (int s = 0; s < 40; s++) waves.Add(WaveHazards.Roll(10, 2, s, 0, 1).Strikes);
            foreach (var strikes in waves)
            {
                var rises = new List<double>();
                float prev = 0;
                for (int ms = 0; ms < 5000; ms++)
                {
                    double t = ms / 1000.0; float level = 0;
                    foreach (double strike in strikes) level = System.Math.Max(level, WaveHazards.FlashIntensity(t - strike, false));
                    if (prev < .5f && level >= .5f) rises.Add(t);
                    prev = level;
                }
                for (int i = 0; i + 3 < rises.Count; i++)
                    Assert.That(rises[i + 3] - rises[i], Is.GreaterThanOrEqualTo(1.0), "More than three flashes in one second.");
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
