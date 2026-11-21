using System;
using System.Reflection;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Labels = Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static void VerifyScoreboardMounts()
        {
            var host = new GameObject("Scoreboard placement verification");
            var type = typeof(GlassBoardView).Assembly.GetType("FloorFracture.WallScoreboard", true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var mount = type.GetMethod("Mount", flags);
            var readout = type.GetMethod("SetReadout", flags);
            var fit = new BoardPlacement(Vector2.zero, 0f, 0f, 2, 2, .5f);
            var room = host.AddComponent<MRUKRoom>();
            Mesh rawMesh = null;
            try
            {
                var eye = Child("Observer");
                eye.position = Vector3.up * 1.6f;
                var wall = Plane(Labels.WALL_FACE, new Vector3(0, 1.4f, 2f), 1.55f, 2.6f);
                room.WallAnchors.Add(wall);
                var fixedSign = Place(null);
                var display = fixedSign.GetComponentInChildren<TextMesh>(true);
                Require(display && Mathf.Abs(fixedSign.transform.position.z - 1.99f) < .01f &&
                    Mathf.Abs(fixedSign.transform.position.y - eye.position.y) < .05f &&
                    Vector3.Dot(fixedSign.transform.forward, -wall.transform.forward) > .999f,
                    "The gym scoreboard failed to sit flush on the visible wall near eye height.");
                var savedPose = new Pose(fixedSign.transform.position, fixedSign.transform.rotation);
                const string beat = "<size=180>4</size>\nLOOK   ·   L1 W1";
                readout.Invoke(fixedSign, new object[] { beat });
                var renderer = display.GetComponent<MeshRenderer>();
                var printed = renderer.bounds;
                Require(display.text == beat && display.gameObject.activeInHierarchy && printed.size.x > .10f &&
                    printed.size.x < 1.16f && printed.size.y < .56f && printed.size.y > .15f,
                    "The countdown and level/wave caption must fit inside the physical cabinet.");
                VerifyScoreboardPixels(fixedSign.transform, display);
                readout.Invoke(fixedSign, new object[] { "" });
                Require(!display.transform.parent.gameObject.activeSelf && fixedSign.gameObject.activeSelf,
                    "An empty readout should hide the entire cabinet while retaining the mount.");
                eye.SetPositionAndRotation(new Vector3(-.25f, 1.53f, .1f), Quaternion.Euler(0, 72, 0));
                mount.Invoke(fixedSign, new object[] { room, fit, eye, null });
                readout.Invoke(fixedSign, new object[] { "SAFE" });
                Require(display.gameObject.activeInHierarchy && fixedSign.transform.position == savedPose.position &&
                    Quaternion.Angle(fixedSign.transform.rotation, savedPose.rotation) < .001f,
                    "The physical scoreboard turned or moved when the player looked away.");
                eye.SetPositionAndRotation(Vector3.up * 1.6f, Quaternion.Euler(0, 180, 0));
                Require(Place(null).GetComponentInChildren<TextMesh>(true),
                    "A clear, readable wall behind the observer should qualify when the room has no better wall.");
                eye.rotation = Quaternion.identity;

                foreach (var label in new[] { Labels.WINDOW_FRAME, Labels.DOOR_FRAME, Labels.WALL_ART })
                {
                    // A narrow feature fits between centre and corner visibility rays.
                    var blocked = Plane(label, new Vector3(.30f, 1.60f, 1.985f), .14f, 1.95f);
                    Require(!Place(null).GetComponentInChildren<TextMesh>(true),
                        "The cabinet overlapped a mapped " + label + ".");
                    room.Anchors.Remove(blocked);
                    Object.DestroyImmediate(blocked.gameObject);
                }

                var furniture = Plane(Labels.STORAGE, new Vector3(0, 1.6f, 1.1f), 1.7f, 1.8f);
                typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.VolumeBounds)).SetValue(furniture,
                    new Bounds(Vector3.zero, new Vector3(1.7f, 1.8f, .35f)));
                Require(!Place(null).GetComponentInChildren<TextMesh>(true),
                    "A cabinet behind mapped furniture was still visible to the placement algorithm.");
                room.Anchors.Remove(furniture);
                Object.DestroyImmediate(furniture.gameObject);

                var scan = Child("Saved room mesh").gameObject.AddComponent<MRUKAnchor>();
                typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.Label)).SetValue(scan, Labels.GLOBAL_MESH);
                rawMesh = new Mesh { name = "Unlabelled wall protrusion" };
                rawMesh.vertices = new[] { new Vector3(.27f, .8f, 1.86f), new Vector3(.27f, 2.4f, 1.86f),
                    new Vector3(.33f, 2.4f, 1.86f), new Vector3(.33f, .8f, 1.86f) };
                rawMesh.triangles = new[] { 0, 1, 2, 2, 3, 0 };
                typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.GlobalMesh)).SetValue(scan, rawMesh);
                typeof(MRUKRoom).GetProperty(nameof(MRUKRoom.GlobalMeshAnchor)).SetValue(room, scan);
                Require(!Place(null).GetComponentInChildren<TextMesh>(true),
                    "Unlabelled scan geometry between the sign's reading corners intersected the cabinet.");
                rawMesh.triangles = new[] { 2, 1, 0, 0, 3, 2 };
                Require(!Place(null).GetComponentInChildren<TextMesh>(true),
                    "Reversing the scan triangle winding allowed the thin obstruction.");
                typeof(MRUKRoom).GetProperty(nameof(MRUKRoom.GlobalMeshAnchor)).SetValue(room, null);

                wall.PlaneBoundary2D.Clear();
                wall.PlaneBoundary2D.AddRange(new[] { new Vector2(-.775f, -1.3f), new Vector2(.775f, -1.3f),
                    new Vector2(.775f, 1.3f), new Vector2(.12f, 1.3f), new Vector2(.12f, -.30f),
                    new Vector2(.07f, -.30f), new Vector2(.07f, 1.3f), new Vector2(-.775f, 1.3f) });
                Require(!Place(null).GetComponentInChildren<TextMesh>(true),
                    "The cabinet extended across a concave cut in the wall outline.");

                room.WallAnchors.Clear();
                room.Anchors.Clear();
                int checks = 0;
                var stand = Place((floor, radius) =>
                {
                    ++checks;
                    return floor.z > .5f + radius && Mathf.Abs(floor.y) < .001f;
                });
                var standMesh = stand.GetComponentInChildren<MeshFilter>(true);
                Require(checks > 0 && standMesh && standMesh.sharedMesh.name == "Freestanding scoreboard" &&
                    Mathf.Abs(standMesh.transform.TransformPoint(standMesh.sharedMesh.bounds.min).y) < .001f &&
                    stand.transform.position.z > 1.1f, "The fallback stand must rest on surveyed floor outside the board.");
                Require(!Place((_, __) => false).GetComponentInChildren<Renderer>(true) &&
                    !Place(null).GetComponentInChildren<Renderer>(true),
                    "A room without a wall or confirmed stand footprint displayed a scoreboard.");
                var layout = new BoardLayout(5, 5, 0x1FUL | 1UL << 5 | 1UL << 10 | 1UL << 15 | 1UL << 20);
                var ctor = typeof(BoardPlacement).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
                    new[] { typeof(Vector2), typeof(float), typeof(float), typeof(BoardLayout), typeof(float) }, null);
                fit = (BoardPlacement)ctor.Invoke(new object[] { Vector2.zero, 0f, 0f, layout, .6f });
                eye.SetPositionAndRotation(new Vector3(-1.2f, 1.6f, 0), Quaternion.LookRotation(Vector3.right));
                var representative = (Vector2)type.GetMethod("CentralPane", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { fit, new Vector2(eye.position.x, eye.position.z) });
                Require(Vector2.Distance(representative, new Vector2(-1.2f, 0)) < .001f,
                    "The viewpoint of an L-shaped board must use its nearest occupied centre pane, resolving ties toward the wearer.");
                var nook = Place((floor, radius) => Mathf.Abs(floor.x) < .2f && floor.z > .15f && floor.z < .75f);
                Require(nook.GetComponentInChildren<MeshFilter>(true) && Mathf.Abs(nook.transform.position.x) < .2f &&
                    nook.transform.position.z < .75f, "The stand missed surveyed clear floor inside the L-shaped board's open nook.");
                var overlap = type.GetMethod("OverlapsPane", BindingFlags.Static | BindingFlags.NonPublic);
                Require((bool)overlap.Invoke(null, new object[] { fit, new Vector2(-.16f, -.5f) }) &&
                    !(bool)overlap.Invoke(null, new object[] { fit, new Vector2(-.16f, .6f) }),
                    "Stand clearance must account for every occupied arm of the board.");
                Record("The scoreboard mounted flush, held its pose, rejected wall openings, art, furniture and a concave edge, and required clear floor for its stand.");

                Component Place(Func<Vector3, float, bool> clear)
                {
                    var board = Child("Test scoreboard").gameObject.AddComponent(type);
                    mount.Invoke(board, new object[] { room, fit, eye, clear });
                    return board;
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (rawMesh) Object.DestroyImmediate(rawMesh);
            }

            Transform Child(string objectName)
            {
                var child = new GameObject(objectName).transform;
                child.SetParent(host.transform, false);
                return child;
            }

            MRUKAnchor Plane(Labels label, Vector3 center, float width, float height)
            {
                var child = Child(label.ToString());
                child.SetPositionAndRotation(center, Quaternion.LookRotation(Vector3.back));
                var anchor = child.gameObject.AddComponent<MRUKAnchor>();
                typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.Label)).SetValue(anchor, label);
                var rectangle = new Rect(-width * .5f, -height * .5f, width, height);
                typeof(MRUKAnchor).GetProperty(nameof(MRUKAnchor.PlaneRect)).SetValue(anchor, rectangle);
                anchor.PlaneBoundary2D.AddRange(new[] { rectangle.min, new Vector2(rectangle.xMax, rectangle.yMin),
                    rectangle.max, new Vector2(rectangle.xMin, rectangle.yMax) });
                room.Anchors.Add(anchor);
                return anchor;
            }
        }

        private static void VerifyScoreboardPixels(Transform sign, TextMesh display)
        {
            const int width = 240, height = 128, layer = 29;
            foreach (var child in sign.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
            var go = new GameObject("Physical scoreboard camera");
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var wallMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            var camera = go.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                var housing = sign.Find("Scoreboard cabinet/Housing").GetComponent<MeshRenderer>();
                var letters = display.GetComponent<MeshRenderer>();
                Require(letters.sharedMaterial.shader == housing.sharedMaterial.shader &&
                    housing.sharedMaterial.shader.name == "Floor Fracture/Scoreboard" &&
                    letters.sharedMaterial.mainTexture == display.font.material.mainTexture,
                    "The physical fixture and its live font atlas must use the same room-depth shader.");
                var forward = sign.forward;
                camera.transform.SetPositionAndRotation(sign.position - forward * 1.5f, sign.rotation);
                camera.enabled = false;
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.magenta;
                camera.cullingMask = 1 << layer;
                camera.orthographic = true;
                camera.orthographicSize = .33f;
                camera.aspect = (float)width / height;
                camera.nearClipPlane = .05f;
                camera.targetTexture = target;
                blocker.layer = layer;
                blocker.transform.SetPositionAndRotation(sign.position - forward * .65f, sign.rotation);
                blocker.transform.localScale = new Vector3(1.18f, .58f, .08f);
                blocker.GetComponent<MeshRenderer>().sharedMaterial = wallMaterial;
                wallMaterial.SetColor("_BaseColor", Color.cyan);
                blocker.SetActive(false);

                var clear = Read();
                int digits = 0, caption = 0, dark = 0, occludedGlyphs = 0;
                blocker.SetActive(true);
                var covered = Read();
                for (int i = 0; i < clear.Length; i++)
                {
                    var p = clear[i];
                    if (p.r < 85 && p.g < 85 && p.b < 85) ++dark;
                    if (p.r < 140 || p.g < 85 || p.r < p.b + 45) continue;
                    if (i / width >= height / 2) ++digits; else ++caption;
                    var q = covered[i];
                    if (q.r < 95 && q.g > 140 && q.b > 140) ++occludedGlyphs;
                }
                Require(digits > 35 && caption > 35 && dark > width * height * .40f &&
                    occludedGlyphs >= (digits + caption) * .98f,
                    $"The GPU must show amber digits and caption on the dark case and occlude both behind virtual geometry. Upper {digits}, lower {caption}, dark {dark}, covered {occludedGlyphs}.");
                Color32[] Read()
                {
                    RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    image.Apply();
                    return image.GetPixels32();
                }
            }
            finally
            {
                RenderTexture.active = previous;
                camera.targetTexture = null;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(blocker);
                Object.DestroyImmediate(wallMaterial);
                Object.DestroyImmediate(image);
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
