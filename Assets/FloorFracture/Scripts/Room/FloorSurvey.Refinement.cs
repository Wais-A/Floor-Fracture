using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    internal sealed partial class FloorSurvey
    {
        // An open aisle can be diagonal to the longest wall. Refine the measured surface
        // only after ordinary placement fails, then use the same fitter and padded tile proof.
        internal static (BoardPlacement? fit, List<FloorSurvey> surveys) Refine(
            List<FloorSurvey> initial, FloorMeshScan scan, float headroom, IReadOnlyList<Vector2[]> excluded,
            Vector2 preferred, IReadOnlyList<Vector2> suggestions, CancellationToken cancel)
        {
            var displayed = new List<FloorSurvey>(initial);
            var rectangles = new List<RoomFloor>();
            var candidates = new List<FloorSurvey>();
            var orientations = new List<FloorSurvey>(initial);
            for (int i = 0; i < initial.Count; i++)
            {
                var original = initial[i];
                if (scan == null) continue;
                float bestWidth = original.AddClearRectangles(null);
                foreach (float angle in original.RefinementHeadings())
                {
                    cancel.ThrowIfCancellationRequested();
                    var survey = new FloorSurvey(original.Floor, scan, headroom, excluded, cancel, original.liveObstacles, original.liveSurface, angle);
                    orientations.Add(survey);
                    int before = rectangles.Count;
                    float width = survey.AddClearRectangles(rectangles);
                    if (width > bestWidth)
                    {
                        bestWidth = width;
                        displayed[i] = survey;
                    }
                    if (rectangles.Count > before) candidates.Add(survey);
                }
            }

            BoardPlacement? best = null;
            FloorSurvey owner = null;
            if (BoardFitter.TryFindOnSurfaces(rectangles, preferred, out var fit, cancel, suggestions, FootMargin, true))
            {
                owner = candidates.Find(s => s.ChecksEveryTile(fit));
                if (owner != null) best = fit;
            }
            var connected = FindConnected(orientations, preferred, cancel, out var measured);
            if (CoversMore(connected, best))
            {
                best = connected;
                owner = measured;
            }
            if (owner != null)
                displayed[initial.FindIndex(original => ReferenceEquals(original.Floor, owner.Floor))] = owner;
            return (best, displayed);
        }

        internal static bool CoversMore(BoardPlacement? candidate, BoardPlacement? other) =>
            candidate is BoardPlacement a && (!(other is BoardPlacement b) ||
                a.Layout.PaneCount * a.Pitch * a.Pitch > b.Layout.PaneCount * b.Pitch * b.Pitch + .0001f);

        private List<float> RefinementHeadings()
        {
            var angles = new List<float>(7);
            for (int degrees = 0; degrees < 90; degrees += 15) angles.Add(Angle + degrees);

            // Include the direction of a long, narrow aisle between obstructions, which
            // can fall between the standard headings. Only positively observed cells vote.
            double x = 0, z = 0, xx = 0, zz = 0, xz = 0;
            int count = 0;
            for (int i = 0; i < Cells.Length; i++)
            {
                if (Cells[i] != TileClearance.Clear) continue;
                double u = (i % Width) * Step.x;
                double v = (i / Width) * Step.y;
                count++;
                x += u; z += v;
                xx += u * u; zz += v * v; xz += u * v;
            }
            if (count < 3) return angles;
            double difference = xx - x * x / count - (zz - z * z / count);
            double cross = 2 * (xz - x * z / count);
            float heading = (float)(.5 * Math.Atan2(cross, difference) * Mathf.Rad2Deg);
            if (Mathf.Abs(Mathf.DeltaAngle(heading, Mathf.Round(heading / 15) * 15)) > 1)
                angles.Add(Angle + heading);
            return angles;
        }
    }
}
