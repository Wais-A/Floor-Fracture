using System.Collections;
using System.Linq;
using Meta.XR.MRUtilityKit;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator VerifyExitDuringWaveResult(WavePlan completed)
        {
            bool cleared = completed.Mode == WaveMode.LastTile;
            string label = cleared ? "LEVEL CLEAR" : "SAFE";
            int score = game.State.WavesSurvived;
            int level = game.Level;
            int nextWave = cleared ? 1 : game.WaveNumber;
            bool[] remaining = Enumerable.Range(0, game.State.Count).Select(game.State.IsIntact).ToArray();
            var room = MRUK.Instance.GetCurrentRoom();
            var board = game.BoardView.Board;
            var chamber = GameObject.Find("Fall chamber");
            Require(chamber && chamber.GetComponentsInChildren<Renderer>().Any(r => r.enabled),
                label + " needs a visible open chamber before the boundary check.");
            VerifyFloorVisibility(false);

            // A freshly broken interior pane is still inside the board. Walking across
            // it during the result display should neither start another wave nor pause.
            StandOn(completed.StartingTile);
            yield return Frames(2);
            Require(game.Stage == GameStage.BetweenWaves && game.State.CurrentSample.Kind == SupportKind.Gap,
                label + " should keep showing while the stance crosses an interior opening.");

            float x = game.State.Width * game.State.Pitch * .5f + .04f;
            float z = game.State.Height * game.State.Pitch * .5f + .04f;
            var edges = new[] { new Vector2(x,0), new Vector2(-x,0), new Vector2(0,z), new Vector2(0,-z) };
            var outside = edges.First(p => room.IsPositionInRoom(board.TransformPoint(GlassGeometry.Ground(p,1.6f)), false));
            StandAt(outside);
            yield return Frames(2);
            Require(game.Panel.Tracked && game.Room.IsRoomLocalized && game.State.IndexAt(outside) < 0,
                "The boundary check must retain tracking inside the scanned room.");
            bool glassVisible = game.BoardView.GetComponentsInChildren<Renderer>().Any(r => r.enabled && r.gameObject.activeInHierarchy);
            bool chamberVisible = chamber.activeInHierarchy && chamber.GetComponentsInChildren<Renderer>().Any(r => r.enabled);
            Require(game.Stage == GameStage.Paused && !glassVisible && !chamberVisible && FloorSurface.HideMesh,
                $"{label} boundary exit left stage {game.Stage}, glass {glassVisible}, chamber {chamberVisible}, brick {!FloorSurface.HideMesh}.");
            VerifyFloorVisibility(false);
            int expectedLevel = cleared ? level + 1 : level;
            Require(game.State.ActiveWave == null && game.State.WavesSurvived == score && game.Level == level &&
                Enumerable.Range(0, game.State.Count).All(i => game.State.IsIntact(i) == remaining[i]),
                label + " pause changed the completed score, new level or surviving panes.");
            yield return Delay(cleared ? 1.85 : 1.35);
            Require(game.Stage == GameStage.Paused && game.Level == level && game.State.WavesSurvived == score,
                label + " advanced again at the canceled result deadline.");

            // A localized player may reveal the board to find glass. Starting the
            // actual timer still requires a supported stance.
            Choose("Show board");
            Require(game.Stage == GameStage.Ready && game.BoardView.gameObject.activeInHierarchy,
                label + " did not reveal the board for a localized player outside it.");
            VerifyFloorVisibility(false);
            Choose("Start wave");
            Require(game.Stage == GameStage.Ready && game.State.ActiveWave == null && game.State.WavesSurvived == score,
                label + " started a wave from outside the glass.");

            StandOn(completed.GuaranteedTile);
            yield return Frames(2);
            double requested = game.GameplayTime;
            Choose("Start wave");
            var resumed = game.State.ActiveWave;
            Require(game.Stage == GameStage.Countdown && resumed != null && resumed.StartsAt >= requested &&
                resumed.Level == expectedLevel && resumed.Number == nextWave && resumed.StartingTile == completed.GuaranteedTile &&
                resumed.Deadline >= requested + resumed.Duration && game.State.WavesSurvived == score,
                label + " did not resume from supported glass with one level advance and a fresh full countdown.");
            Record($"{label} paused immediately on leaving the glass inside the tracked room. Glass and chamber hid; score {score} survived. The off-board menu revealed the board and rejected Start until supported. Level {expectedLevel}, wave {nextWave} received a fresh {resumed.Duration:F1} seconds.");
            if (cleared)
            {
                // Return the next level to Ready for the rest of the playthrough.
                game.gameObject.SendMessage("OnApplicationPause", true);
                game.gameObject.SendMessage("OnApplicationPause", false);
                Choose("Show board");
                Require(game.Stage == GameStage.Ready && game.Level == expectedLevel && game.WaveNumber == 1 &&
                    game.State.WavesSurvived == score, "Pausing the new level counted Last Tile twice.");
            }
        }
    }
}
