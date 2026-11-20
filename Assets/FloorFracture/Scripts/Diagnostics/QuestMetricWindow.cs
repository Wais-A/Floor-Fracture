using System;
using System.Globalization;

namespace FloorFracture
{
    // Each field has its own sample count. Missing counters never enter an average as zero.
    internal struct MetricSeries
    {
        public int Count { get; private set; }
        public double Sum { get; private set; }
        public double Max { get; private set; }

        public void Add(double sample)
        {
            if (double.IsNaN(sample) || double.IsInfinity(sample) || sample < 0) return;
            Count++; Sum += sample;
            Max = Math.Max(Max, sample);
        }

        public string Mean => Count == 0 ? "" : QuestMetricWindow.Number(Sum / Count);
        public string Peak => Count == 0 ? "" : QuestMetricWindow.Number(Max);
    }

    internal struct QuestMetricWindow
    {
        public MetricSeries FrameMs, MainThreadMs, TimedCpuMs, TimedGpuMs, XrGpuMs, Draws, Triangles;
        public int OverBudget;
        public double TurnDegrees, TrackedSeconds;

        public void Frame(double seconds)
        {
            if (!(seconds > 0) || double.IsInfinity(seconds)) return;
            double ms = seconds * 1000;
            FrameMs.Add(ms);
            if (seconds >= .0139) OverBudget++;
        }

        public string Fps => FrameMs.Count == 0 ? "" : Number(1000 * FrameMs.Count / FrameMs.Sum);
        public string TurnRate => TrackedSeconds > 0 ? Number(TurnDegrees / TrackedSeconds) : "";
        internal static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        internal static string Integer(long value) => value.ToString(CultureInfo.InvariantCulture);
        internal static string Text(string value) => string.IsNullOrEmpty(value) ? "" :
            "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";
    }
}
