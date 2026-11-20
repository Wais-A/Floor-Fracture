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
        private static IEnumerator VerifySettingsPreviewAndVolume()
        {
            var oldHead = head;
            var canvas = game.Panel.transform.Find(GamePanel.CanvasName);
            var preview = canvas.Find("Title/Warning preview");
            Require(preview && preview.GetComponent<GlassTile>() && !preview.GetComponentInChildren<Collider>(),
                "Settings should show a real glass sample that cannot intercept selections.");
            var ordinary = game.BoardView.Board.Find("Glass 0, 0");
            var reference = game.BoardView.Board.Find("Sliding pane").GetComponent<GlassTile>();
            var cracks = preview.Find("Fissures").GetComponent<MeshRenderer>();
            var eye = game.Rig.centerEyeAnchor.GetComponent<Camera>();
            var sound = game.GetComponent<GameSound>();
            int clicks = 0;
            float clickVolume = -1f;
            void HearClick(float volume) { ++clicks; clickVolume = volume; }
            sound.EditorMenuClick += HearClick;
            try
            {
                var corner = game.State.Center(game.State.Count - 1);
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
                    reference.ShowWarning(true, Now, gentle);
                    foreach (string name in new[] { "Smoky pane", "Rim", "Fissures" })
                    {
                        var sample = preview.Find(name);
                        var properties = new MaterialPropertyBlock();
                        sample.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                        var tint = properties.GetColor("_Color");
                        reference.transform.Find(name).GetComponent<MeshRenderer>().GetPropertyBlock(properties);
                        Require(sample.GetComponent<MeshFilter>().sharedMesh == ordinary.Find(name).GetComponent<MeshFilter>().sharedMesh &&
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
                StandOn(game.State.Count / 2);
                Choose("Start wave");
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
                Choose("Restart");
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
            Record("The native rig's provisional floor-height pose did not strand the menu below the eye. A page that began below the viewport appeared at eye height. The SDK display event recalled Settings, Paused, and Results at 56 cm. The menu stayed fixed during ordinary head movement, stayed hidden during play and waited for tracking after suspension. The glass, floor anchor and camera rig kept their poses.");
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
            Vector3 offset = forward * .56f + Vector3.down * .07f;
            var expected = new Pose(head.position + offset, Quaternion.LookRotation(offset, Vector3.up));
            Require(game.Panel.Visible && HasPose(canvas, expected), "The open page did not face the eye at the reachable menu distance.");
        }

        private static Pose WorldPose(Transform item) => new Pose(item.position, item.rotation);

        private static bool HasPose(Transform item, Pose pose) =>
            Vector3.Distance(item.position, pose.position) < .002f && Quaternion.Angle(item.rotation, pose.rotation) < .15f;
    }
}
