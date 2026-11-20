using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FloorFracture.Rooms.Tests")]

namespace FloorFracture
{
    internal static class MrukFloorReader
    {
        private const MRUKAnchor.SceneLabels NonObstacles = MRUKAnchor.SceneLabels.FLOOR |
            MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.GLOBAL_MESH;

        internal static MRUKRoom FindContainingRoom(IReadOnlyList<MRUKRoom> rooms, MRUKRoom hint,
            Vector3 eye, float trackingFloorY, bool editorFixture)
        {
            if (hint && (hint.IsLocal || editorFixture))
            {
                bool inside = hint.FloorAnchors.Count <= 1
                    ? hint.IsPositionInRoom(eye, false)
                    : ContainsFloorPoint(hint, eye, trackingFloorY);
                if (inside) return hint;
            }
            // SDK 209 can test only one floor anchor when finding the current room.
            // Recover a missing or stale hint only when exactly one local multi-floor room contains the eye.
            MRUKRoom found = null;
            foreach (var candidate in rooms)
            {
                if (!candidate || candidate == hint || (!candidate.IsLocal && !editorFixture) ||
                    candidate.FloorAnchors.Count < 2 || !ContainsFloorPoint(candidate, eye, trackingFloorY)) continue;
                if (found) return null;
                found = candidate;
            }
            return found;
        }

        private static bool ContainsFloorPoint(MRUKRoom room, Vector3 eye, float trackingFloorY)
        {
            foreach (var anchor in room.FloorAnchors)
            {
                if (!anchor || anchor.PlaneBoundary2D == null || anchor.PlaneBoundary2D.Count < 3) continue;
                var plane = anchor.transform;
                var normal = plane.forward;
                if (Mathf.Abs(normal.y) < .999f) continue;
                var offset = eye - plane.position;
                float y = plane.position.y - (normal.x * offset.x + normal.z * offset.z) / normal.y;
                if (Mathf.Abs(y - trackingFloorY) > .25f) continue;
                Vector2 point = plane.InverseTransformPoint(new Vector3(eye.x, y, eye.z));
                if (anchor.IsPositionInBoundary(point)) return true;
                // Include exact perimeter contact when both native point-in-polygon tests
                // happen to classify their shared boundary as outside.
                var edge = anchor.PlaneBoundary2D;
                for (int i = 0, j = edge.Count - 1; i < edge.Count; j = i++)
                    if ((point - FloorGeometry.Closest(edge[j],edge[i],point)).sqrMagnitude <=
                        FloorGeometry.Epsilon * FloorGeometry.Epsilon) return true;
            }
            return false;
        }

        public static bool TryRead(MRUKRoom room, float trackingFloorY, float eyeHeight, out List<RoomFloor> floors)
        {
            floors = new List<RoomFloor>();
            if (!room || room.FloorAnchors == null || room.Anchors == null) return false;
            foreach (var anchor in room.FloorAnchors)
            {
                if (!anchor || anchor.PlaneBoundary2D == null || anchor.PlaneBoundary2D.Count < 3) continue;
                if (Mathf.Abs(anchor.transform.forward.y) < 0.999f) continue;
                var floorVertices = new List<Vector2>();
                float low = float.PositiveInfinity, high = float.NegativeInfinity;
                foreach (var local in anchor.PlaneBoundary2D)
                {
                    var world = anchor.transform.TransformPoint(new Vector3(local.x, local.y, 0f));
                    var xz = new Vector2(world.x, world.z);
                    if (!FloorGeometry.Finite(xz) || !FloorGeometry.Finite(world.y)) return false;
                    low = Mathf.Min(low, world.y);
                    high = Mathf.Max(high, world.y);
                    if (floorVertices.Count == 0 || (xz - floorVertices[floorVertices.Count - 1]).sqrMagnitude > 1e-10f)
                        floorVertices.Add(xz);
                }
                if (floorVertices.Count > 1 && (floorVertices[0] - floorVertices[floorVertices.Count - 1]).sqrMagnitude <= 1e-10f)
                    floorVertices.RemoveAt(floorVertices.Count - 1);
                float floorY = (low + high) * 0.5f;
                // A single horizontal board cannot span a slope or a different storey.
                if (high - low > 0.06f || Mathf.Abs(floorY - trackingFloorY) > 0.25f) continue;

                var obstacles = new List<Vector2[]>();
                foreach (var obj in room.Anchors)
                {
                    if (!obj) return false;
                    if (obj.HasAnyLabel(NonObstacles)) continue;
                    if (!TryProjectObstacle(obj, floorY, Mathf.Max(eyeHeight + 0.3f, 2.2f), out var footprint)) return false;
                    if (footprint != null) obstacles.Add(footprint);
                }
                var floor = new RoomFloor(floorVertices.ToArray(), obstacles, low, high);
                if (BoardFitter.IsUsable(floor)) floors.Add(floor);
            }
            if (floors.Count > 1) floors = FloorPatchUnion.Join(floors);
            return floors.Count > 0;
        }

        private static bool TryProjectObstacle(MRUKAnchor anchor, float floorY, float headRoom,
            out Vector2[] outline)
        {
            outline = null;
            var points = new List<Vector2>();
            float bottom = float.PositiveInfinity, top = float.NegativeInfinity;
            if (anchor.VolumeBounds.HasValue)
            {
                var bounds = anchor.VolumeBounds.Value;
                for (int x = 0; x < 2; x++)
                    for (int y = 0; y < 2; y++)
                        for (int z = 0; z < 2; z++)
                            Project(new Vector3(x == 0 ? bounds.min.x : bounds.max.x,
                                y == 0 ? bounds.min.y : bounds.max.y, z == 0 ? bounds.min.z : bounds.max.z));
            }
            if (anchor.PlaneBoundary2D != null && anchor.PlaneBoundary2D.Count >= 3)
            {
                foreach (var p in anchor.PlaneBoundary2D) Project(new Vector3(p.x, p.y, 0f));
            }
            else if (anchor.PlaneRect.HasValue)
            {
                var r = anchor.PlaneRect.Value;
                Project(new Vector3(r.xMin, r.yMin, 0f));
                Project(new Vector3(r.xMax, r.yMin, 0f));
                Project(new Vector3(r.xMax, r.yMax, 0f));
                Project(new Vector3(r.xMin, r.yMax, 0f));
            }
            if (points.Count == 0 || !FloorGeometry.Finite(top) || !FloorGeometry.Finite(bottom)) return false;
            foreach (var p in points) if (!FloorGeometry.Finite(p)) return false;
            if (top >= floorY - 0.05f && bottom <= floorY + headRoom) outline = FloorGeometry.ConvexHull(points);
            return true;

            void Project(Vector3 local)
            {
                var world = anchor.transform.TransformPoint(local);
                points.Add(new Vector2(world.x, world.z));
                top = Mathf.Max(top, world.y);
                bottom = Mathf.Min(bottom, world.y);
            }
        }
    }
}
