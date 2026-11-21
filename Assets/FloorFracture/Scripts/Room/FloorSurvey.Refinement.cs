using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace FloorFracture
{
    internal sealed partial class FloorSurvey
    {
        // Keep the coarse, verified result in contention as we examine finer headings. Aisles
        // between furniture may be diagonal to the longest wall. Full-floor mode also compares
        // these headings when the coarse result already fits a normal board.
        internal static (BoardPlacement? fit, List<FloorSurvey> surveys) Refine(
            List<FloorSurvey> initial, FloorMeshScan scan, float headroom, IReadOnlyList<Vector2[]> excluded,
            Vector2 preferred, IReadOnlyList<Vector2> suggestions, CancellationToken cancel, bool entireFloor = false)
        {
            var displayed = new List<FloorSurvey>(initial);
            var search = new ConnectedSearch(preferred, entireFloor, suggestions, cancel);
            foreach (var survey in initial) search.Examine(survey);
            bool filledDefault = search.Best is BoardPlacement coarse && coarse.Layout.PaneCount == DefaultPaneLimit &&
                coarse.Pitch >= PreferredMinPitch;
            if (scan != null && (entireFloor || !filledDefault))
            {
                for (int i = 0; i < initial.Count; i++)
                {
                    var original = initial[i];
                    float bestWidth = original.AddClearRectangles(null);
                    foreach (float angle in original.RefinementHeadings())
                    {
                        cancel.ThrowIfCancellationRequested();
                        var survey = new FloorSurvey(original.Floor, scan, headroom, excluded, cancel,
                            original.liveObstacles, original.liveSurface, angle);
                        search.Examine(survey);
                        float width = survey.AddClearRectangles(null);
                        if (width > bestWidth)
                        {
                            bestWidth = width;
                            displayed[i] = survey;
                        }
                    }
                }
            }
            if (!search.HasFourPreferred && scan != null)
            {
                // Recover continuous rectangle fits that fall between trial grid phases or
                // headings, including the original fitter's exact furniture contacts.
                for (int i = 0; i < initial.Count; i++)
                {
                    var original = initial[i];
                    var floor = original.Floor;
                    var obstacles = new List<Vector2[]>(floor.Obstacles);
                    if (excluded != null) obstacles.AddRange(excluded);
                    var semantic = new RoomFloor(floor.Boundary, obstacles, floor.Height);
                    if (BoardFitter.TryFindOnSurfaces(new[] { semantic }, preferred, out var exact,
                        cancel, suggestions, FootMargin))
                    {
                        search.ExamineRectangle(original, exact);
                        if (displayed[i] != original) search.ExamineRectangle(displayed[i], exact);
                    }
                    var rectangles = new List<RoomFloor>();
                    displayed[i].AddClearRectangles(rectangles);
                    if (BoardFitter.TryFindOnSurfaces(rectangles, preferred, out var measured,
                        cancel, suggestions, FootMargin, true))
                        search.ExamineRectangle(displayed[i], measured);
                }
            }
            if (search.Owner != null)
                displayed[initial.FindIndex(original => ReferenceEquals(original.Floor, search.Owner.Floor))] = search.Owner;
            return (search.Best, displayed);
        }

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
