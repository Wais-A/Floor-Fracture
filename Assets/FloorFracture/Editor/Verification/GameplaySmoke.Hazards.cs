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
            // Spotlights first, in Ready, where no wave start replaces the forced set during the 3 s check.
            Choose("Start"); StandOn(MiddlePane);
            yield return Stage(GameStage.Ready);
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
            // Hazard renderers on, then the same frame without them, as luminance.
            (float[] with, float[] without) Render(MeshRenderer[] parts)
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = target };
                float[] Read()
                {
                    RenderPipeline.SubmitRenderRequest(camera, request);
                    RenderTexture.active = target;
                    image.ReadPixels(new Rect(0, 0, size, size), 0, 0); image.Apply();
                    return image.GetPixels32().Select(p => (21 * p.r + 72 * p.g + 7 * p.b) / 100f).ToArray();
                }
                Require(parts.All(s => s.enabled), "Forced hazards did not enable their renderers.");
                var lit = Read();
                foreach (var s in parts) s.enabled = false;
                var clear = Read();
                foreach (var s in parts) s.enabled = true;
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

                view.SetHazards(new WaveHazards(0, 0, 2, Array.Empty<double>(), 11), game.GameplayTime);
                yield return Delay(.5);
                var lights = board.Find("Overhead lights");
                var lamps = Enumerable.Range(0, 3).Select(i => lights.Find("Lamp " + i)).Where(l => l.gameObject.activeSelf).ToArray();
                Require(lamps.Length == 2, $"Two spotlights must light two lamps, not {lamps.Length}.");
                var room = game.Room.CurrentRoom;
                float? ceiling = room && room.CeilingAnchors.Count > 0 ? room.CeilingAnchors.Min(a => a.transform.position.y) : null;
                float lampY = WaveHazards.LampHeight(floorY, ceiling);
                Require(lamps.All(l => Mathf.Abs(l.position.y - lampY) <= .01f),
                    $"Lamps must hang at {lampY:F2} m, not {string.Join(", ", lamps.Select(l => l.position.y.ToString("F2")))}.");
                var lit = lights.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled).ToArray();
                Require(lit.Length == 4, $"Two spotlights must draw two beams and two pools, not {lit.Length} renderers.");
                var spot = Render(lit);
                float brightest = Enumerable.Range(0, spot.with.Length).Max(i => spot.with[i] - spot.without[i]);
                Require(brightest >= 25f, $"Spotlights brightened the board's brightest pixel by only {brightest:F1}/255.");
                Capture("hazard-spotlights", SmokeView.CenterEye);
                var pools = Enumerable.Range(0, 2).Select(i => lights.Find("Spotlight pool " + i)).ToArray();
                var last = pools.Select(p => p.localPosition).ToArray();
                var swept = new float[pools.Length];
                double sweepEnds = Now + 2;
                yield return Wait(() =>
                {
                    for (int k = 0; k < pools.Length; ++k) { swept[k] += Vector3.Distance(last[k], pools[k].localPosition); last[k] = pools[k].localPosition; }
                    return Now >= sweepEnds;
                }, "spotlight pools to sweep", 10);
                Require(swept.All(d => d >= .3f), $"Each spotlight pool must sweep 0.3 m in 2 s: {string.Join(", ", swept.Select(d => d.ToString("F2")))} m.");
                var sound = game.GetComponent<GameSound>();
                var hums = lamps.Select(l => l.GetComponentInChildren<AudioSource>()).ToArray();
                Require(hums.All(h => h && h.isPlaying && h.spatialize && h.volume > 0), "Each lit lamp must play its spatial hum.");
                float volume = sound.MasterVolume;
                sound.SetVolume(0);
                Require(hums.All(h => !h.isPlaying), "Lamp hums kept playing at zero volume.");
                sound.SetVolume(volume);
                view.SetHazards(WaveHazards.None, game.GameplayTime);
                yield return Delay(.7);
                Require(lit.All(r => !r.enabled) && hums.All(h => !h.isPlaying), "Spotlights stayed on 0.7 s after the hazards were withdrawn.");
                Record($"Two forced spotlights hung at {lampY:F2} m, brightened the board's brightest pixel by {brightest:F0}/255 and swept their pools {swept.Min():F2} m or more in 2 s. Each lamp hummed spatially, went silent at zero volume and switched off 0.7 s after withdrawal.");

                Choose("Start wave");
                yield return Stage(GameStage.Countdown);
                var plan = game.State.ActiveWave;
                StandOn(plan.GuaranteedTile);
                view.SetHazards(new WaveHazards(1f, 0, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(.5);
                var fog = Render(slices);
                float rise = fog.with.Average() - fog.without.Average();
                Require(rise >= 8f, $"Floor fog raised the board's mean luminance by only {rise:F1}/255.");

                view.SetHazards(new WaveHazards(0f, 1, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(.5);
                var patch = Render(slices);
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

        private static float Flash => Shader.GetGlobalFloat("_FloorFractureFlash");

        // The peak flash between now and `until` on the gameplay clock.
        private static IEnumerator SampleFlash(double until, float[] peak)
        {
            peak[0] = 0;
            yield return Wait(() => { peak[0] = Mathf.Max(peak[0], Flash); return game.GameplayTime >= until; }, "the lightning flash", 5);
        }

        private static IEnumerator VerifyLightning()
        {
            // In Ready, where no wave start replaces the forced strike.
            Choose("Start"); StandOn(MiddlePane);
            yield return Stage(GameStage.Ready);
            var view = game.BoardView;
            var board = view.Board;
            var sound = game.GetComponent<GameSound>();
            var sky = board.Find("Lightning/Sky flash")?.GetComponent<MeshRenderer>();
            Require(sky && !sky.enabled, "The board must carry a hidden sky flash.");
            bool oldReduced = view.ReducedMotion;
            float oldVolume = sound.MasterVolume;
            var peak = new float[1];
            try
            {
                view.ReducedMotion = false;
                sound.SetVolume(1); // a muted mix plays no thunder
                int cues = sound.EditorThunderCues;
                double now = game.GameplayTime, strike = now + .3;
                view.SetHazards(new WaveHazards(0, 0, 0, new[] { strike }, 5), now);
                yield return Wait(() => game.GameplayTime >= strike, "the forced strike", 5);
                // Read the curve at fixed clock times, so a slow frame cannot step over the 60 ms peak.
                view.DrawHazards(strike + .05); float full = Flash;
                view.DrawHazards(strike + .1); float gap = Flash;
                view.DrawHazards(strike + .19); float second = Flash;
                Require(full > .9f && gap == 0 && second > .6f,
                    $"A strike must flash above 0.9, go dark, then flash again: {full:F2}, {gap:F2}, {second:F2}.");
                yield return Wait(() => game.GameplayTime >= now + .9, "the flash to end", 5);
                Require(Flash == 0 && !sky.enabled, $"The flash must be over by 0.6 s after the strike, not {Flash:F2}.");
                var thunder = sound.EditorLastThunder;
                Require(sound.EditorThunderCues - cues == 1 && thunder && thunder.spatialize,
                    $"One strike must schedule exactly one spatial thunder voice, not {sound.EditorThunderCues - cues}.");
                var offset = thunder.transform.position - board.position;
                Require(offset.magnitude >= 8f && Vector3.Dot(offset, board.up) >= 2f,
                    $"Thunder must come from at least 8 m away and 2 m up, not {offset.magnitude:F1} m and {Vector3.Dot(offset, board.up):F1} m.");

                view.ReducedMotion = true;
                now = game.GameplayTime;
                view.SetHazards(new WaveHazards(0, 0, 0, new[] { now + .3 }, 5), now);
                yield return SampleFlash(now + .3 + .7, peak);
                float soft = peak[0];
                Require(soft > .1f && soft <= .35f, $"With Reduced motion a strike must glow softly at 0.35 or less, not {soft:F2}.");
                Record($"A forced strike flashed to {full:F2}, went dark and flashed again at {second:F2}, ended by 0.6 s, with one spatial thunder voice {offset.magnitude:F1} m out and {Vector3.Dot(offset, board.up):F1} m up. With Reduced motion it peaked at {soft:F2}.");
            }
            finally { view.ReducedMotion = oldReduced; sound.SetVolume(oldVolume); }
            view.SetHazards(WaveHazards.None, game.GameplayTime);
            yield return Delay(.7);
            Require(!sky.enabled && Flash == 0, "The sky flash stayed on after the hazards were withdrawn.");
        }

        private static IEnumerator VerifyHazardPauseAndExit()
        {
            // In Countdown: a menu pause from Ready withdraws the hazards on resume, a Countdown pause keeps them.
            Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            StandOn(game.State.ActiveWave.GuaranteedTile);
            var view = game.BoardView;
            var sound = game.GetComponent<GameSound>();
            var voices = sound.GetComponentsInChildren<AudioSource>(true).Where(a => a.name.StartsWith("Thunder ")).ToArray();
            int cues = sound.EditorThunderCues;
            double strike = game.GameplayTime + .4;
            view.SetHazards(new WaveHazards(0, 0, 1, new[] { strike }, 5), game.GameplayTime);
            yield return Delay(.1);
            var hum = view.Board.Find("Overhead lights/Lamp 0").GetComponentInChildren<AudioSource>();
            Require(hum.isPlaying, "The forced spotlight must hum before the pause.");
            MenuInput(new OVRInput.OVRControllerTouch());
            Require(game.Stage == GameStage.Paused && game.GameplayTime < strike, "The menu did not pause before the strike.");
            yield return Delay(1);
            Require(Flash == 0 && sound.EditorThunderCues == cues && voices.All(v => !v.isPlaying) && !hum.isPlaying,
                "A paused run flashed, played thunder or kept its lamp hum.");
            Choose("Resume");
            Require(game.Stage == GameStage.Countdown, $"Resume returned to {game.Stage}, not the countdown.");
            yield return Wait(() => game.GameplayTime >= strike + .7, "the held strike", 5);
            Require(sound.EditorThunderCues - cues == 1, $"The held strike must fire once after resume, not {sound.EditorThunderCues - cues} times.");

            double now = game.GameplayTime;
            view.SetHazards(new WaveHazards(1, 1, 2, new[] { now + .2 }, 5), now);
            yield return Delay(.3);
            MenuInput(new OVRInput.OVRControllerTouch());
            Choose("Main menu");
            yield return Delay(.7);
            var parts = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None)
                .Where(r => r.transform.parent && r.transform.parent.name is "Floor fog" or "Overhead lights" or "Lightning").ToArray();
            var lit = parts.Where(r => r.enabled).Select(r => r.name).ToArray();
            Require(lit.Length == 0, "Hazard renderers stayed on after Main menu: " + string.Join(", ", lit));
            var hums = Object.FindObjectsByType<AudioSource>(FindObjectsSortMode.None).Where(a => a.name == "Lamp hum").ToArray();
            Require(hums.All(h => !h.isPlaying) && voices.All(v => !v.isPlaying) && Flash == 0,
                "Main menu left a lamp hum, thunder or the flash running.");

            // A strike that comes due while the hazards fade out must not flash or thunder.
            var home = game.BoardView;
            now = game.GameplayTime;
            home.SetHazards(new WaveHazards(0, 0, 0, new[] { now + .6 }, 5), now);
            yield return Wait(() => game.GameplayTime >= now + .4, "the forced set to fade in", 5);
            int before = sound.EditorThunderCues;
            home.SetHazards(WaveHazards.None, game.GameplayTime);
            var peak = new float[1];
            yield return SampleFlash(now + 1.1, peak);
            Require(peak[0] == 0 && sound.EditorThunderCues == before,
                $"A strike during the fade-out flashed to {peak[0]:F2} or played thunder.");
            Record($"A strike held through a 1 s menu pause with no flash, thunder or hum, then fired once after resume. Main menu switched off {parts.Length} hazard renderers, every lamp hum, the thunder and the flash within 0.7 s. A strike due 0.2 s into the fade-out stayed dark and silent.");
        }
    }
}
