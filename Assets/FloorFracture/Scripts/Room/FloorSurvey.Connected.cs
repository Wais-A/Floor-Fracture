using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    internal sealed partial class FloorSurvey
    {
        internal const int DefaultPaneLimit = 12;
        internal const float PreferredMinPitch = .69f;
        private static readonly float[] ConnectedPitches = { .75f, .72f, PreferredMinPitch, .65f, .60f, .55f, .50f, .45f, .40f };
        private static readonly Vector2Int[] Directions = {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
        };

        // Four comfortable panes take precedence over a denser compact fit. Only try compact
        // panes when the preferred range cannot produce four. Two edge-neighbors still suffice.
        private readonly struct FitScore
        {
            private readonly int tier, count, area;
            private readonly float pitch, distance;
            internal bool HasFourPreferred => tier == 3;

            internal FitScore(int count, float pitch, float distance)
            {
                tier = count >= 4 ? (pitch >= PreferredMinPitch ? 3 : 2) : 1;
                this.count = count;
                this.pitch = pitch;
                this.distance = distance;
                area = Mathf.RoundToInt(count * pitch * pitch * 10000);
            }

            internal bool Beats(FitScore other, bool entireFloor)
            {
                if (tier != other.tier) return tier > other.tier;
                if (!entireFloor && tier == 2 && pitch != other.pitch) return pitch > other.pitch;
                if (!entireFloor && count != other.count) return count > other.count;
                if (area != other.area) return area > other.area;
                if (pitch != other.pitch) return pitch > other.pitch;
                return distance < other.distance - .00001f;
            }
        }

        internal static BoardPlacement? FindConnected(IEnumerable<FloorSurvey> evidence, Vector2 preferred,
            CancellationToken cancel, out FloorSurvey owner, bool entireFloor = false, IReadOnlyList<Vector2> suggestions = null)
        {
            var search = new ConnectedSearch(preferred, entireFloor, suggestions, cancel);
            foreach (var survey in evidence) search.Examine(survey);
            owner = search.Owner;
            return search.Best;
        }

        private sealed class ConnectedSearch
        {
            private readonly Vector2 preferred;
            private readonly bool entireFloor;
            private readonly IReadOnlyList<Vector2> suggestions;
            private readonly CancellationToken cancel;
            private FitScore score;
            internal BoardPlacement? Best { get; private set; }
            internal FloorSurvey Owner { get; private set; }

            internal ConnectedSearch(Vector2 preferred, bool entireFloor, IReadOnlyList<Vector2> suggestions, CancellationToken cancel)
            {
                this.preferred = preferred;
                this.entireFloor = entireFloor;
                this.suggestions = suggestions;
                this.cancel = cancel;
            }

            internal bool HasFourPreferred => score.HasFourPreferred;

            // A room or furniture edge can define an exact rotation and offset between our
            // trial grids. Treat its fitted rectangle as an additional verified footprint.
            internal void ExamineRectangle(FloorSurvey survey, BoardPlacement source)
            {
                float spanX = source.Columns * source.Pitch, spanZ = source.Rows * source.Pitch;
                float radians = source.AngleDegrees * Mathf.Deg2Rad;
                var right = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
                var forward = new Vector2(-right.y, right.x);
                float headX = Vector2.Dot(preferred - source.Center, right);
                float headZ = Vector2.Dot(preferred - source.Center, forward);
                foreach (float pitch in ConnectedPitches)
                {
                    int cols = Mathf.FloorToInt((spanX + FloorGeometry.Epsilon) / pitch);
                    int rows = Mathf.FloorToInt((spanZ + FloorGeometry.Epsilon) / pitch);
                    int maxWidth = entireFloor ? cols : Mathf.Min(cols, DefaultPaneLimit);
                    for (int width = entireFloor ? cols : 1; width > 0 && width <= maxWidth; width++)
                    {
                        int height = entireFloor ? rows : Mathf.Min(rows, DefaultPaneLimit / width);
                        int count = width * height;
                        if (count < 2) continue;
                        float dx = Mathf.Max(0, (spanX - width * pitch) * .5f);
                        float dz = Mathf.Max(0, (spanZ - height * pitch) * .5f);
                        var center = source.ToWorldXZ(Mathf.Clamp(headX, -dx, dx), Mathf.Clamp(headZ, -dz, dz));
                        float distance = (center - preferred).sqrMagnitude +
                            pitch * pitch * (width * width + height * height - 2) / 12f;
                        var candidate = new FitScore(count, pitch, distance);
                        if (!candidate.Beats(score, entireFloor)) continue;
                        var fit = new BoardPlacement(center, source.FloorHeight, source.AngleDegrees, width, height, pitch);
                        if (!survey.ChecksEveryTile(fit)) continue;
                        Best = fit;
                        Owner = survey;
                        score = candidate;
                    }
                }
            }

            internal void Examine(FloorSurvey survey)
            {
                // An integral image of the current evidence makes each padded clearance query O(1).
                int stride = survey.Width + 1;
                var blocked = new int[stride * (survey.Height + 1)];
                var low = Vector2.positiveInfinity;
                var high = Vector2.negativeInfinity;
                for (int z = 0; z < survey.Height; z++)
                {
                    int rowBlocked = 0;
                    for (int x = 0; x < survey.Width; x++)
                    {
                        if (survey.Cells[z * survey.Width + x] != TileClearance.Clear) rowBlocked++;
                        else
                        {
                            low = Vector2.Min(low, survey.Corner(x, z));
                            high = Vector2.Max(high, survey.Corner(x + 1, z + 1));
                        }
                        blocked[(z + 1) * stride + x + 1] = rowBlocked + blocked[z * stride + x + 1];
                    }
                }
                if (float.IsPositiveInfinity(low.x)) return;

                var localHead = survey.Local(preferred);
                foreach (float pitch in ConnectedPitches)
                {
                    if (pitch < PreferredMinPitch && score.HasFourPreferred) break;
                    float half = pitch * .5f + FootMargin;
                    var firstCenter = low + Vector2.one * half;
                    var span = high - Vector2.one * half - firstCenter;
                    if (span.x < 0 || span.y < 0) continue;
                    int cols = 1 + Mathf.FloorToInt((span.x + FloorGeometry.Epsilon) / pitch);
                    int rows = 1 + Mathf.FloorToInt((span.y + FloorGeometry.Epsilon) / pitch);
                    if (cols * rows < 2) continue;
                    var available = new byte[cols * rows];
                    var component = new int[available.Length];
                    var nearby = new int[DefaultPaneLimit];
                    var frontier = new int[DefaultPaneLimit * 4];

                    // Try phases aligned with the wearer and SDK open-floor suggestions as well as
                    // uniformly spaced phases. Every admitted pane must pass the same clearance tests.
                    TryPhase(AlignedOffset(localHead));
                    if (suggestions != null)
                        foreach (var point in suggestions) TryPhase(AlignedOffset(survey.Local(point)));
                    int phases = Mathf.CeilToInt(pitch / .05f);
                    for (int z = 0; z < phases; z++)
                        for (int x = 0; x < phases; x++)
                            TryPhase(new Vector2(x, z) * (pitch / phases));

                    Vector2 AlignedOffset(Vector2 point) => new Vector2(
                        Mathf.Repeat(point.x - firstCenter.x, pitch), Mathf.Repeat(point.y - firstCenter.y, pitch));

                    void TryPhase(Vector2 offset)
                    {
                        cancel.ThrowIfCancellationRequested();
                        Vector2 first = firstCenter + offset;
                        Vector2 headCell = (localHead - first) / pitch;
                        for (int z = 0; z < rows; z++)
                            for (int x = 0; x < cols; x++)
                                available[z * cols + x] = ClearBox(first + new Vector2(x, z) * pitch, half) ? (byte)1 : (byte)0;
                        for (int seed = 0; seed < available.Length; seed++)
                        {
                            if (available[seed] != 1) continue;
                            int count = Flood(available, cols, rows, seed, component, headCell, out int nearest);
                            if (count < 2) continue;
                            var chosen = component;
                            if (!entireFloor && count > DefaultPaneLimit)
                            {
                                TakeNearest(available, cols, rows, nearest, headCell, nearby, frontier);
                                chosen = nearby;
                                count = nearby.Length;
                            }

                            var lower = new Vector2Int(cols, rows);
                            var upper = Vector2Int.zero;
                            float sumDistance = 0;
                            for (int i = 0; i < count; i++)
                            {
                                var cell = new Vector2Int(chosen[i] % cols, chosen[i] / cols);
                                lower = Vector2Int.Min(lower, cell);
                                upper = Vector2Int.Max(upper, cell);
                                sumDistance += (headCell - (Vector2)cell).sqrMagnitude;
                            }
                            var candidate = new FitScore(count, pitch, sumDistance * pitch * pitch / count);
                            if (!candidate.Beats(score, entireFloor)) continue;
                            int width = upper.x - lower.x + 1;
                            var indices = new int[count];
                            for (int i = 0; i < count; i++)
                                indices[i] = (chosen[i] / cols - lower.y) * width + chosen[i] % cols - lower.x;
                            var layout = new BoardLayout(width, upper.y - lower.y + 1, indices);
                            var middle = first + (Vector2)(lower + upper) * (.5f * pitch);
                            var fit = new BoardPlacement(survey.World(middle), survey.Floor.Height, survey.Angle, layout, pitch);
                            if (!survey.ChecksEveryTile(fit)) continue;
                            Best = fit;
                            Owner = survey;
                            score = candidate;
                        }
                    }
                }

                bool ClearBox(Vector2 center, float half)
                {
                    const float e = FloorGeometry.Epsilon;
                    int x0 = Mathf.FloorToInt((center.x - half - survey.min.x + e) / survey.Step.x);
                    int x1 = Mathf.CeilToInt((center.x + half - survey.min.x - e) / survey.Step.x);
                    int z0 = Mathf.FloorToInt((center.y - half - survey.min.y + e) / survey.Step.y);
                    int z1 = Mathf.CeilToInt((center.y + half - survey.min.y - e) / survey.Step.y);
                    if (x0 < 0 || z0 < 0 || x1 > survey.Width || z1 > survey.Height) return false;
                    return blocked[z1 * stride + x1] - blocked[z0 * stride + x1]
                         - blocked[z1 * stride + x0] + blocked[z0 * stride + x0] == 0;
                }
            }
        }

        // States 1 and 2 identify unvisited clear panes and complete, edge-connected components.
        private static int Flood(byte[] available, int cols, int rows, int seed, int[] queue, Vector2 head, out int nearest)
        {
            queue[0] = nearest = seed;
            available[seed] = 2;
            int count = 1;
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                int index = queue[i];
                var cell = new Vector2Int(index % cols, index / cols);
                float distance = ((Vector2)cell - head).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; nearest = index; }
                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if ((uint)next.x >= (uint)cols || (uint)next.y >= (uint)rows) continue;
                    int n = next.y * cols + next.x;
                    if (available[n] != 1) continue;
                    queue[count++] = n;
                    available[n] = 2;
                }
            }
            return count;
        }

        // Grow from the wearer's closest pane. Selecting only from the current frontier keeps
        // a capped board connected around a missing corner or a bend in a narrow aisle.
        private static void TakeNearest(byte[] available, int cols, int rows, int seed, Vector2 head,
            int[] chosen, int[] frontier)
        {
            frontier[0] = seed;
            available[seed] = 3;
            int candidates = 1;
            for (int i = 0; i < chosen.Length; i++)
            {
                int best = 0;
                float bestDistance = float.PositiveInfinity;
                for (int j = 0; j < candidates; j++)
                {
                    int n = frontier[j];
                    float distance = (new Vector2(n % cols, n / cols) - head).sqrMagnitude;
                    if (distance < bestDistance) { bestDistance = distance; best = j; }
                }
                int index = frontier[best];
                chosen[i] = index;
                frontier[best] = frontier[--candidates];
                var cell = new Vector2Int(index % cols, index / cols);
                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if ((uint)next.x >= (uint)cols || (uint)next.y >= (uint)rows) continue;
                    int n = next.y * cols + next.x;
                    if (available[n] != 2) continue;
                    frontier[candidates++] = n;
                    available[n] = 3;
                }
            }
        }
    }
}
