using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Meta.XR.BuildingBlocks;
using Oculus.Interaction;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator VerifyTutorial(int first)
        {
            StandOn(first);
            Choose("Tutorial");
            yield return Stage(GameStage.PracticeStep);
            var stone = game.GetComponent<PracticeStone>();
            Require(stone && !stone.Body, "The movement lesson spawned the optional stone.");
            yield return Frames(3);
            Require(game.State.CurrentSample.Index == first, "The tutorial did not mark the first pane underfoot.");
            int neighbor = first % game.State.Width == 0 ? first + 1 : first - 1;
            var board = game.BoardView.Board;
            var startingPane = board.Find("Glass " + first % game.State.Width + ", " + first / game.State.Width);
            var fissures = startingPane.Find("Fissures").GetComponent<MeshRenderer>();
            var marker = board.Find("Occupied square").GetComponent<MeshRenderer>();
            Require(fissures.enabled && marker.enabled && game.State.ActiveWave == null,
                "Stepping practice did not show the normal crack warning and occupancy ring without a countdown.");
            StandOn(neighbor);
            yield return Wait(() => !startingPane.gameObject.activeSelf, "original practice pane shattering", 4);
            Require(game.Stage == GameStage.PracticeStep && marker.enabled && game.State.CurrentSample.Index == neighbor &&
                game.State.WavesSurvived == 0, "Taking the safe step did not preserve practice, the marker and the score.");
            yield return Stage(GameStage.Ready);
            Require(!stone.Body && game.State.ActiveWave == null && startingPane.gameObject.activeSelf && !fissures.enabled,
                "The movement lesson did not restore the original clear pane and return to Ready.");
            Record("Tutorial marked and cracked the starting pane. An untimed step to adjacent clear glass shattered the pane behind the player, then restored it and reached Ready without creating a stone.");

            Choose("Try the stone");
            yield return Stage(GameStage.PracticeStone);
            var body = stone.Body;
            var grab = body.GetComponent<Grabbable>();
            var block = body.GetComponentsInChildren<BuildingBlock>(true)
                .SingleOrDefault(marker => marker.BlockId == "f8766c17-aaf8-4b55-9431-de8deea83842");
            Require(grab && body.GetComponentInChildren<GrabInteractable>(true) && block &&
                block.InstallationRoutineCheckpoint?.InstallationRoutineId == "f8338144-8280-4ff6-9455-ae2bb9c20fe8",
                "The optional stone lost its installed Meta grab interaction.");
            VerifyStoneOcclusion(stone);
            int throws = 0;
            int pointer = 0;
            Vector3 velocity = Vector3.zero, applied = Vector3.zero;
            var samples = new Queue<(double time, double physicsTime, Vector3 grip, Vector3 stone)>();
            double lastPhysicsTime = -1;
            void OnThrow(Vector3 linear, Vector3 angular)
            {
                ++throws;
                velocity = linear;
                applied = body.linearVelocity;
                string path = string.Join(" | ", samples.Select(s =>
                    $"{s.time:F3}/{s.physicsTime:F3} grip {s.grip:F3} stone {s.stone:F3}"));
                Debug.Log($"[Floor Fracture verification] Stone release {throws}. Native {linear:F3}, applied {applied:F3}. Time/physics time and recent poses: {path}");
            }
            void OnPointer(PointerEvent evt)
            {
                if (evt.Type == PointerEventType.Select)
                {
                    pointer = evt.Identifier;
                    samples.Clear();
                    lastPhysicsTime = -1;
                }
                if (evt.Identifier != pointer || evt.Type != PointerEventType.Move || lastPhysicsTime == Time.fixedTimeAsDouble) return;
                // Grabbable signals Move before applying its transformer. Meta reads this
                // same object pose at the event. Keep one observation per physics tick.
                lastPhysicsTime = Time.fixedTimeAsDouble;
                if (samples.Count == 12) samples.Dequeue();
                samples.Enqueue((Time.timeAsDouble, lastPhysicsTime, evt.Pose.position, body.transform.position));
            }
            grab.VelocityThrow.WhenThrown += OnThrow;
            grab.WhenPointerEventRaised += OnPointer;
            try
            {
                yield return GrabPracticeStone(body, grab);
                // The SDK sends Cancel for an interrupted gesture. An intentional release sends Unselect.
                grab.ProcessPointerEvent(new PointerEvent(pointer, PointerEventType.Cancel, left));
                SetHands(left, false);
                yield return Wait(() => StoneReady(body), "cancelled stone returning to reach", 5);
                Require(throws == 0 && game.Stage == GameStage.PracticeStone, "Cancelling a grab counted as a throw.");

                int edge = (neighbor / game.State.Width) * game.State.Width + game.State.Width - 1;
                yield return WalkToStonePane(edge);
                yield return GrabPracticeStone(body, grab);
                Vector3 outside = new Vector3(game.State.Width * .5f * game.State.Pitch + .16f,
                    .85f, game.State.Center(edge).y);
                yield return LowerStoneTo(body, board.TransformPoint(outside));
                SetHands(left, false);
                yield return Wait(() => throws == 1 && StoneReady(body), "missed throw returning to reach", 6);
                Require(game.Stage == GameStage.PracticeStone, "An off-board throw changed game stage.");

                int far = first == 0 ? game.State.Count - 1 : 0;
                var pane = board.Find("Glass " + far % game.State.Width + ", " + far / game.State.Width).gameObject;
                var center = game.State.Center(far);
                yield return WalkToStonePane(far);
                yield return GrabPracticeStone(body, grab);
                yield return LowerStoneTo(body, board.TransformPoint(new Vector3(center.x, .85f, center.y)));
                SetHands(left, false);
                yield return Wait(() => throws == 2, "SDK release over the distant pane", 4);
                var lateral = Vector3.ProjectOnPlane(velocity, Vector3.up);
                Require(lateral.magnitude < .15f,
                    $"The straight-down drop gained {lateral.magnitude:F3} m/s sideways at release. SDK velocity was {velocity:F3}.");
                yield return Wait(() => !pane.activeSelf, "glass under the distant drop shattering", 5);
                yield return Wait(() => pane.activeSelf && StoneReady(body), "sandbox glass and stone returning", 6);

                center = game.State.Center(first);
                Vector3 restPoint = board.TransformPoint(new Vector3(center.x, .85f, center.y));
                yield return WalkToStonePane(first);
                yield return GrabPracticeStone(body, grab);
                yield return CarryStoneTo(restPoint - board.right * .22f, .65f);
                yield return CarryStoneTo(restPoint, .95f);
                yield return Delay(.4);
                Require(Vector3.Distance(body.position, restPoint) < .04f, "The carried stone did not come to rest above the pane.");
                SetHands(left, false);
                yield return Wait(() => throws == 3, "SDK release after carrying and holding still", 4);
                Require(applied.sqrMagnitude < .0001f, $"A stationary drop retained {applied:F3} m/s from the previous carry.");
                Record($"A carried stone held still for 0.4 s dropped at {applied.magnitude:F3} m/s. The SDK supplied {velocity.magnitude:F3} m/s before the stationary-drop check.");
                yield return Wait(() => !startingPane.gameObject.activeSelf, "stationary release landing on the original pane", 5);
                yield return Wait(() => startingPane.gameObject.activeSelf && StoneReady(body), "resting drop recovery", 6);

                int middle = game.State.Height / 2 * game.State.Width + game.State.Width / 2;
                center = game.State.Center(middle);
                Vector3 tossPoint = board.TransformPoint(new Vector3(center.x, .85f, center.y));
                yield return WalkToStonePane(middle);
                yield return GrabPracticeStone(body, grab);
                yield return CarryStoneTo(tossPoint - board.right * .20f, .65f);
                yield return CarryStoneTo(tossPoint, .45f);
                SetHands(left, false);
                yield return Wait(() => throws == 4, "native throw velocity on moving release", 4);
                Require(Vector3.Dot(velocity, board.right) > .08f && velocity.magnitude < 1.5f &&
                    Vector3.Distance(velocity, applied) < .01f,
                    $"The moving toss should keep the native impulse. SDK {velocity:F3}, applied {applied:F3}.");
                yield return Wait(() => board.GetComponentsInChildren<GlassTile>(true).Any(tile =>
                    tile.name.StartsWith("Glass ") && !tile.gameObject.activeSelf), "the moving throw breaking glass", 5);
                Require(game.Stage == GameStage.PracticeStone && game.State.WavesSurvived == 0, "The sandbox changed the game score or stage.");
                Record("In optional stone practice, a cancelled SDK grab returned without a throw, a miss returned to reach, a distant drop broke its actual pane, and a moving release carried native throw velocity into another pane.");
            }
            finally
            {
                grab.VelocityThrow.WhenThrown -= OnThrow;
                grab.WhenPointerEventRaised -= OnPointer;
            }

            Choose("Practice stepping");
            yield return Stage(GameStage.PracticeStep);
            Require(!body.gameObject.activeSelf, "The stone remained active when stepping resumed.");
            Choose("Try the stone");
            Choose("Go to game");
            yield return Stage(GameStage.Ready);
            Require(!body.gameObject.activeSelf && game.State.ActiveWave == null, "Leaving optional practice did not return to readiness.");
            StandOn(first);
        }

        private static IEnumerator GrabPracticeStone(Rigidbody body, Grabbable grab)
        {
            var pose = new Pose(body.position, game.BoardView.Board.rotation);
            SetHands(pose, false);
            yield return Frames(2);
            SetHands(pose, true);
            yield return Wait(() => grab.SelectingPointsCount == 1, "SDK near grab", 4);
        }

        private static IEnumerator WalkToStonePane(int index)
        {
            var board = game.BoardView.Board;
            Vector2 center = game.State.Center(index);
            // The player walks over while empty-handed and bends to a reachable drop height.
            Vector3 target = board.TransformPoint(new Vector3(center.x, 1.30f, center.y));
            var look = board.rotation * Quaternion.Euler(15f, 0f, 0f);
            yield return MovePracticePose(head.position, target, 1.2f, position => SetHead(new Pose(position, look)));
            yield return Wait(() => game.State.CurrentSample.Index == index, "tracked player arriving at practice pane", 4);
            Choose("Return stone");
        }

        private static IEnumerator CarryStoneTo(Vector3 target, float speed)
        {
            var rotation = left.rotation;
            return MovePracticePose(left.position, target, speed,
                position => SetHands(new Pose(position, rotation), true));
        }

        private static IEnumerator MovePracticePose(Vector3 start, Vector3 target, float speed, System.Action<Vector3> move)
        {
            // Use Meta's game clock. In a fast batch run dozens of frames can fall
            // within one physics tick, so counting rendered frames changes throw speed.
            double began = Time.timeAsDouble;
            float distance = Vector3.Distance(start, target);
            while (true)
            {
                float travel = (float)(Time.timeAsDouble - began) * speed;
                move(Vector3.MoveTowards(start, target, travel));
                yield return Frames(1);
                if (travel >= distance) yield break;
            }
        }

        private static IEnumerator LowerStoneTo(Rigidbody body, Vector3 position)
        {
            yield return CarryStoneTo(position + Vector3.up * .14f, .65f);
            double began = Time.fixedTimeAsDouble;
            yield return CarryStoneTo(position, .18f);
            Require(Time.fixedTimeAsDouble - began >= 20 * Time.fixedDeltaTime,
                "The controlled descent did not span twenty physics updates.");
            Require(Vector3.Distance(body.position, position) < .04f,
                $"The held stone missed its release pose. Expected {position:F3}, observed {body.position:F3}.");
        }

        private static bool StoneReady(Rigidbody body)
        {
            Vector3 forward = Vector3.ProjectOnPlane(game.Panel.Head.forward, Vector3.up).normalized;
            Vector3 reach = game.Panel.Head.position + forward * .34f + Vector3.down * .27f;
            return body.gameObject.activeInHierarchy && !body.useGravity && Vector3.Distance(body.position, reach) < .04f;
        }
    }
}
