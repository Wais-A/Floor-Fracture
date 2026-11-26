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

                // Level-one Ready asks for no fog, so the forced fog stays through each 3 s roll-in.
                view.SetFog(new WaveHazards(1f, 0, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(3.2);
                var fog = Render(slices);
                float rise = fog.with.Average() - fog.without.Average();
                Require(rise >= 8f, $"Floor fog raised the board's mean luminance by only {rise:F1}/255.");

                view.SetFog(new WaveHazards(0f, 1, 0, Array.Empty<double>(), 7), game.GameplayTime);
                yield return Delay(3.2);
                var patch = Render(slices);
                var panes = PlayablePanes.Select(p => PaneRise(patch, p)).ToArray();
                Require(panes.Max() - panes.Min() >= 12f,
                    $"A fog patch must stand out over a pane: brightest {panes.Max():F1}, dimmest {panes.Min():F1} (/255 added).");
                Capture("hazard-fog", SmokeView.CenterEye);

                view.SetFog(WaveHazards.None, game.GameplayTime);
                yield return Delay(1.6);
                Require(slices.All(s => !s.enabled), "Fog renderers stayed on 1.6 s after the fog was withdrawn.");
                Record($"Forced floor fog raised the board's mean luminance by {rise:F0}/255. One patch added {panes.Max():F0}/255 over its pane against {panes.Min():F0}/255 elsewhere. Fog stays within 35 cm and was gone 1.6 s after withdrawal.");

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

        private static IEnumerator VerifyFogBelongsToTheLevel()
        {
            // Full fall motion, so the fall lifts the board and its fog.
            Choose("Settings");
            if (PlayerPrefs.GetInt("ff.reduced", 0) != 0) Choose("Fall motion");
            Choose("Back");
            Choose("Development"); Choose("Seed 379 · level 3");
            yield return Stage(GameStage.Ready);
            double ready = game.GameplayTime;
            var view = game.BoardView;
            var board = view.Board;
            var slices = Enumerable.Range(0, 4).Select(i => board.Find("Floor fog/Floor fog " + i).GetComponent<MeshRenderer>()).ToArray();
            var material = slices[0].sharedMaterial;
            int seed = view.Fog.VisualSeed;
            Require(view.Fog.FogDensity > 0 && view.Fog.FogPatches == 1, "Level three's Ready must bring in fog with one patch.");
            yield return Wait(() => game.GameplayTime >= ready + 3.2, "the level fog to roll in", 8);
            float rolledIn = material.GetFloat("_Fade");
            Require(rolledIn >= .99f && slices.All(s => s.enabled), $"The level fog must be in 3.2 s after Ready, not at {rolledIn:F2}.");
            bool Steady(string when) // checked on every sampled frame
            {
                float fade = material.GetFloat("_Fade");
                Require(slices.All(s => s.enabled) && fade >= .99f && view.Fog.VisualSeed == seed,
                    $"The level fog faded or changed {when}: fade {fade:F2}, seed {view.Fog.VisualSeed} (was {seed}).");
                return true;
            }
            StandOn(MiddlePane); Choose("Start wave");
            yield return Wait(() => Steady("in the count-in") && game.Stage == GameStage.Countdown, "the first wave", 6);
            for (int survived = 0; survived < 2; ++survived)
            {
                var wave = game.State.ActiveWave;
                int number = game.WaveNumber;
                StandOn(wave.GuaranteedTile);
                yield return Wait(() => Steady("during a wave") && game.Stage == GameStage.BetweenWaves, "the wave to resolve", 10);
                // A survived wave advances the number. A cleared level (finale, or a jump with no standing exit) does not.
                Require(game.WaveNumber == number + 1, $"Level three cleared on wave {number}, which rightly fades its fog; the fixture needs a longer level.");
                yield return Wait(() => Steady("between waves") && game.Stage == GameStage.Countdown, "the next wave", 8);
            }
            // Staying on the starting pane, which always breaks, loses the third wave.
            float restY = board.position.y;
            yield return Wait(() => Steady("in the losing wave") && game.Stage == GameStage.Fall, "the fall", 12);
            double fell = Now;
            // Smoke steps run from EditorApplication.update, between player frames: after FallSequence.Update
            // has moved the board and FloorFog.LateUpdate has written _BoardY, so the two must agree.
            var fogRoot = board.Find("Floor fog");
            bool MeasuredFromBoard()
            {
                float measured = material.GetFloat("_BoardY"), actual = fogRoot.position.y;
                Require(Mathf.Abs(measured - actual) <= .001f, $"The fog measured from y {measured:F3}, not the lifted board's {actual:F3}.");
                return true;
            }
            yield return Wait(() => Steady("in the fall") && MeasuredFromBoard() && Now >= fell + 1.5, "1.5 s of the fall", 8);
            float lift = board.position.y - restY;
            Require(lift >= 1f, $"The fall must lift the board, not move it {lift:F2} m.");
            game.Ending.Skip(); yield return Stage(GameStage.Results);
            yield return Wait(() => slices.All(s => !s.enabled), "the level fog to fade on Results", 1.6);
            Choose("Main menu"); yield return Stage(GameStage.Welcome);
            Record($"Level three's fog rolled in to {rolledIn:F2} 3.2 s after Ready and stayed at full strength with one visual seed through the count-in, two waves, the losing wave and the first 1.5 s of the fall, while the board rose {lift:F1} m. It was gone within 1.6 s of Results.");
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
            var veil = board.Find("Lightning/Flash veil")?.GetComponent<MeshRenderer>();
            Require(veil && !veil.enabled, "The board must carry a hidden flash veil.");
            var block = new MaterialPropertyBlock();
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
                veil.GetPropertyBlock(block); float veilAlpha = block.GetColor("_Tint").a;
                Require(veil.enabled && veilAlpha >= .45f, $"A strike must wash the whole view: veil on {veil.enabled}, alpha {veilAlpha:F2}.");
                view.DrawHazards(strike + .1); float gap = Flash;
                Require(!veil.enabled, "The flash veil must be off in the gap between flashes.");
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
                view.DrawHazards(now + .3 + .1);
                Require(Flash > 0 && !veil.enabled, "With Reduced motion a strike must not show the flash veil.");
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
            view.SetHazards(new WaveHazards(0, 0, 2, new[] { now + .2 }, 5), now);
            view.SetFog(new WaveHazards(1, 1, 0, Array.Empty<double>(), 5), now);
            yield return Delay(.3);
            MenuInput(new OVRInput.OVRControllerTouch());
            Choose("Main menu");
            yield return Delay(.7);
            var parts = Object.FindObjectsByType<MeshRenderer>()
                .Where(r => r.transform.parent && r.transform.parent.name is "Floor fog" or "Overhead lights" or "Lightning").ToArray();
            var lit = parts.Where(r => r.enabled).Select(r => r.name).ToArray();
            Require(lit.Length == 0, "Hazard renderers stayed on after Main menu: " + string.Join(", ", lit));
            var hums = Object.FindObjectsByType<AudioSource>().Where(a => a.name == "Lamp hum").ToArray();
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
