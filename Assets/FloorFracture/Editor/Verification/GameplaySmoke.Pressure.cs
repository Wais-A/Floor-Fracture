using System.Collections;
using System.Linq;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator VerifyOpeningDraft()
        {
            var sound = game.GetComponent<GameSound>();
            var draft = sound.transform.Find("Opening draft").GetComponent<AudioSource>();
            var music = sound.transform.Find("Background music").GetComponent<AudioSource>();
            float master = PlayerPrefs.GetInt("ff.volume", 3) / 4f;
            yield return Wait(() => Mathf.Approximately(music.volume, master * .12f),
                "background music to fade back to the selected gain after a fall", 1);
            Require(draft.clip && draft.clip.name == "HoleDraft" && draft.clip.channels == 1 && draft.loop &&
                draft.spatialize && draft.spatialBlend == 1f && draft.GetComponent<MetaXRAudioSource>().EnableSpatialization,
                "The opening needs its own mono clip and Meta spatial voice.");

            // Keep a collapsed board visible in stone practice so both openings last beyond a complete loop.
            var state = game.State;
            var wave = state.BeginWave(CornerPane, 1, 1, game.GameplayTime - 5);
            int stance = wave.StationarySafe.OrderByDescending(i => (state.Center(i) - state.Center(CornerPane)).sqrMagnitude).First();
            state.Tick(state.Center(stance), true, wave.Deadline);
            int far = PlayablePanes.Where(i => !state.IsIntact(i)).OrderByDescending(i =>
                (state.Center(i) - state.Center(stance)).sqrMagnitude).First();
            int near = PlayablePanes.Where(i => i != stance && i != far).OrderBy(i =>
                (state.Center(i) - state.Center(stance)).sqrMagnitude).First();
            StandOn(stance); Choose("Tutorial"); Choose("Try the stone");
            game.BoardView.PracticeBreak(near);
            Vector3 Rim(int i) => game.BoardView.Board.TransformPoint(GlassGeometry.Ground(state.Center(i), -.16f));
            yield return Delay(.32);
            Require(!draft.isPlaying, "Looking across a hole triggered a draft.");
            LookAtTile(stance);
            yield return Delay(.25);
            Require(!draft.isPlaying, "Looking into intact glass triggered a draft.");
            LookAtTile(near);
            yield return Wait(() => draft.isPlaying, "draft from neighboring hole", 1);
            Require(Vector3.Distance(Rim(near), draft.transform.position) < .01f && draft.volume > 0f,
                "Draft did not play from just below the opening.");
            float onset = draft.volume;
            yield return Delay(.4);
            float full = draft.volume;
            Require(full > onset && draft.time > .25f, "The opening draft did not fade in.");
            float beforeTurn = draft.time;
            LookAtTile(far);
            yield return Wait(() => Vector3.Distance(Rim(far), draft.transform.position) < .02f,
                "continuous draft to follow a direct look at another opening", 1);
            Require(draft.isPlaying && draft.time >= beforeTurn && Mathf.Approximately(draft.volume, full),
                "Looking directly between openings restarted or silenced the breeze.");
            yield return Wait(() => draft.time < beforeTurn, "wind recording to wrap during a held look", draft.clip.length + 2);
            Require(draft.isPlaying && draft.volume > 0f, "Wind ended during a held look.");
            LookAtTile(stance);
            yield return Wait(() => draft.isPlaying && draft.volume < full && draft.volume > 0f,
                "audible fade while looking away", .4);
            yield return Wait(() => !draft.isPlaying, "draft to finish fading out", 1);
            LookAtTile(far);
            yield return Wait(() => draft.isPlaying, "draft after looking back", 1);
            sound.SetVolume(0);
            yield return Delay(.25);
            Require(!draft.isPlaying, "Master mute left the opening audible.");
            sound.SetVolume(master);
            yield return Wait(() => draft.isPlaying, "unmuted draft", 1);
            MenuInput(new OVRInput.OVRControllerTouch());
            Require(game.Stage == GameStage.Paused && !draft.isPlaying, "The opening played through the pause menu.");
            Choose("Main menu");
            float distance = Vector2.Distance(state.Center(stance), state.Center(far));
            Record($"The spatial breeze followed a direct look from a nearby hole to another {distance:F2} m away, played through the loop seam, faded in and out, and respected mute and pause.");
        }

        private static IEnumerator VerifyTileCracks()
        {
            var music = game.transform.Find("Background music").GetComponent<AudioSource>();
            Require(music.isPlaying && music.loop && music.clip.length >= 180f && music.spatialBlend == 0f &&
                music.clip.loadType == AudioClipLoadType.Streaming, "The long background track should stream in stereo.");
            music.time = music.clip.length - .25f;
            yield return Wait(() => music.time < 2f, "the full background track to loop", 3);
            music.time = 30f;
            Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            Require(music.isPlaying && music.time > 32f, "Opening the wave restarted or stopped the music.");
            var sound = game.GetComponent<GameSound>();
            float volume = PlayerPrefs.GetInt("ff.volume", 3) / 4f;
            var plan = game.State.ActiveWave;
            var board = game.BoardView.Board;
            var voices = plan.BreakingTiles.Select(i =>
            {
                var tile = board.Find($"Glass {i % game.State.Width}, {i / game.State.Width}");
                var source = tile.GetComponentInChildren<AudioSource>();
                Require(tile.Find("Fissures").GetComponent<Renderer>().enabled && source && source.clip &&
                    source.clip.name == "PaneStress" && source.clip.channels == 1 && source.spatialBlend == 1f &&
                    source.spatialize && source.GetComponent<MetaXRAudioSource>().EnableSpatialization &&
                    Vector3.Distance(source.transform.position, tile.position) < .001f,
                    "Each marked pane must emit its own mono crack at the position of its visible fissures.");
                return source;
            }).ToArray();
            float beforeSwitches = music.time;
            Require(voices.Length > 1 && board.GetComponentsInChildren<AudioSource>().Length == voices.Length,
                "A safe pane played a warning or multiple panes shared a source.");
            StandOn(plan.GuaranteedTile);
            yield return Delay(.12);
            Require(voices.All(v => v.isPlaying && v.volume > 0 && v.timeSamples > 0),
                "The recorded strain did not continue during the reveal.");
            StandOn(plan.StartingTile);
            yield return Frames(2);
            StandOn(plan.GuaranteedTile);
            yield return Frames(2);
            Require(music.isPlaying && music.time > beforeSwitches, "Rapid tile switches interrupted the background music.");
            sound.SetVolume(0);
            yield return Frames(2);
            Require(music.volume == 0f && music.isPlaying && voices.All(v => !v.gameObject.activeInHierarchy),
                "Master mute should silence the mix and preserve the music position.");
            sound.SetVolume(volume);
            yield return Frames(2);
            Require(voices.All(v => v.isPlaying), "Live warnings failed to resume with the master volume.");
            yield return Wait(() => game.GameplayTime > plan.RevealEndsAt + .03, "cracks to clear", 2);
            Require(voices.All(v => !v.gameObject.activeInHierarchy), "A warning continued into recall.");
            yield return Stage(GameStage.BetweenWaves);
            yield return Stage(GameStage.Countdown);
            Require(board.GetComponentsInChildren<AudioSource>().Any(v => v.clip && v.clip.name == "PaneStress"),
                "An automatic wave omitted its crack sounds.");
            MenuInput(new OVRInput.OVRControllerTouch());
            Require(music.isPlaying && music.time > beforeSwitches, "Pausing or advancing a wave restarted the score.");
            Require(board.GetComponentsInChildren<AudioSource>(true).All(v => !v.isPlaying),
                "A tile continued to crack while paused.");
            Choose("Main menu");
            Record($"All {voices.Length} glowing panes had separate Meta spatial sources. The recording continued during the reveal, respected mute, ended with the cracks and played again on the automatic wave.");
        }

        private static IEnumerator VerifyStagedPressure()
        {
            foreach (int level in new[] { 5, 7, 12 })
            {
                Choose("Development");
                Choose("Later levels");
                Choose("Level " + level);
                int RankStart(int tile)
                {
                    var probe = new BoardState(game.State.Layout, 379, game.State.Pitch);
                    var plan = probe.BeginWave(tile, level, 1, 0);
                    return (plan.IsJump == (level == 12) ? game.State.Count : 0) + plan.StationarySafe.Count;
                }
                int start = PlayablePanes.OrderByDescending(RankStart).First();
                StandOn(start);
                Choose("Start wave");
                yield return Stage(GameStage.Countdown);
                var wave = game.State.ActiveWave;
                if (level == 12) Require(wave.IsJump, "The opening should offer a one-hole jump.");
                if (wave.IsJump)
                {
                    var landing = game.BoardView.Board.Find("Jump landing").GetComponent<Renderer>();
                    var arrow = game.BoardView.Board.Find("Jump direction").GetComponent<Renderer>();
                    Require(wave.IsJump && !game.State.IsIntact(wave.JumpGap) && game.State.IsIntact(start) &&
                        landing.enabled && arrow.enabled,
                        "The one-hole jump needs an open lane, a visible direction and a marked landing.");
                    StandAt(game.State.Center(wave.JumpGap));
                    yield return Delay(.34);
                    Require(game.Stage == GameStage.Countdown && game.State.CurrentSample.Kind == SupportKind.Gap,
                        "The marked gap did not allow time to cross.");
                    StandOn(wave.GuaranteedTile);
                    yield return Stage(GameStage.BetweenWaves, 4);
                    Require(game.State.WavesSurvived == 1 && !landing.enabled && !arrow.enabled,
                        "Landing did not score once and clear the jump markers.");
                    if (level == 12)
                    {
                        MenuInput(new OVRInput.OVRControllerTouch());
                        Choose("Main menu");
                        yield return Stage(GameStage.Welcome);
                        continue;
                    }
                    yield return Stage(GameStage.Countdown, 4);
                    wave = game.State.ActiveWave;
                }
                int score = game.State.WavesSurvived;
                int groups = wave.BreakGroups.Count;
                Require(!wave.IsJump && groups >= 2, $"Level {level} did not stage an ordinary collapse.");
                var first = wave.BreakGroups[0];
                var last = wave.BreakGroups[groups - 1];
                Require(last.Tiles.Contains(wave.StartingTile) && last.BreaksAt == wave.Deadline,
                    "Starting glass was scheduled to give way early.");
                Transform Tile(int i) => game.BoardView.Board.Find($"Glass {i % game.State.Width}, {i / game.State.Width}");
                var earlyPane = Tile(first.Tiles[0]);
                var latePane = Tile(wave.StartingTile);
                Require(earlyPane.Find("Fissures").GetComponent<Renderer>().enabled &&
                    !latePane.Find("Fissures").GetComponent<Renderer>().enabled, "The sweep lit its last group at the first cue.");
                yield return Wait(() => game.GameplayTime >= wave.RevealEndsAt + .04, "end of sweep", 2);
                Require(!earlyPane.Find("Fissures").GetComponent<Renderer>().enabled &&
                    !latePane.Find("Fissures").GetComponent<Renderer>().enabled, "Recall retained a visible clue.");
                yield return Wait(() => game.State.AppliedBreakGroupCount >= 1, "first early collapse", 4);
                Require(!earlyPane.gameObject.activeSelf && latePane.gameObject.activeSelf && game.Stage == GameStage.Countdown,
                    "The early hole or the protected starting glass disagreed with the rules.");
                if (level == 7)
                {
                    int trap = game.Ending.CurrentLevelEnding;
                    game.SendMessage("OnApplicationPause", true);
                    game.SendMessage("OnApplicationPause", false);
                    Choose("Show board");
                    Require(game.State.IsIntact(first.Tiles[0]) && earlyPane.gameObject.activeSelf,
                        "Tracking recovery left a provisional hole from the canceled wave.");
                    Choose("Start wave");
                    yield return Stage(GameStage.Countdown);
                    wave = game.State.ActiveWave;
                    Require(game.Ending.CurrentLevelEnding == trap && game.State.AppliedBreakGroupCount == 0,
                        "Tracking recovery consumed the trap or an early break.");
                }
                StandOn(wave.GuaranteedTile);
                yield return Stage(GameStage.BetweenWaves,5);
                Require(game.State.WavesSurvived == score + 1 && !game.State.IsIntact(wave.StartingTile),
                    "A stationary step failed to survive the staged break.");
                MenuInput(new OVRInput.OVRControllerTouch());
                Choose("Main menu");
                yield return Stage(GameStage.Welcome);
            }
            Record("Levels 5 and 7 staged early collapses around a protected start. Tracking recovery restored the glass and trap. Level 12 opened one gap and showed the arrow and landing ring. Crossing the gap scored one wave and cleared both markers.");
        }
    }
}
