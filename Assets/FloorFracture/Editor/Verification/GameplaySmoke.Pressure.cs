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
            foreach (int level in new[] {6,7,9,12})
            {
                // Start an actual development run on the room fitted by the scene.
                game.SendMessage("DevelopmentRun",level);
                int RankStart(int tile)
                {
                    var probe=new BoardState(game.State.Layout,379,game.State.Pitch);
                    var plan=probe.BeginWave(tile,level,1,0);
                    return 100*(plan.JumpLandings.Count+plan.RowRoute.Count)+plan.StationarySafe.Count;
                }
                int start=PlayablePanes.OrderByDescending(RankStart).First();
                Transform Tile(int i) => game.BoardView.Board.Find($"Glass {i%game.State.Width}, {i/game.State.Width}");
                bool Cracked(int i) => Tile(i).Find("Fissures").GetComponent<Renderer>().enabled;
                StandOn(start); Choose("Start wave");
                yield return Stage(GameStage.Countdown);
                var wave=game.State.ActiveWave;
                var ring=game.BoardView.Board.Find("Occupied square").GetComponent<Renderer>();
                Require(!game.BoardView.Board.Find("Jump direction") && !game.BoardView.Board.Find("Jump landing") &&
                    ring.enabled,"Only the underfoot support ring should be present.");
                if(level==6 || level==9)
                {
                    Require(wave.RowRoute.Count==(level==6 ? 2 : 3),"The fitted room must support the complete row crossing.");
                    if(level==6)
                    {
                        // Stay on the first marked row, then retry this level through Results.
                        yield return Stage(GameStage.Fall,4);
                        Require(game.State.LastLoss==WaveLossReason.GapGraceExpired,"Waiting on the first row should lose after it falls.");
                        game.Ending.Skip(); yield return Stage(GameStage.Results);
                        Choose("Retry Level 6"); StandOn(start); Choose("Start wave");
                        yield return Stage(GameStage.Countdown);
                        wave=game.State.ActiveWave;
                    }
                    if(level==9)
                    {
                        StandOn(wave.RowRoute[0]);
                        yield return Wait(()=>game.State.AppliedBreakGroupCount==1,"first row to fall",3);
                        MenuInput(new OVRInput.OVRControllerTouch());
                        double frozen=game.GameplayTime;
                        yield return Delay(.3);
                        Require(game.Stage==GameStage.Paused && frozen==game.GameplayTime && game.State.AppliedBreakGroupCount==1,
                            "Opening the menu must freeze the current row.");
                        Choose("Resume");
                        int trap=game.Ending.CurrentLevelEnding;
                        game.SendMessage("OnApplicationPause",true); game.SendMessage("OnApplicationPause",false);
                        Choose("Show board");
                        Require(game.State.IsIntact(start) && Tile(start).gameObject.activeSelf &&
                            game.Ending.CurrentLevelEnding==trap,"Tracking recovery must restore the rows and trap.");
                        StandOn(start); Choose("Start wave"); yield return Stage(GameStage.Countdown);
                        wave=game.State.ActiveWave;
                    }
                    for(int i=0;i<wave.RowRoute.Count;++i)
                    {
                        int previous=i==0 ? start : wave.RowRoute[i-1];
                        int next=wave.RowRoute[i];
                        Require(Cracked(previous) && !Cracked(next),"Only the currently falling row should cue the next step.");
                        if(i==0) Capture("level-"+level+"-collapsing-row",SmokeView.BoardObserver);
                        StandOn(next);
                        if(i+1<wave.RowRoute.Count)
                        {
                            int groups=i+1;
                            yield return Wait(()=>game.State.AppliedBreakGroupCount==groups,"next row cue",3);
                            Require(!Tile(previous).gameObject.activeSelf && Tile(next).gameObject.activeSelf,
                                "Visual glass disagreed with the row that just fell.");
                        }
                    }
                }
                else if(level==7)
                {
                    Require(wave.JumpLandings.Count>1,"The recorded floor should offer alternate jump directions.");
                    int landing=wave.JumpLandings.First(tile=>tile!=wave.GuaranteedTile);
                    yield return Wait(()=>game.GameplayTime>=wave.JumpOpensAt+.03,"gap opening",3);
                    Capture("jump-with-open-landings",SmokeView.BoardObserver);
                    int gap=(start+landing)/2;
                    Require(!game.State.IsIntact(gap) && !Tile(gap).gameObject.activeSelf && Tile(landing).gameObject.activeSelf,
                        "The alternate crossing must contain one visible opening and a clear landing.");
                    StandOn(gap); yield return Delay(.33);
                    Require(game.Stage==GameStage.Countdown && game.State.CurrentSample.Kind==SupportKind.Gap &&
                        !ring.enabled,"The crossing must allow time over the gap and clear the feet ring.");
                    StandOn(landing); yield return Frames(2);
                    Require(game.State.CurrentSample.Index==landing && ring.enabled,"The alternate landing must restore the feet ring.");
                }
                else
                {
                    Require(wave.BreakGroups.Count==1 && !wave.IsJump && wave.RowRoute.Count==0 &&
                        wave.BreakingTiles.All(Cracked),"All marked panes must cue together on a simultaneous wave.");
                    StandOn(wave.GuaranteedTile);
                }
                yield return Stage(GameStage.BetweenWaves,5);
                Require(game.State.WavesSurvived==1 && !game.State.IsIntact(start),"The crossing must score once.");
                MenuInput(new OVRInput.OVRControllerTouch()); Choose("Main menu");
                yield return Stage(GameStage.Welcome);
            }
            Record("Crossed two rows at level 6 and three at level 9, lost and retried level 6, froze a row in the menu and restored it after tracking recovery. Landed in an alternate direction at level 7 with only the support ring. All marked glass fell together at level 12.");
        }
    }
}
