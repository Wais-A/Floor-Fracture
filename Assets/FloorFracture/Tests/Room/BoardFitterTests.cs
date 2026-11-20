using System.Collections.Generic;
using System.Linq;
using Meta.XR.MRUtilityKit;
using NUnit.Framework;
using UnityEngine;

namespace FloorFracture.Tests
{
    public class BoardFitterTests
    {
        [Test]
        public void FitsTheWholeSurfaceAcrossTwoCoplanarFloorAnchors()
        {
            var patches = new[] { Floor(Rectangle(-2,-1,0,1)), Floor(Rectangle(0,-1,2,1)) };
            Assert.That(BoardFitter.TryFind(patches, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(8f).Within(.001f));
            Assert.That(fit.Pitch, Is.EqualTo(2f/3f).Within(.0001f));
        }

        [TestCase(0f)]
        [TestCase(-.25f)]
        public void UsesAPartialSeamOrOverlapWithoutFillingTheCorners(float rightStarts)
        {
            var right = Rectangle(rightStarts,-.75f,2,.75f);
            System.Array.Reverse(right);
            var floor = Floor(new[] { P(-2,-1), P(0,-1), P(0,-.75f), P(2,-.75f),
                P(2,.75f), P(0,.75f), P(0,1), P(-2,1) });
            var patches = new[] { Floor(Rectangle(-2,-1,0,1)), Floor(right) };
            Assert.That(BoardFitter.TryFind(patches, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.GreaterThanOrEqualTo(5.999f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [TestCase(.001f,0f)]
        [TestCase(.0002f,0f)]
        [TestCase(0f,.005f)]
        [TestCase(0f,.18f)]
        public void KeepsSeparatePatchesAcrossAGapOrStep(float gap, float step)
        {
            var left = Floor(Rectangle(-2,-1,0,1));
            var right = new RoomFloor(Rectangle(gap,-1,2+gap,1), new List<Vector2[]>(), step);
            Assert.That(BoardFitter.TryFind(new[] { left, right }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(4f).Within(.001f));
            Assert.That(BoardFitter.Fits(left,fit) || BoardFitter.Fits(right,fit), Is.True);
        }

        [Test]
        public void JoinsARotatedConcaveAnchorWithoutLosingMetricSize()
        {
            var pose = new BoardPlacement(P(2.3f,-3.2f), 0, 24.36f, 6, 3);
            var lShape = new[] { P(-2,-1), P(2,-1), P(2,0), P(0,0), P(0,1), P(-2,1) };
            RoomFloor Rotated(Vector2[] points) => Floor(points.Select(p => pose.ToWorldXZ(p.x,p.y)).ToArray());
            var patches = new[] { Rotated(lShape), Rotated(Rectangle(0,0,2,1)) };
            Assert.That(BoardFitter.TryFind(patches, pose.Center, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(8f).Within(.002f));
            Assert.That(Vector2.Distance(fit.Center, pose.Center), Is.LessThan(.001f));
        }

        [Test]
        public void ACornerContactCannotFillTheSpaceBetweenDiagonalAnchors()
        {
            var lower = Floor(Rectangle(-2,-2,0,0));
            var upper = Floor(Rectangle(0,0,2,2));
            var regions = FloorPatchUnion.Join(new[] { lower, upper });
            Assert.That(regions.Exists(f => BoardFitter.Fits(f, MinimumBoard())), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { lower, upper }, Vector2.zero, out var fit), Is.True);
            Assert.That(BoardFitter.Fits(lower,fit) || BoardFitter.Fits(upper,fit), Is.True);
        }

        [Test]
        public void PreservesHolesAndIslandsAndRechecksObstaclesFromEitherSide()
        {
            var hole = Rectangle(-1.5f,-1.5f,1.5f,1.5f);
            var obstacle = Rectangle(1.8f,-.3f,2.1f,.3f);
            var patches = new[] {
                Floor(Rectangle(-3,-3,3,-1.5f)), Floor(Rectangle(-3,-1.5f,-1.5f,1.5f)),
                Floor(Rectangle(1.5f,-1.5f,3,1.5f), obstacle), Floor(Rectangle(-3,1.5f,3,3)),
                Floor(Rectangle(-.5f,-.5f,.5f,.5f)) };
            var regions = FloorPatchUnion.Join(patches);
            Assert.That(regions.Count, Is.EqualTo(2));
            bool Supports(float x, float z) => regions.Exists(f => BoardFitter.Fits(f,
                new BoardPlacement(P(x,z), 0, 0, 2, 2)));
            Assert.That(Supports(0,0), Is.True, "Island");
            Assert.That(Supports(0,.6f), Is.False, "An opening around the island");
            Assert.That(Supports(-2,1.5f), Is.True, "The seam in the surrounding walkway");
            Assert.That(Supports(2,0), Is.False, "Furniture on the opposite anchor");
            Assert.That(BoardFitter.TryFind(patches, Vector2.zero, out var fit), Is.True);
            Assert.That(BoardFitter.Fits(Floor(Rectangle(-3,-3,3,3),hole,obstacle), fit), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void LocalizesFloorWithAbsentOrStaleHintAndRejectsOutsideWrongStoreyAndAmbiguousRooms(bool split)
        {
            var root = new GameObject("MRUK localization fixture");
            root.SetActive(false);
            try
            {
                MRUKRoom RoomAt(float x)
                {
                    var host = new GameObject("Room");
                    host.transform.SetParent(root.transform, false);
                    host.transform.position = Vector3.right * x;
                    var room = host.AddComponent<MRUKRoom>();
                    foreach (float start in split ? new[] { -2f, 0f } : new[] { -2f })
                    {
                        var floor = new GameObject("FLOOR").AddComponent<MRUKAnchor>();
                        floor.transform.SetParent(host.transform,false);
                        floor.transform.localRotation = Quaternion.Euler(-90,0,0);
                        floor.PlaneBoundary2D.AddRange(Rectangle(start,-1,split ? start+2 : 2f,1));
                        room.FloorAnchors.Add(floor);
                    }
                    return room;
                }
                var active = RoomAt(0);
                var other = RoomAt(10);
                var rooms = new[] { other, active };
                MRUKRoom At(float x, MRUKRoom hint = null, float floorY = 0) =>
                    MrukFloorReader.FindContainingRoom(rooms,hint,new Vector3(x,1.6f,0),floorY,true);
                foreach (float x in new[] { -1f, -.001f, 0f, .001f, 1f })
                {
                    Assert.That(At(x), Is.SameAs(active));
                    Assert.That(At(x,other), Is.SameAs(active));
                    Assert.That(At(x,active), Is.SameAs(active));
                }
                Assert.That(At(10,active), Is.SameAs(other));
                Assert.That(At(4,active), Is.Null);
                Assert.That(At(0,active,2), Is.Null);
                Assert.That(At(0,active,float.NaN), Is.Null);
                Assert.That(MrukFloorReader.FindContainingRoom(rooms,active,new Vector3(0,1.6f,0),0,false), Is.Null,
                    "An Editor room without a local device anchor cannot supply runtime tracking.");
                other.transform.position = Vector3.zero;
                Assert.That(At(0), Is.Null, "Two competing floor regions with no current room hint.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void MeasuresTheCalibratedFloorUnderTheEyeInWorldLockedSpace()
        {
            var origin = new Vector3(5, -.8f, -4);
            var rotation = Quaternion.Euler(1.5f, 37, -.75f);
            var floorPoint = origin + rotation * new Vector3(1.6f, 0, 2.1f);
            var eye = floorPoint + Vector3.up * 1.45f;
            Assert.That(MrukFloorReader.TrackingFloorHeight(origin, rotation * Vector3.up, eye),
                Is.EqualTo(floorPoint.y).Within(.0001f));
            Assert.That(float.IsNaN(MrukFloorReader.TrackingFloorHeight(origin, Vector3.right, eye)), Is.True);
            Assert.That(float.IsNaN(MrukFloorReader.TrackingFloorHeight(origin, Vector3.down, eye)), Is.True);
        }

        [Test]
        public void ReadsAndRechecksTwoNativeRoomAnchorsWithoutReplacingTheirMeshes()
        {
            var host = new GameObject("Split MRUK room");
            host.SetActive(false);
            var meshes = new List<Mesh>();
            // EffectMesh triangulates through Meta's shared library. Edit Mode has no
            // running MRUK singleton to initialize it, so hold it for this test only.
            var native = typeof(MRUK).Assembly.GetType("Meta.XR.MRUtilityKit.MRUKNative", true);
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
            bool loadNative = (System.IntPtr)native.GetField("_nativeLibraryPtr", flags).GetValue(null) == System.IntPtr.Zero;
            try
            {
                if (loadNative) native.GetMethod("LoadMRUKSharedLibrary", flags).Invoke(null, null);
                var room = host.AddComponent<MRUKRoom>();
                MRUKAnchor AddFloor(Vector2[] points)
                {
                    var go = new GameObject("FLOOR");
                    go.transform.SetParent(host.transform, false);
                    go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
                    var anchor = go.AddComponent<MRUKAnchor>();
                    typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.Label)).SetValue(anchor, MRUKAnchor.SceneLabels.FLOOR);
                    FloorGeometry.Bounds(points, out var min, out var max);
                    typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.PlaneRect)).SetValue(anchor,
                        Rect.MinMaxRect(min.x,min.y,max.x,max.y));
                    anchor.PlaneBoundary2D.AddRange(points);
                    room.FloorAnchors.Add(anchor);
                    room.Anchors.Add(anchor);
                    return anchor;
                }
                var left = AddFloor(Rectangle(-2,-1,0,1));
                var right = AddFloor(Rectangle(0,-1,2,1));
                var effect = host.AddComponent<EffectMesh>();
                foreach (var anchor in room.FloorAnchors)
                    meshes.Add(effect.CreateEffectMesh(anchor).mesh);
                Assert.That(MrukFloorReader.TryRead(room,0,1.6f,out var regions), Is.True);
                Assert.That(BoardFitter.TryFindOnSurfaces(regions,Vector2.zero,out var fit), Is.True);
                Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(8f).Within(.001f));
                Assert.That(MrukFloorReader.TryRead(room,0,1.6f,out var current), Is.True);
                Assert.That(current.Exists(floor => BoardFitter.Fits(floor, fit)), Is.True);
                Assert.That(room.FloorAnchors, Is.EqualTo(new[] { left, right }));
                Assert.That(right.PlaneBoundary2D.SequenceEqual(Rectangle(0,-1,2,1)), Is.True);
                Assert.That(effect.EffectMeshObjects.Count, Is.EqualTo(2));
                for (int i = 0; i < meshes.Count; ++i)
                {
                    var patch = effect.EffectMeshObjects[room.FloorAnchors[i]];
                    Assert.That(patch.mesh, Is.SameAs(meshes[i]));
                    Assert.That(patch.mesh.triangles.Length, Is.EqualTo(6));
                    Assert.That(patch.effectMeshGO.transform.parent, Is.EqualTo(room.FloorAnchors[i].transform));
                }

                var first = effect.EffectMeshObjects[left].effectMeshGO.GetComponent<MeshFilter>();
                var second = effect.EffectMeshObjects[right].effectMeshGO.GetComponent<MeshFilter>();
                right.transform.localPosition = Vector3.left * .5f;
                Mesh clipped;
                using (var coverage = new FloorPatchCoverage())
                {
                    coverage.Add(left, first);
                    coverage.Add(right, second);
                    clipped = second.sharedMesh;
                    Assert.That(first.sharedMesh, Is.SameAs(meshes[0]));
                    Assert.That(clipped, Is.Not.SameAs(meshes[1]));
                    double area = 0;
                    var v = clipped.vertices;
                    var t = clipped.triangles;
                    for (int i = 0; i < t.Length; i += 3)
                        area += Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).magnitude * .5;
                    Assert.That(area, Is.EqualTo(3).Within(.0001));
                    Assert.That(effect.EffectMeshObjects[right].mesh, Is.SameAs(meshes[1]));
                }
                Assert.That(second.sharedMesh, Is.SameAs(meshes[1]));
                Assert.That(!clipped, Is.True, "The generated drawing mesh was released.");
                right.transform.localPosition += Vector3.up * .04f;
                using (var coverage = new FloorPatchCoverage())
                {
                    coverage.Add(left, first);
                    coverage.Add(right, second);
                    Assert.That(second.sharedMesh, Is.SameAs(meshes[1]), "The other elevation keeps its coverage.");
                }
                Assert.That(MrukFloorReader.TryRead(room,0,1.6f,out current), Is.True);
                Assert.That(current.Exists(floor => BoardFitter.Fits(floor, fit)), Is.False);

                // A malformed later scan still has its old native draw mesh. Its bad
                // boundary must never remove coverage from another valid draw.
                right.transform.localPosition = Vector3.left * .5f;
                foreach (var invalid in new[] { new[] { P(-2,-1), P(0,1), P(-2,1), P(0,-1) },
                    new[] { P(-2,-1), P(0,-1), P(float.NaN,1), P(-2,1) } })
                {
                    left.PlaneBoundary2D.Clear();
                    left.PlaneBoundary2D.AddRange(invalid);
                    using var coverage = new FloorPatchCoverage();
                    coverage.Add(left,first);
                    coverage.Add(right,second);
                    Assert.That(first.sharedMesh, Is.SameAs(meshes[0]));
                    Assert.That(second.sharedMesh, Is.SameAs(meshes[1]));
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                foreach (var mesh in meshes) if (mesh) Object.DestroyImmediate(mesh);
                if (loadNative) native.GetMethod("FreeMRUKSharedLibrary", flags).Invoke(null, null);
            }
        }

        [Test]
        public void FillsTheClearSquareThatPreviouslyProducedTwoByTwoAt57Centimeters()
        {
            const float side = 1.946f;
            var floor = Floor(Rectangle(0f, 0f, side, side));
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(0.1f, 0.1f), out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(3));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(side / 3f).Within(0.0001f));
            Assert.That(fit.Center.x, Is.EqualTo(side / 2f).Within(0.0001f));
            Assert.That(fit.Center.y, Is.EqualTo(side / 2f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void FurnitureLimitsOnlyTheOccupiedSideOfTheSameSquare()
        {
            const float side = 1.946f;
            float openWidth = side * 2f / 3f;
            var floor = Floor(Rectangle(0f, 0f, side, side), Rectangle(openWidth, 0f, side, side));
            var fullSquare = new BoardPlacement(P(side / 2f, side / 2f), 0f, 0f, 3, 3, side / 3f);
            Assert.That(BoardFitter.Fits(floor, fullSquare), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { floor }, fullSquare.Center, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(2));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(side / 3f).Within(0.0001f));
            Assert.That(fit.Center.x + fit.Columns * fit.Pitch / 2f, Is.EqualTo(openWidth).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [TestCase(1.00f, 1.00f, 2, 2, 0.50f, 1.00f, 1.00f)]
        [TestCase(1.80f, 1.80f, 3, 3, 0.60f, 1.80f, 1.80f)]
        [TestCase(1.94f, 1.94f, 3, 3, 1.94f / 3f, 1.94f, 1.94f)]
        [TestCase(2.06f, 2.06f, 4, 4, 0.515f, 2.06f, 2.06f)]
        [TestCase(2.30f, 2.80f, 4, 5, 0.56f, 2.24f, 2.80f)]
        [TestCase(2.80f, 2.30f, 5, 4, 0.56f, 2.80f, 2.24f)]
        [TestCase(3.20f, 3.60f, 6, 7, 3.60f / 7f, 6f * 3.60f / 7f, 3.60f)]
        [TestCase(4.00f, 4.20f, 6, 6, 4f / 6f, 4.00f, 4.00f)]
        [TestCase(5.60f, 5.60f, 8, 8, 0.67f, 5.36f, 5.36f)]
        [TestCase(7.00f, 7.00f, 8, 8, 0.67f, 5.36f, 5.36f)]
        [TestCase(6.40f, 2.00f, 10, 3, 0.64f, 6.40f, 1.92f)]
        [TestCase(1.80f, 6.80f, 3, 11, 0.60f, 1.80f, 6.60f)]
        [TestCase(8.04f, 2.68f, 12, 4, 0.67f, 8.04f, 2.68f)]
        [TestCase(21.44f, 1.34f, 32, 2, 0.67f, 21.44f, 1.34f)]
        [TestCase(1.34f, 21.44f, 2, 32, 0.67f, 1.34f, 21.44f)]
        public void MaximizesTheWholeGlassFootprint(float roomX, float roomZ, int columns, int rows,
            float pitch, float glassX, float glassZ)
        {
            var floor = Floor(Rectangle(-roomX / 2, -roomZ / 2, roomX / 2, roomZ / 2));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(columns));
            Assert.That(fit.Rows, Is.EqualTo(rows));
            Assert.That(fit.Pitch, Is.EqualTo(pitch).Within(0.0001f));
            Assert.That(fit.Columns * fit.Pitch, Is.EqualTo(glassX).Within(0.0001f));
            Assert.That(fit.Rows * fit.Pitch, Is.EqualTo(glassZ).Within(0.0001f));
            var outline = fit.FootprintOutline();
            Assert.That((outline[1] - outline[0]).magnitude, Is.EqualTo(glassX).Within(0.0001f));
            Assert.That((outline[2] - outline[1]).magnitude, Is.EqualTo(glassZ).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void AddsARowWhenClearFloorExtendsAlongTheLongSide()
        {
            // The two earlier fitters chose seven by two and eight by three on this floor.
            var floor = Floor(Rectangle(-2.75f, -1.07f, 2.75f, 1.47f));
            var strip = new BoardPlacement(Vector2.zero, 0f, 0f, 7, 2, 0.67f);
            var wider = new BoardPlacement(P(0f, 0.2f), 0f, 0f, 8, 3, 0.58f);
            Assert.That(BoardFitter.Fits(floor, strip), Is.True);
            Assert.That(BoardFitter.Fits(floor, wider), Is.True);

            Assert.That(BoardFitter.TryFind(new[] { floor }, strip.Center, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(11));
            Assert.That(fit.Rows, Is.EqualTo(5));
            Assert.That(fit.Pitch, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(fit.Center.y, Is.EqualTo(0.18f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void KeepsTwoRowsWhenTheLongSideMeetsAWallOrFurniture(bool furniture)
        {
            var floor = furniture
                ? Floor(Rectangle(-2.68f, -0.67f, 2.68f, 1.07f),
                    Rectangle(-2.68f, 0.67f, 2.68f, 1.07f))
                : Floor(Rectangle(-2.68f, -0.67f, 2.68f, 0.67f));
            var wider = new BoardPlacement(P(0f, 0.2f), 0f, 0f, 8, 3, 0.58f);
            Assert.That(BoardFitter.Fits(floor, wider), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(8));
            Assert.That(fit.Rows, Is.EqualTo(2));
            Assert.That(fit.Pitch, Is.EqualTo(0.67f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            Assert.That(fit.ToWorldXZ(0f, fit.Rows * fit.Pitch / 2f).y, Is.EqualTo(0.67f).Within(0.0001f));
        }

        [Test]
        public void EqualGlassAreaFavorsTheWiderShapeThenLargerPanes()
        {
            var corridor = Floor(Rectangle(-2f, -0.5f, 2f, 0.5f));
            var wider = Floor(Rectangle(4f, -1f, 6f, 1f));
            // Both floors hold 4 m². The square admits 3 × 3 or 4 × 4 at that area.
            Assert.That(BoardFitter.TryFind(new[] { corridor, wider }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns, Is.EqualTo(3));
            Assert.That(fit.Rows, Is.EqualTo(3));
            Assert.That(fit.Pitch, Is.EqualTo(2f / 3f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(wider, fit), Is.True);
        }

        [Test]
        public void ABoardWithMoreGlassBeatsASmallerSquareNearThePlayer()
        {
            var square = Floor(Rectangle(-1.2f, -1.2f, 1.2f, 1.2f));
            var corridor = Floor(Rectangle(5f, 0f, 9.69f, 1.34f));
            Assert.That(BoardFitter.TryFind(new[] { square, corridor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.EqualTo(4.69f * 1.34f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(corridor, fit), Is.True);
        }

        [Test]
        public void FindsTheLargerSixDegreeFitBetweenThreeSupports()
        {
            var floor = Floor(Rectangle(-2f, -1.6f, 2f, 1.6f), new[] { P(1.6f, -1.3f) },
                new[] { P(-1.2f, 1f) }, new[] { P(-1.7f, -0.2f) });
            var witness = new BoardPlacement(P(0.094f, -0.145f), 0f, 6f, 7, 5, 0.5085f);
            Assert.That(BoardFitter.Fits(floor, witness), Is.True);
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch,
                Is.GreaterThanOrEqualTo(witness.Columns * witness.Rows * witness.Pitch * witness.Pitch));
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
        }

        [Test]
        public void RectanglesCertifyHeadFirstThenClosestSuggestedCenterAndKeepFallback()
        {
            var floor = Floor(Rectangle(-12f, -3.08f, 12f, 3.08f), new[] { Vector2.zero });
            var suggestions = new[] { P(6f, 0), P(1f, 0), P(4f, 0), P(float.NaN, 0) };
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var chosen,
                suggestedCenters: suggestions), Is.True);
            Assert.That(chosen.Columns * chosen.Rows, Is.EqualTo(64));
            Assert.That(chosen.Center, Is.EqualTo(P(4f, 0)));
            Assert.That(BoardFitter.Fits(floor, chosen), Is.True);
            foreach (var pair in new[] { new[] { P(4f, 0), P(-4f, 0) }, new[] { P(-4f, 0), P(4f, 0) } })
            {
                Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var tied,
                    suggestedCenters: pair), Is.True);
                Assert.That(tied.Center, Is.EqualTo(P(-4f, 0)));
            }
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-5f, 0), out var nearby,
                suggestedCenters: suggestions), Is.True);
            Assert.That(nearby.Center, Is.EqualTo(P(-5f, 0)));
            Assert.That(BoardFitter.TryFind(new[] { floor }, Vector2.zero, out var fallback,
                suggestedCenters: new[] { P(0.1f, 0) }), Is.True);
            Assert.That(fallback.Columns * fallback.Rows, Is.EqualTo(64));
            Assert.That(BoardFitter.Fits(floor, fallback), Is.True);
        }

        [Test]
        public void MatchesOrExceedsEveryAlignedShapeOnRectangularScans()
        {
            for (int x = 0; x <= 15; ++x)
            for (int z = 0; z <= 12; ++z)
            {
                float roomX = 1.8f + x * 0.24f, roomZ = 1.8f + z * 0.31f;
                var floor = Floor(Rectangle(0, 0, roomX, roomZ));
                Assert.That(BoardFitter.TryFind(new[] { floor }, P(roomX / 2, roomZ / 2), out var fit), Is.True);
                float expected = 0f;
                for (int c = 2; c <= 32; ++c)
                for (int r = 2; r <= 64 / c; ++r)
                {
                    float pitch = Mathf.Min(0.67f, Mathf.Min(roomX / c, roomZ / r));
                    if (pitch >= 0.5f) expected = Mathf.Max(expected, c * r * pitch * pitch);
                }
                float glassArea = fit.Columns * fit.Rows * fit.Pitch * fit.Pitch;
                Assert.That(glassArea, Is.GreaterThanOrEqualTo(expected - 0.0001f));
                Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            }
        }

        [Test]
        public void PreservesAnExactRotatedLegacyPatchBetweenFurniture()
        {
            var patch = new BoardPlacement(P(3.3f, -2.5f), 0f, 27.8f, 3, 4);
            // Furniture blocks the left end of a longer rotated floor, leaving one exact patch.
            Vector2[] InRoom(float left, float right) => new[] {
                patch.ToWorldXZ(left, -1f), patch.ToWorldXZ(right, -1f),
                patch.ToWorldXZ(right, 1f), patch.ToWorldXZ(left, 1f) };
            var floor = Floor(InRoom(-4f, 0.75f), InRoom(-4f, -0.75f));
            Assert.That(BoardFitter.Fits(floor, patch), Is.True);
            Assert.That(BoardFitter.TryFind(new[] { floor }, patch.ToWorldXZ(-3f, 0), out var fit), Is.True);
            Assert.That(BoardFitter.Fits(floor, fit), Is.True);
            Assert.That(fit.Columns * fit.Rows * fit.Pitch * fit.Pitch, Is.GreaterThanOrEqualTo(3f));
        }

        [Test]
        public void ConcavityBetweenFourInteriorCornersIsRejected()
        {
            var notch = Floor(new[] { P(-2,-2), P(2,-2), P(2,2), P(0.1f,2), P(0.1f,0.4f),
                P(-0.1f,0.4f), P(-0.1f,2), P(-2,2) });
            Assert.That(BoardFitter.Fits(notch, MinimumBoard()), Is.False);
        }

        [Test]
        public void FourCornersTouchingTheOutsideOfAnExactNotchIsNotFloor()
        {
            var notch = Floor(new[] { P(-2,-2), P(2,-2), P(2,2), P(0.5f,2), P(0.5f,-0.5f),
                P(-0.5f,-0.5f), P(-0.5f,2), P(-2,2) });
            Assert.That(BoardFitter.Fits(notch, MinimumBoard()), Is.False);
        }

        [Test]
        public void InteriorFurnitureCrossingWallsAndEnclosingVolumesAreRejected()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.1f,0.1f,0.2f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(-2,0.2f), P(2,0.2f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, new[] { P(0.4f,0.4f) }), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(-2,-2,2,2)), MinimumBoard()), Is.False);
        }

        [Test]
        public void RejectsFurnitureInsideGlassAndAcceptsExactEdgeContact()
        {
            var boundary = Rectangle(-3,-3,3,3);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.495f,0.1f,1f,0.2f)), MinimumBoard()), Is.False);
            Assert.That(BoardFitter.Fits(Floor(boundary, Rectangle(0.5f,0.1f,1f,0.2f)), MinimumBoard()), Is.True);
        }

        [TestCase(23.68f)]
        [TestCase(0.005f)]
        public void RotatedExactWidthFloorPreservesPoseAndMetricPitch(float angle)
        {
            var actual = new BoardPlacement(P(3.1f,-2.7f), 0.12f, angle, 4, 2);
            var floor = new RoomFloor(actual.FootprintOutline(), new List<Vector2[]>(), 0.12f);
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(0,0), out var found), Is.True);
            Assert.That(found.Rows * found.Columns, Is.EqualTo(8));
            Assert.That(found.FloorHeight, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That((found.Center - actual.Center).magnitude, Is.LessThan(0.0001f));
            Assert.That((found.ToWorldXZ(0.5f,0) - found.Center).magnitude, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(BoardFitter.Fits(floor, found), Is.True);
        }

        [Test]
        public void KeepsFullSizedTilesWhenTheMinimumCannotFit()
        {
            var small = Floor(Rectangle(0,0,8,0.999f));
            Assert.That(BoardFitter.TryFind(new[] { small }, Vector2.zero, out _), Is.False);
        }

        [Test]
        public void FindsOffsetSlotBoundedOnlyByFurnitureAndAThinWall()
        {
            var floor = Floor(Rectangle(-4,-0.5f,4,0.5f), Rectangle(-4,-1,0.123f,1),
                new[] { P(1.1232f,-1), P(1.1232f,1) }, Rectangle(1.124f,-1,4,1));
            Assert.That(BoardFitter.TryFind(new[] { floor }, P(-3,0), out var found), Is.True);
            Assert.That(found.Columns * found.Rows, Is.EqualTo(4));
            Assert.That(found.Center.x, Is.InRange(0.6229f,0.6233f));
        }

        [TestCase(17.3f)]
        [TestCase(89.8f)]
        public void PreservesAnExactRotatedAisleBetweenFourFurnitureFootprints(float angle)
        {
            var aisle = new BoardPlacement(P(-3.37f, 5.21f), 0f, angle, 8, 2);
            Vector2[] Box(float left, float bottom, float right, float top) =>
                Rectangle(left, bottom, right, top).Select(v => aisle.ToWorldXZ(v.x, v.y)).ToArray();
            RoomFloor FurnishedRoom(float topOfAisle) => Floor(Box(-2, -2, 2, 2),
                Box(-2, -2, 0, -.5f), Box(0, -2, 2, -.5f),
                Box(-2, topOfAisle, 0, 2), Box(0, topOfAisle, 2, 2));

            var room = FurnishedRoom(.5f);
            Assert.That(BoardFitter.TryFind(new[] { room }, aisle.Center, out var fit), Is.True);
            Assert.That(BoardFitter.Fits(room, fit), Is.True);
            Assert.That(fit.Columns * fit.Rows, Is.EqualTo(16));
            Assert.That((fit.Center - aisle.Center).magnitude, Is.LessThan(.001f));
            Assert.That(BoardFitter.TryFind(new[] { FurnishedRoom(.498f) }, aisle.Center, out _), Is.False);
        }

        [Test]
        public void RejectsCrossedOrMissingFloorGeometry()
        {
            var invalid = Floor(new[] { P(-3,-2), P(2,3), P(-2,2), P(3,-2) });
            Assert.That(BoardFitter.TryFind(new[] { invalid }, P(0,0), out _), Is.False);
            Assert.That(BoardFitter.TryFind(new[] { Floor(new Vector2[0]) }, P(0,0), out _), Is.False);
        }

        [Test]
        public void ExcludingADeclinedPatchProducesADisjointPreview()
        {
            var boundary = Rectangle(-7,-2,7,2);
            Assert.That(BoardFitter.TryFind(new[] { Floor(boundary) }, Vector2.zero, out var first), Is.True);
            var withRejection = Floor(boundary, first.FootprintOutline());
            Assert.That(BoardFitter.TryFind(new[] { withRejection }, Vector2.zero, out var second), Is.True);
            Assert.That(BoardFitter.Fits(withRejection, first), Is.False);
            Assert.That(BoardFitter.Fits(withRejection, second), Is.True);
        }

        private static BoardPlacement MinimumBoard() => new BoardPlacement(Vector2.zero, 0, 0, 2, 2);
        private static Vector2 P(float x, float z) => new Vector2(x,z);
        private static RoomFloor Floor(Vector2[] boundary, params Vector2[][] obstacles) =>
            new RoomFloor(boundary, new List<Vector2[]>(obstacles));
        private static Vector2[] Rectangle(float left, float bottom, float right, float top) =>
            new[] { P(left,bottom), P(right,bottom), P(right,top), P(left,top) };
    }
}
