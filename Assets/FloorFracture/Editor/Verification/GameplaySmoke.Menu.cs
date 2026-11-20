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
