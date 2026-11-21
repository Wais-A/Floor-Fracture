using System.Collections;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        // Feed Meta's public button-state buffers through the same GetDown path used on Quest.
        private static void MenuInput(OVRInput.OVRControllerBase source, bool alreadyHeld=false)
        {
            var devices=OVRInput.controllers;
            try
            {
                source.previousState.Buttons=alreadyHeld ? (uint)OVRInput.RawButton.Start : 0;
                source.currentState.Buttons=(uint)OVRInput.RawButton.Start;
                OVRInput.controllers=new System.Collections.Generic.List<OVRInput.OVRControllerBase> { source };
                game.SendMessage("Update");
            }
            finally { OVRInput.controllers=devices; }
        }

        private static IEnumerator VerifyLevelPicker()
        {
            var fit = game.Room.Placement;
            yield return RayChoose(2, false);
            Require(game.Stage == GameStage.LevelSelect && !game.Panel.EditorChoose("Level 2"),
                "A fresh player could start a locked level.");
            var locked = game.Panel.transform.Find(GamePanel.CanvasName + "/Choice 2").GetComponent<Button>();
            Require(!locked.IsInteractable() && locked.GetComponentInChildren<Text>().text == "Level 2 · Locked",
                "The next level must be visible and disabled.");
            Choose("Back");
            Choose("Development"); Choose("Later levels"); Choose("Level 12"); Choose("Main menu");
            Require(!PlayerPrefs.HasKey(ProgressKey), "A development shortcut unlocked levels.");

            Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            while (game.Level == 1)
            {
                if (game.Stage == GameStage.Paused)
                {
                    Choose("Show board");
                    if (game.Level != 1) break;
                    Choose("Start wave");
                    yield return Stage(GameStage.Countdown);
                }
                Require(game.Stage == GameStage.Countdown && game.State.ActiveWave != null,
                    "Expected an active wave on the way to the first unlock, got " + game.Stage);
                StandOn(game.State.ActiveWave.GuaranteedTile);
                yield return Stage(GameStage.BetweenWaves, 10);
                yield return Wait(() => game.Stage != GameStage.BetweenWaves, "next wave or Ready", 5);
            }
            Require(game.Level == 2 && game.Stage == GameStage.Ready && PlayerPrefs.GetInt(ProgressKey) == 2,
                "Level 2 was not saved on reaching Ready before its first wave.");
            Choose("Main menu"); Choose("Choose level"); Choose("Level 2");
            StandOn(MiddlePane); Choose("Start wave");
            yield return Stage(GameStage.Fall, 12);
            game.Ending.Skip(); yield return Stage(GameStage.Results);
            Choose("Choose level");
            Require(!game.Panel.EditorChoose("Level 3"), "Losing unlocked the next level.");
            MenuInput(new OVRInput.OVRControllerTouch());
            yield return Stage(GameStage.Results);
            Choose("Retry Level 2");
            Require(game.Stage == GameStage.Ready && game.Level == 2 && game.WaveNumber == 1 &&
                game.State.WavesSurvived == 0 && game.State.ActiveWave == null && PlayablePanes.All(game.State.IsIntact) &&
                game.Room.Placement.Equals(fit), "Retry did not start the lost level on fresh glass in the same room.");
            Choose("Main menu"); Choose("Choose level"); Choose("Level 1");
            Require(game.Level == 1 && PlayerPrefs.GetInt(ProgressKey) == 2, "Replaying lowered the saved unlock.");
            Choose("Main menu");

            PlayerPrefs.SetInt(ProgressKey, 13);
            try
            {
                Choose("Choose level");
                Require(!game.Panel.EditorChoose("Level 14") && !game.Panel.EditorChoose("Level 10"),
                    "The picker failed to open on the highest unlocked page.");
                Choose("Earlier levels"); Choose("Later levels"); Choose("Level 13");
                Require(game.Level == 13 && game.Stage == GameStage.Ready && game.WaveNumber == 1,
                    "Level selection was capped at 12.");
                Choose("Main menu");
            }
            finally { PlayerPrefs.SetInt(ProgressKey, 2); PlayerPrefs.Save(); }
            Record("Reached Level 2, left before its first wave, selected it, lost it, and retried on fresh glass. Earlier levels, disabled locks, development shortcuts and paging to Level 13 passed.");
        }

        private static IEnumerator VerifyMenuPauseAndQuit()
        {
            var touch=new OVRInput.OVRControllerTouch();
            var hands=new OVRInput.OVRControllerHands();
            var fit=game.Room.Placement;
            Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
            yield return Stage(GameStage.Countdown);
            yield return Delay(.24);
            var wave=game.State.ActiveWave;
            var warned=game.BoardView.Board.Find($"Glass {wave.StartingTile%game.State.Width}, {wave.StartingTile/game.State.Width}/Fissures").GetComponent<Renderer>();
            MenuInput(touch);
            yield return Stage(GameStage.Paused);
            MenuInput(touch,true);
            Require(game.Stage==GameStage.Paused && game.Panel.Visible,"Holding Menu dismissed the pause screen.");
            yield return Delay(wave.Duration+.15);
            MenuInput(hands);
            yield return Stage(GameStage.Countdown);
            yield return Frames(2);
            Require(game.State.ActiveWave==wave && !game.Panel.Visible,
                "The controller/hand pause consumed time or replaced the active wave.");
            Require(warned.enabled && wave.ShowsWarnings(game.GameplayTime),
                "Pausing during the reveal consumed the time left to show the cracks.");
            yield return Wait(()=>game.GameplayTime>wave.RevealEndsAt+.10,"warning to clear",3);
            MenuInput(hands);
            yield return Delay(.4);
            MenuInput(touch);
            yield return Frames(2);
            Require(game.State.ActiveWave==wave && !warned.enabled,
                "Resuming during recall replayed the reveal.");
            MenuInput(hands);
            yield return Stage(GameStage.Paused);
            yield return RayChoose(2,false);
            yield return Stage(GameStage.Welcome);

            Choose("Development"); game.Ending.SetDevelopmentEnding(0); Choose("Preview loss");
            yield return Stage(GameStage.Fall); yield return Delay(1.05);
            var walls=GameObject.Find("Fall chamber").transform.Find("Shaft walls");
            MenuInput(hands);
            var paused=walls.position;
            yield return Delay(.35);
            Require(game.Stage==GameStage.Paused && !walls.gameObject.activeInHierarchy &&
                walls.position==paused,"Hand Menu failed to freeze and conceal the fall.");
            yield return RayChoose(1,true);
            yield return Stage(GameStage.Fall); yield return Frames(3);
            Require(walls.gameObject.activeInHierarchy && walls.position.y>=paused.y && walls.position.y-paused.y<1.5f,
                "Resuming from the panel jumped ahead in the fall.");
            game.Ending.Skip(); yield return Stage(GameStage.Results);
            yield return RayChoose(3,false);
            yield return Stage(GameStage.Welcome);
            Require(game.Panel.Visible && !game.Ending.IsPlaying && game.BoardView.gameObject.activeInHierarchy &&
                game.State.ActiveWave==null && game.State.WavesSurvived==0 && game.Level==1 &&
                game.Room.Placement.Equals(fit),"Results Quit did not restore the main menu on the same floor.");
            game.Ending.SetDevelopmentEnding(-1);
            Choose("Start"); StandOn(MiddlePane); Choose("Start wave"); yield return Stage(GameStage.Countdown);
            MenuInput(touch); Choose("Main menu"); yield return Stage(GameStage.Welcome);
            Record("Meta Touch and hand Menu edges paused and resumed the same countdown. Holding Menu kept it paused. The hand menu froze a fall, and the panel resumed it. Results Quit returned to Welcome and started a fresh playable wave on the same floor.");
        }

        private static IEnumerator VerifyStartCount()
        {
            var touch = new OVRInput.OVRControllerTouch();
            Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
            var label = game.Panel.Head.Find("Start count/Count").GetComponent<Text>();
            bool Showing(string value) => label.isActiveAndEnabled && label.text == value;
            Require(game.Stage == GameStage.CountIn && Showing("3") && game.State.ActiveWave == null &&
                !game.Panel.EditorChoose("Start wave"), "Start should show 3 once and protect the glass.");
            yield return Wait(() => Showing("2"), "second count", 2);
            MenuInput(touch);
            yield return Delay(1.1);
            Require(!label.isActiveAndEnabled && game.State.ActiveWave == null, "Pause exposed or consumed the count.");
            Choose("Resume");
            Require(Showing("2"), "Resume skipped the frozen numeral.");
            yield return Wait(() => Showing("1"), "last count", 2);
            Require(game.State.ActiveWave == null && game.State.WavesSurvived == 0, "A wave began before GO.");
            StandAt(new Vector2(100, 100));
            yield return Stage(GameStage.Ready);
            Require(!label.isActiveAndEnabled && game.State.ActiveWave == null, "Leaving the board at GO started a wave.");

            StandOn(MiddlePane); Choose("Start wave");
            game.SendMessage("OnApplicationPause", true);
            game.SendMessage("OnApplicationPause", false);
            Choose("Show board");
            Require(game.Stage == GameStage.Ready && !label.isActiveAndEnabled, "Tracking recovery retained the old count.");
            var sound = game.GetComponent<GameSound>();
            int knocks = sound.EditorStartKnocks;
            int goCues = sound.EditorGoCues;
            double pressed = game.GameplayTime;
            Choose("Start wave");
            int other = PlayablePanes.First(tile => game.State.Neighbors(MiddlePane, tile));
            StandOn(other);
            yield return Stage(GameStage.Countdown);
            var wave = game.State.ActiveWave;
            Require(Showing("GO!") && wave.StartingTile == other && wave.StartsAt >= pressed + 3 &&
                wave.Deadline == wave.StartsAt + wave.Duration, "GO must start a full wave on the current pane.");
            StandOn(wave.GuaranteedTile);
            yield return Wait(() => !label.isActiveAndEnabled, "GO fade", 1);
            yield return Stage(GameStage.BetweenWaves);
            yield return Stage(GameStage.Countdown, 2.5);
            Require(!label.isActiveAndEnabled && game.WaveNumber == 2, "An automatic switch played a new count-in.");
            Require(sound.EditorStartKnocks == knocks + 3 && sound.EditorGoCues == goCues + 1,
                "Only the three start knocks and GO should sound across a manual and automatic wave.");
            Require(!game.transform.Find("Wall scoreboard") && !game.transform.Find("Countdown tick"),
                "A gameplay clock or its ticking voice was created.");
            MenuInput(touch); Choose("Main menu");
            Record("Manual starts showed 3, 2, 1 and GO at the headset. Pause froze the count, tracking reset it, leaving the board canceled it, and changing panes selected the new starting tile. The next switch started directly.");
        }

        private static IEnumerator VerifySettingsPreviewAndVolume()
        {
            var oldHead = head;
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName);
            var preview = canvas.Find("Title/Warning preview");
            Require(preview && preview.GetComponent<GlassTile>() && !preview.GetComponentInChildren<Collider>(),
                "Settings should show a real glass sample that cannot intercept selections.");
            var ordinary = game.BoardView.Board.Find($"Glass {CornerPane % game.State.Width}, {CornerPane / game.State.Width}");
            var reference = game.BoardView.Board.Find("Sliding pane").GetComponent<GlassTile>();
            var intactRim = ordinary.Find("Rim").GetComponent<MeshFilter>().sharedMesh;
            var sliderRim = reference.transform.Find("Rim").GetComponent<MeshFilter>();
            Require(intactRim == GlassGeometry.SafeRim && sliderRim.sharedMesh == intactRim && intactRim != GlassGeometry.Rim &&
                intactRim.bounds == GlassGeometry.Rim.bounds && intactRim.vertexCount == GlassGeometry.Rim.vertexCount &&
                intactRim.triangles.Length == GlassGeometry.Rim.triangles.Length && intactRim.subMeshCount == 1 &&
                intactRim.colors.Any(c => c == Color.white) && intactRim.colors.Any(c => Mathf.Max(c.r, c.g, c.b) < .06f),
                "Intact stationary and moving glass must share the two-tone rim within the ordinary warning rim's mesh budget and bounds.");
            var cracks = preview.Find("Fissures").GetComponent<MeshRenderer>();
            var eye = game.Rig.centerEyeAnchor.GetComponent<Camera>();
            var sound = game.GetComponent<GameSound>();
            int clicks = 0;
            float clickVolume = -1f;
            void HearClick(float volume) { ++clicks; clickVolume = volume; }
            sound.EditorMenuClick += HearClick;
            try
            {
                var corner = game.State.Center(PlayablePanes.Last());
                foreach (var stance in new[] { corner, corner + Vector2.one * (game.State.Pitch * 2) })
                {
                    StandAt(stance);
                    game.Panel.Recenter();
                    yield return Frames(2);
                    Require(preview.gameObject.activeInHierarchy && cracks.enabled &&
                        game.BoardView.GetComponentsInChildren<GlassTile>().All(tile => !tile.transform.Find("Fissures").GetComponent<MeshRenderer>().enabled),
                        "Settings lit a board pane or failed to show the sample.");
                    var leftEdge = preview.TransformPoint(Vector3.left * GlassGeometry.Half);
                    var rightEdge = preview.TransformPoint(Vector3.right * GlassGeometry.Half);
                    foreach (var point in new[] { leftEdge, rightEdge })
                    {
                        var viewport = eye.WorldToViewportPoint(point);
                        Require(viewport.z > 0 && viewport.x > 0 && viewport.x < 1 && viewport.y > 0 && viewport.y < 1,
                            "The warning sample fell outside the menu view.");
                    }
                    Require(Vector3.Angle(leftEdge - eye.transform.position, rightEdge - eye.transform.position) > 6f,
                        "The menu warning sample became too small to read.");
                    var instructions = (RectTransform)canvas.Find("Instructions");
                    Require(instructions.InverseTransformPoint(leftEdge).x > instructions.rect.xMax,
                        "The warning sample overlapped the Settings instructions.");
                    Capture(game.State.IndexAt(stance) < 0 ? "settings-outside-board" : "settings-on-board", SmokeView.CenterEye);
                }
                for (int i = 0; i < 2; ++i)
                {
                    bool oldGentle = PlayerPrefs.GetInt("ff.gentle") != 0;
                    Choose("Warning");
                    bool gentle = PlayerPrefs.GetInt("ff.gentle") != 0;
                    Require(gentle != oldGentle, "Warning setting did not persist.");
                    reference.ShowWarning(true, 0, gentle);
                    foreach (string name in new[] { "Smoky pane", "Rim", "Fissures" })
                    {
                        var sample = preview.Find(name);
                        var properties = new MaterialPropertyBlock();
                        sample.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                        var tint = properties.GetColor("_Color");
                        var warningPart = reference.transform.Find(name);
                        warningPart.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                        Require(sample.GetComponent<MeshFilter>().sharedMesh == warningPart.GetComponent<MeshFilter>().sharedMesh &&
                            tint == properties.GetColor("_Color"), "Settings did not show the selected gameplay warning on " + name + ".");
                    }
                }
                if (PlayerPrefs.GetInt("ff.gentle") != 0) Choose("Warning");
                int level = PlayerPrefs.GetInt("ff.volume",3);
                for (int i = 0; i < 5; ++i)
                {
                    int before = clicks;
                    level = (level + 1) % 5;
                    if (i == 0) yield return RayChoose(3, true);
                    else Choose("Volume");
                    Require(PlayerPrefs.GetInt("ff.volume") == level && clicks == before + 1 && Mathf.Approximately(clickVolume, level / 4f),
                        "A volume choice must play exactly one click at the newly selected master volume, including mute.");
                }
                Choose("Back");
                Require(game.Stage == GameStage.Welcome && !preview.gameObject.activeInHierarchy,
                    "The Settings sample remained visible on Welcome.");
            }
            finally
            {
                sound.EditorMenuClick -= HearClick;
                reference.ShowWarning(false, Now, false);
                SetHead(oldHead);
                game.Panel.Recenter();
            }
            Require(sliderRim.sharedMesh == intactRim, "Clearing a moving pane's warning must restore its shared intact rim.");
            Record("The Settings sample remained visible on and outside the board. It displayed the same pane, rim and fissures at both warning intensities. Each of the five volume selections played a single click at the newly chosen gain, including silent mute.");
        }

        private sealed class FixtureDisplay : OVRDisplay
        {
            internal void NotifyRecenter()
            {
                // Exercise the installed SDK's RecenteredPose event without resetting an XR origin.
                recenterRequested = true;
                recenterRequestedFrameCount = Time.frameCount - 1;
                Update();
            }
        }

        private static IEnumerator VerifyMenuRecenter()
        {
            var initialHead = head;
            var display = new FixtureDisplay();
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName);
            game.Panel.EditorUseRecenterDisplay(display);
            try
            {
                var origin = game.Rig.trackingSpace.position;
                SetHead(new Pose(origin, Quaternion.identity));
                game.Panel.Recenter();
                yield return Frames(2);
                SetHead(new Pose(origin + Vector3.up * 1.70f, Quaternion.identity));
                yield return Frames(3);
                RequireCenteredMenu(canvas, 0f);
                // A localized room can replace the loading page after the initial height changes.
                canvas.position = origin + Vector3.forward * .56f;
                Choose("Settings");
                RequireCenteredMenu(canvas, 0f);
                yield return VerifyRecenterOnPage(display, canvas, GameStage.Settings, 135f);
                Choose("Back");
                Choose("Start");
                StandOn(MiddlePane);
                Choose("Start wave");
                yield return Stage(GameStage.Countdown);
                Require(game.Stage == GameStage.Countdown && !game.Panel.Visible, "The countdown should hide the menu.");
                var hiddenPose = WorldPose(canvas);
                display.NotifyRecenter();
                yield return Frames(2);
                Require(!game.Panel.Visible && HasPose(canvas, hiddenPose), "A recenter moved or reopened the hidden menu.");

                game.gameObject.SendMessage("OnApplicationPause", true);
                Require(game.Stage == GameStage.Paused, "Suspending a wave should show the Paused page.");
                var pausedPose = WorldPose(canvas);
                SetHead(new Pose(head.position, Quaternion.Euler(20f, -65f, 0f)));
                display.NotifyRecenter();
                yield return Frames(2);
                Require(HasPose(canvas, pausedPose), "The menu used an untracked pose during suspension.");
                game.gameObject.SendMessage("OnApplicationPause", false);
                yield return Frames(3);
                RequireCenteredMenu(canvas, -65f);
                yield return VerifyRecenterOnPage(display, canvas, GameStage.Paused, 52f);

                Choose("Show board");
                Choose("Main menu");
                Choose("Development");
                game.Ending.SetDevelopmentEnding(0);
                Choose("Preview loss");
                game.Ending.Skip();
                yield return Stage(GameStage.Results);
                yield return VerifyRecenterOnPage(display, canvas, GameStage.Results, -112f, 90f);
                Choose("Retry Level");
                Choose("Main menu");
                yield return Stage(GameStage.Welcome);
            }
            finally
            {
                game.Ending.SetDevelopmentEnding(-1);
                game.Panel.EditorUseRecenterDisplay(OVRManager.display);
                SetHead(initialHead);
                game.Panel.Recenter();
            }
            Record("The menu recovered from a provisional eye pose and an offscreen position. The SDK display event recalled Settings, Paused and Results 56 cm forward and 24 cm below the tracked eye. Ordinary head turns, hidden gameplay, suspended tracking and the anchored glass passed their checks.");
        }

        private static IEnumerator VerifyRecenterOnPage(FixtureDisplay display, Transform canvas, GameStage page,
            float yaw, float pitch = 15f)
        {
            yield return Stage(page);
            yield return Frames(3);
            game.Panel.EditorUseRecenterDisplay(display);
            var original = WorldPose(canvas);
            var floor = MRUK.Instance.Rooms.Single().FloorAnchors.First().transform;
            var board = game.BoardView.Board;
            var floorPose = WorldPose(floor);
            var boardPose = WorldPose(board);
            var rigPose = WorldPose(game.Rig.transform);
            var content = canvas.GetComponentsInChildren<Text>().Select(label => label.text).ToArray();
            var events = EventSystem.current;
            var priorSelection = events ? events.currentSelectedGameObject : null;
            var choice = canvas.GetComponentInChildren<Button>().gameObject;
            if (events) events.SetSelectedGameObject(choice);
            SetHead(new Pose(head.position, Quaternion.Euler(0f, yaw - 70f, 0f)));
            yield return Frames(2);
            Require(HasPose(canvas, original), page + " followed a head turn before a recenter.");
            display.NotifyRecenter();
            // The camera pose can still change between OVRManager.Update and MRUK's world lock.
            SetHead(new Pose(head.position + new Vector3(.08f, .03f, -.05f), Quaternion.Euler(pitch, yaw, 0f)));
            Require(HasPose(canvas, original), page + " sampled the eye before the tracking update.");
            yield return Frames(3);
            RequireCenteredMenu(canvas, yaw);
            Require(game.Stage == page && content.SequenceEqual(canvas.GetComponentsInChildren<Text>().Select(label => label.text)),
                page + " lost its current content or options on recenter.");
            Require(!events || events.currentSelectedGameObject == choice, page + " lost the selected button on recenter.");
            if (events) events.SetSelectedGameObject(priorSelection);
            Require(HasPose(floor, floorPose) && HasPose(board, boardPose) && HasPose(game.Rig.transform, rigPose),
                "Recentering " + page + " displaced the floor, glass, or camera rig.");
            var placed = WorldPose(canvas);
            SetHead(new Pose(head.position, Quaternion.Euler(0f, yaw + 25f, 0f)));
            yield return Frames(2);
            Require(HasPose(canvas, placed), page + " kept following the eye after the event was consumed.");
        }

        private static void RequireCenteredMenu(Transform canvas, float yaw)
        {
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Vector3 offset = forward * .56f + Vector3.down * .24f;
            var expected = new Pose(head.position + offset, Quaternion.LookRotation(offset, Vector3.up));
            Require(game.Panel.Visible && HasPose(canvas, expected),
                $"Menu recenter failed at {game.Stage}. Expected {expected.position:F3} at yaw {yaw:F1}, " +
                $"observed {canvas.position:F3} at yaw {canvas.eulerAngles.y:F1}, visible={game.Panel.Visible}.");
        }

        private static Pose WorldPose(Transform item) => new Pose(item.position, item.rotation);

        private static bool HasPose(Transform item, Pose pose) =>
            Vector3.Distance(item.position, pose.position) < .002f && Quaternion.Angle(item.rotation, pose.rotation) < .15f;
    }
}
