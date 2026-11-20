using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    internal sealed partial class FloorSurvey
    {
        // Try ordinary stepping distances as well as smaller panes when the rectangular fit fails.
        private static readonly float[] ConnectedPitches = {
            BoardState.MaxPitch, .60f, .55f, BoardState.MinPitch, .45f, BoardState.CompactMinPitch
        };

        internal static BoardPlacement? FindConnected(IEnumerable<FloorSurvey> evidence, Vector2 preferred,
            CancellationToken cancel, out FloorSurvey owner)
        {
            BoardPlacement? best = null;
            int bestArea = 0;
            float bestDistance = float.PositiveInfinity;
            owner = null;
            foreach (var survey in evidence)
            {
                if (survey.SearchConnected(preferred, cancel, ref best, ref bestArea, ref bestDistance)) owner = survey;
            }
            return best;
        }

        private bool SearchConnected(Vector2 preferred, CancellationToken cancel, ref BoardPlacement? best,
            ref int bestArea, ref float bestDistance)
        {
            // A summed-area table counts blocked survey cells in any padded, axis-aligned tile.
            // Build once for this pass so every trial offset sees the latest live-depth vetoes.
            int stride = Width + 1;
            var blocked = new int[stride * (Height + 1)];
            var low = Vector2.positiveInfinity;
            var high = Vector2.negativeInfinity;
            var localHead = Local(preferred);
            Vector2 nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int z = 0; z < Height; z++)
            {
                int rowBlocked = 0;
                for (int x = 0; x < Width; x++)
                {
                    if (Cells[z * Width + x] != TileClearance.Clear) rowBlocked++;
                    else
                    {
                        var corner = Corner(x, z);
                        low = Vector2.Min(low, corner);
                        high = Vector2.Max(high, Corner(x + 1, z + 1));
                        var center = corner + Step * .5f;
                        float distance = (center - localHead).sqrMagnitude;
                        if (distance < nearestDistance) { nearestDistance = distance; nearest = center; }
                    }
                    blocked[(z + 1) * stride + x + 1] = rowBlocked + blocked[z * stride + x + 1];
                }
            }
            if (float.IsPositiveInfinity(nearestDistance)) return false;

            // A board has at most 64 slots. Bound the work near the player even on a multi-room scan.
            var reach = Vector2.one * (BoardState.MaxPanes * BoardState.MaxPitch);
            low = Vector2.Max(low, nearest - reach);
            high = Vector2.Min(high, nearest + reach);
            var queue = new Vector2Int[BoardState.MaxPanes];
            bool improved = false;

            foreach (float pitch in ConnectedPitches)
            {
                float half = pitch * .5f + FootMargin;
                var firstCenter = low + Vector2.one * half;
                var lastCenter = high - Vector2.one * half;
                if (firstCenter.x > lastCenter.x || firstCenter.y > lastCenter.y) continue;
                int cols = 1 + Mathf.FloorToInt((lastCenter.x - firstCenter.x + FloorGeometry.Epsilon) / pitch);
                int rows = 1 + Mathf.FloorToInt((lastCenter.y - firstCenter.y + FloorGeometry.Epsilon) / pitch);
                if (cols * rows < 2) continue;
                var available = new bool[cols * rows];
                int phases = Mathf.CeilToInt(pitch / .05f);
                for (int pz = 0; pz < phases; pz++)
                    for (int px = 0; px < phases; px++)
                    {
                        cancel.ThrowIfCancellationRequested();
                        var first = firstCenter + new Vector2(px, pz) * (pitch / phases);
                        for (int z = 0; z < rows; z++)
                            for (int x = 0; x < cols; x++)
                            {
                                var center = first + new Vector2(x, z) * pitch;
                                available[z * cols + x] = ClearBox(center, half);
                            }

                        for (int seed = 0; seed < available.Length; seed++)
                        {
                            if (!available[seed]) continue;
                            int count = TakeComponent(available, cols, rows, seed, queue, out var lower, out var upper);
                            if (count < 2) continue;
                            int area = Mathf.RoundToInt(count * pitch * pitch * 10000);
                            if (area < bestArea || (area == bestArea && pitch < best.Value.Pitch)) continue;
                            int width = upper.x - lower.x + 1, height = upper.y - lower.y + 1;
                            ulong mask = 0;
                            float distance = float.PositiveInfinity;
                            for (int i = 0; i < count; i++)
                            {
                                var cell = queue[i];
                                mask |= 1UL << ((cell.y - lower.y) * width + cell.x - lower.x);
                                distance = Mathf.Min(distance, (first + (Vector2)cell * pitch - localHead).sqrMagnitude);
                            }
                            if (area == bestArea && pitch == best.Value.Pitch && distance >= bestDistance) continue;
                            var middle = first + (Vector2)(lower + upper) * (.5f * pitch);
                            var fit = new BoardPlacement(World(middle), Floor.Height, Angle, new BoardLayout(width, height, mask), pitch);
                            if (!ChecksEveryTile(fit)) continue;
                            best = fit;
                            bestArea = area;
                            bestDistance = distance;
                            improved = true;
                        }
                    }
            }
            return improved;

            bool ClearBox(Vector2 center, float half)
            {
                const float e = FloorGeometry.Epsilon;
                int x0 = Mathf.FloorToInt((center.x - half - min.x + e) / Step.x);
                int x1 = Mathf.CeilToInt((center.x + half - min.x - e) / Step.x);
                int z0 = Mathf.FloorToInt((center.y - half - min.y + e) / Step.y);
                int z1 = Mathf.CeilToInt((center.y + half - min.y - e) / Step.y);
                if (x0 < 0 || z0 < 0 || x1 > Width || z1 > Height) return false;
                return blocked[z1 * stride + x1] - blocked[z0 * stride + x1]
                     - blocked[z1 * stride + x0] + blocked[z0 * stride + x0] == 0;
            }
        }

        private static int TakeComponent(bool[] available, int cols, int rows, int seed, Vector2Int[] queue,
            out Vector2Int lower, out Vector2Int upper)
        {
            var start = new Vector2Int(seed % cols, seed / cols);
            var low = start;
            var high = start;
            available[seed] = false;
            queue[0] = start;
            int count = 1;
            for (int i = 0; i < count; i++)
            {
                var p = queue[i];
                Visit(p + Vector2Int.right);
                Visit(p + Vector2Int.up);
                Visit(p + Vector2Int.left);
                Visit(p + Vector2Int.down);
            }
            lower = low;
            upper = high;
            return count;

            void Visit(Vector2Int p)
            {
                if ((uint)p.x >= (uint)cols || (uint)p.y >= (uint)rows || !available[p.y * cols + p.x]) return;
                var nextLow = Vector2Int.Min(low, p);
                var nextHigh = Vector2Int.Max(high, p);
                if ((nextHigh.x - nextLow.x + 1) * (nextHigh.y - nextLow.y + 1) > BoardState.MaxPanes) return;
                low = nextLow;
                high = nextHigh;
                available[p.y * cols + p.x] = false;
                queue[count++] = p;
            }
        }
    }
}
