using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator VerifyHazardVisuals()
        {
            Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            var plan = game.State.ActiveWave;
            StandOn(plan.GuaranteedTile);
            var view = game.BoardView;
            var board = view.Board;
            var slices = Enumerable.Range(0, 4).Select(i => board.Find("Floor fog/Floor fog " + i)?.GetComponent<MeshRenderer>()).ToArray();
            Require(slices.All(s => s), "The board must carry four floor fog slices.");
            float floorY = game.Room.Placement.FloorHeight;
            Require(slices.All(s => s.GetComponent<MeshFilter>().sharedMesh.vertices.All(v => s.transform.TransformPoint(v).y - floorY <= .35f)),
                "Floor fog rose above knee height.");

            const int size = 96;
            var go = new GameObject("Hazard verification camera");
            var camera = go.AddComponent<Camera>();
            var target = RenderTexture.GetTemporary(size, size, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(size, size, TextureFormat.RGB24, false);
            var old = RenderTexture.active;
            float halfX = game.State.Width * game.State.Pitch * .5f, halfZ = game.State.Height * game.State.Pitch * .5f;
            float extent = Mathf.Max(halfX, halfZ);
            // Fog with its renderers on, then the same frame without them, as luminance.
            (float[] with, float[] without) Render()
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                float[] Read()
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, size, size), 0, 0); image.Apply();
                    return image.GetPixels32().Select(p => (21 * p.r + 72 * p.g + 7 * p.b) / 100f).ToArray();
                }
                Require(slices.All(s => s.enabled), "Forced fog did not enable its slices.");
                var lit = Read();
                foreach (var s in slices) s.enabled = false;
                var clear = Read();
                foreach (var s in slices) s.enabled = true;
                return (lit, clear);
            }
            float PaneRise((float[] with, float[] without) frame, int pane)
            {
                var c = camera.WorldToViewportPoint(board.TransformPoint(GlassGeometry.Ground(game.State.Center(pane))));
                int half = Mathf.Max(1, (int)(game.State.Pitch * .45f / (2 * extent) * size));
                int cx = (int)(c.x * size), cy = (int)(c.y * size);
                float sum = 0; int count = 0;
                for (int y = Math.Max(0, cy - half); y < Math.Min(size, cy + half); ++y)
                    for (int x = Math.Max(0, cx - half); x < Math.Min(size, cx + half); ++x)
                    { sum += frame.with[y * size + x] - frame.without[y * size + x]; ++count; }
                return count == 0 ? 0 : sum / count;
            }
            try
            {
                camera.enabled = false;
                camera.GetUniversalAdditionalCameraData().allowXRRendering = false;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.aspect = 1;
                camera.targetTexture = target;
                camera.orthographic = true;
                camera.orthographicSize = extent;
                camera.nearClipPlane = .05f; camera.farClipPlane = 3f;
                // Straight down from below a real ceiling, with board +z at the top of the image.
                camera.transform.SetPositionAndRotation(board.TransformPoint(new Vector3(0, 1.5f, 0)), Quaternion.LookRotation(-board.up, board.forward));

                view.SetHazards(new WaveHazards(1f, 0, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(.5);
                var fog = Render();
                float rise = fog.with.Average() - fog.without.Average();
                Require(rise >= 8f, $"Floor fog raised the board's mean luminance by only {rise:F1}/255.");

                view.SetHazards(new WaveHazards(0f, 1, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(.5);
                var patch = Render();
                var panes = PlayablePanes.Select(p => PaneRise(patch, p)).ToArray();
                Require(panes.Max() - panes.Min() >= 12f,
                    $"A fog patch must stand out over a pane: brightest {panes.Max():F1}, dimmest {panes.Min():F1} (/255 added).");
                Capture("hazard-fog", SmokeView.CenterEye);
                Require(game.Stage != GameStage.Countdown || game.State.ActiveWave == plan, "A new wave replaced the forced hazards mid-check.");

                view.SetHazards(WaveHazards.None, game.GameplayTime);
                yield return Delay(.7);
                Require(slices.All(s => !s.enabled), "Fog renderers stayed on 0.7 s after the hazards were withdrawn.");
                Record($"Forced floor fog raised the board's mean luminance by {rise:F0}/255. One patch added {panes.Max():F0}/255 over its pane against {panes.Min():F0}/255 elsewhere. Fog stays within 35 cm and was gone 0.7 s after withdrawal.");
            }
            finally
            {
                RenderTexture.active = old;
                Object.DestroyImmediate(image);
                Object.DestroyImmediate(go);
                RenderTexture.ReleaseTemporary(target);
            }
            MenuInput(new OVRInput.OVRControllerTouch());
            Choose("Main menu");
        }
    }
}
