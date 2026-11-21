using System;

namespace FloorFracture
{
    internal readonly struct WavePacing
    {
        public readonly double ClueSeconds;
        private readonly int survivorPercent, intervals;

        private WavePacing(double clueSeconds, int survivorPercent, int intervals = 1)
        {
            ClueSeconds = clueSeconds;
            this.survivorPercent = survivorPercent;
            this.intervals = intervals;
        }

        private static readonly double[] OpeningSeconds =
            {
                2.00, 1.75, 1.50, 1.32, 1.16, 1.00,
                0.93, 0.86, 0.80, 0.70, 0.60, 0.50
            };

        public static double SwitchSeconds(int level, int waveNumber)
        {
            int index = Math.Min(level, OpeningSeconds.Length) - 1;
            double opening = OpeningSeconds[index];
            double nextOpening = OpeningSeconds[Math.Min(index + 1, OpeningSeconds.Length - 1)];
            return Math.Max(nextOpening, opening - .08 * Math.Min(waveNumber - 1, 3));
        }

        private static readonly int[] Levels = { 1, 3, 5, 7, 9, 12 };
        private static readonly WavePacing[] Anchors =
        {
            new(.80, 70),
            new(.55, 60),
            new(.40, 52),
            new(.30, 45),
            new(.28, 40),
            new(.25, 35)
        };

        public static WavePacing At(int level)
        {
            for (int i = 1; i < Levels.Length; ++i)
            {
                if (level > Levels[i]) continue;
                int step = level - Levels[i - 1];
                int span = Levels[i] - Levels[i - 1];
                double t = (double)step / span;
                var from = Anchors[i - 1];
                var to = Anchors[i];
                return new WavePacing(from.ClueSeconds + (to.ClueSeconds - from.ClueSeconds) * t,
                    from.survivorPercent * (span - step) + to.survivorPercent * step, span);
            }
            return Anchors[Anchors.Length - 1];
        }

        // Keep the fraction exact until rounding to whole panes, including halfway cases.
        public int TargetSurvivors(int intact) =>
            (int)(((long)intact * survivorPercent + 50 * intervals) / (100 * intervals));

        public static double RecoveryAt(int level) => 1.28 - .68 * (Math.Min(level, 12) - 1) / 11;

        public static double BreakFraction(int level, int group, int count)
        {
            if (group == count - 1 || level < 5) return 1;
            if (level < 7) return .75;
            double first = .65 - .05 * (Math.Min(level, 12) - 7) / 5;
            return first + group * .20;
        }
    }
}
