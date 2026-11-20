using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Unity.Collections;
using Unity.U2D.Physics;
using UnityEngine;
using UnityEngine.Rendering;

namespace FloorFracture
{
    // Leave each native anchor and source mesh intact. Only the drawing mesh of an
    // overlapping patch loses the area already painted by a coplanar patch.
    public sealed class FloorPatchCoverage : IDisposable
    {
        private sealed class Patch
        {
            internal Vector2[] outline;
            internal Vector2 min, max;
            internal float low, high;
        }

        private sealed class Level
        {
            internal float low, high;
            internal readonly List<Patch> patches = new();
        }

        private readonly List<Level> levels = new();
        private readonly List<(MeshFilter filter, Mesh source, Mesh visible)> changed = new();

        public void Add(MRUKAnchor anchor, MeshFilter filter)
        {
            if (!filter || !filter.sharedMesh || !anchor || anchor.PlaneBoundary2D == null ||
                anchor.PlaneBoundary2D.Count < 3 || Mathf.Abs(anchor.transform.forward.y) < .999f) return;
            var patch = new Patch { outline = new Vector2[anchor.PlaneBoundary2D.Count],
                low = float.PositiveInfinity, high = float.NegativeInfinity };
            for (int i = 0; i < patch.outline.Length; ++i)
            {
                var p = anchor.transform.TransformPoint(anchor.PlaneBoundary2D[i]);
                patch.outline[i] = new Vector2(p.x,p.z);
                patch.low = Mathf.Min(patch.low,p.y);
                patch.high = Mathf.Max(patch.high,p.y);
            }
            if (!FloorGeometry.Finite(patch.low) || !FloorGeometry.Finite(patch.high) ||
                patch.high - patch.low > FloorPatchUnion.CoplanarRange || !FloorGeometry.SimpleFloor(patch.outline)) return;
            FloorGeometry.Bounds(patch.outline, out patch.min, out patch.max);
            var level = levels.Find(entry =>
                Mathf.Max(entry.high,patch.high) - Mathf.Min(entry.low,patch.low) <= FloorPatchUnion.CoplanarRange);
            if (level == null)
            {
                level = new Level { low = patch.low, high = patch.high };
                levels.Add(level);
            }
            level.low = Mathf.Min(level.low,patch.low);
            level.high = Mathf.Max(level.high,patch.high);
            var coveredBy = level.patches.FindAll(other =>
                Mathf.Min(other.max.x,patch.max.x) - Mathf.Max(other.min.x,patch.min.x) > FloorGeometry.Epsilon &&
                Mathf.Min(other.max.y,patch.max.y) - Mathf.Max(other.min.y,patch.min.y) > FloorGeometry.Epsilon);
            if (coveredBy.Count > 0)
            {
                var native = filter.sharedMesh;
                var visible = Subtract(patch, coveredBy, anchor.transform, filter.transform, native);
                if (visible)
                {
                    changed.Add((filter,native,visible));
                    filter.sharedMesh = visible;
                }
            }
            level.patches.Add(patch);
        }

        private static Mesh Subtract(Patch patch, List<Patch> occupied, Transform anchor, Transform draw, Mesh source)
        {
            using var composer = PhysicsComposer.Create(Allocator.Temp);
            var origin = patch.outline[0];
            FloorPatchUnion.AddLayer(composer, patch.outline, origin, PhysicsComposer.Operation.OR, 0);
            for (int i = 0; i < occupied.Count; ++i)
                FloorPatchUnion.AddLayer(composer, occupied[i].outline, origin, PhysicsComposer.Operation.NOT, i + 1);
            using var polygons = composer.CreatePolygonGeometry(Allocator.Temp);
            if (composer.rejectedGeometryCount != 0) return null;
            var vertices = new List<Vector3>();
            var indices = new List<int>();
            var normal = anchor.forward;
            double visibleArea = 0;
            foreach (var polygon in polygons)
            {
                int first = vertices.Count;
                var outline = new Vector2[polygon.count];
                for (int i = 0; i < polygon.count; ++i)
                {
                    var p = origin + polygon.vertices[i] / FloorPatchUnion.MillimetresPerMetre;
                    outline[i] = p;
                    var offset = p - new Vector2(anchor.position.x,anchor.position.z);
                    float y = anchor.position.y - (normal.x * offset.x + normal.z * offset.y) / normal.y;
                    vertices.Add(draw.InverseTransformPoint(new Vector3(p.x,y,p.y)));
                }
                visibleArea += Math.Abs(FloorPatchUnion.SignedArea(outline));
                // The composer returns convex pieces, including around any enclosed cutout.
                for (int i = 2; i < polygon.count; ++i)
                {
                    indices.Add(first);
                    indices.Add(first+i-1);
                    indices.Add(first+i);
                }
            }
            if (Math.Abs(FloorPatchUnion.SignedArea(patch.outline)) - visibleArea <= 1e-7) return null;
            var mesh = new Mesh { name = source.name + " visible floor", indexFormat =
                vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices,0,false);
            mesh.bounds = source.bounds;
            return mesh;
        }

        public void Dispose()
        {
            foreach (var item in changed)
            {
                if (item.filter && item.filter.sharedMesh == item.visible) item.filter.sharedMesh = item.source;
                if (Application.isPlaying) UnityEngine.Object.Destroy(item.visible);
                else UnityEngine.Object.DestroyImmediate(item.visible);
            }
            changed.Clear();
            levels.Clear();
        }
    }
}
