using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static string SmokeBagKey => SessionState.GetString(Key + "bag", "");

        private static void InitializeTrapVerification()
        {
            string preference = "ff.verification.waveEndingBag." + Guid.NewGuid().ToString("N");
            SessionState.SetString(Key + "bag", preference);
            game.Ending.EditorUseShufflePreferences(preference, 379);
        }

        private static void RestoreTrapVerification()
        {
            string key = SmokeBagKey;
            if (key.Length == 0) return;
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.DeleteKey(key + ".rotation");
            SessionState.EraseString(Key + "bag");
        }

        private static int VisibleTrap()
        {
            var chamber = GameObject.Find("Fall chamber");
            Require(chamber && !game.Ending.IsPlaying, "Expected an open chamber before the fall.");
            var surface = chamber.transform.Find("Hazard surface").GetComponent<MeshRenderer>();
            var properties = new MaterialPropertyBlock();
            surface.GetPropertyBlock(properties);
            Require(surface.enabled && game.BoardView.Board.Find("Opening stencil").GetComponent<Renderer>().enabled,
                "The assigned destination must be visible through the open glass.");
            return Mathf.RoundToInt(properties.GetFloat("_Theme"));
        }

        private static IEnumerator VerifyTrapRotation()
        {
            const int seed = 379;
            string key = SmokeBagKey + ".rotation";
            game.Ending.EditorUseShufflePreferences(key,seed);
            try
            {
                var firsts = new HashSet<int>();
                for (int candidate = 0; candidate < 128; candidate++)
                {
                    game.Ending.SetShuffleSeed(candidate);
                    firsts.Add(game.Ending.EditorPeekNextLevelEnding);
                }
                Require(firsts.Count == EndingNames.Length, "A fresh installation cannot open with every trap.");
                game.Ending.SetShuffleSeed(seed);
                int forcedPreview = (game.Ending.EditorPeekNextLevelEnding + 1) % EndingNames.Length;
                game.Ending.SetDevelopmentEnding(forcedPreview);
                Choose("Development"); Choose("Preview loss");
                yield return Stage(GameStage.Fall);
                Require(game.Ending.LastEndingName == EndingNames[forcedPreview] && !PlayerPrefs.HasKey(key),
                    "A development preview drew from the real level bag or ignored its selection.");
                game.Ending.Skip(); yield return Stage(GameStage.Results);
                Choose("Quit to main menu");

                var order = new List<int>();
                Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
                int waves = 0;
                for (int level = 1; level <= EndingNames.Length; level++)
                {
                    yield return Stage(GameStage.Countdown);
                    int slot = game.Ending.CurrentLevelEnding;
                    string saved = PlayerPrefs.GetString(key);
                    Require(game.Level == level && slot >= 0 && slot < EndingNames.Length &&
                        !order.Contains(slot) && saved.Length > 0, "A level repeated a trap or missed its draw.");
                    order.Add(slot);
                    if (level == 1)
                    {
                        MenuInput(new OVRInput.OVRControllerTouch()); Choose("Resume");
                        Require(game.Stage == GameStage.Countdown && game.Ending.CurrentLevelEnding == slot &&
                            PlayerPrefs.GetString(key) == saved, "Manual pause consumed a trap.");
                        game.gameObject.SendMessage("OnApplicationPause",true);
                        yield return Stage(GameStage.Paused);
                        game.gameObject.SendMessage("OnApplicationPause",false);
                        Choose("Show board"); Choose("Start wave");
                        yield return Stage(GameStage.Countdown);
                    }
                    int firstWave = waves;
                    while (game.Level == level)
                    {
                        Require(game.Stage == GameStage.Countdown && game.Ending.CurrentLevelEnding == slot &&
                            PlayerPrefs.GetString(key) == saved, "A new wave or tracking retry changed the level's trap.");
                        if (waves > firstWave)
                            Require(VisibleTrap() == slot, "An existing opening changed destination between waves.");
                        StandOn(game.State.ActiveWave.GuaranteedTile);
                        yield return Stage(GameStage.BetweenWaves, 10);
                        yield return Frames(1);
                        waves++;
                        Require(game.State.WavesSurvived == waves && VisibleTrap() == slot &&
                            game.Ending.CurrentLevelEnding == slot && PlayerPrefs.GetString(key) == saved,
                            "Newly broken glass or the level-clear interval changed the destination.");
                        yield return Wait(() => game.Stage != GameStage.BetweenWaves, "following wave or level", 5);
                    }
                    Require(game.Stage == GameStage.Ready && game.Level == level + 1 && waves - firstWave > 1,
                        "The fitted fixture did not complete multiple waves in this level.");
                    if (level == 3)
                    {
                        int next = game.Ending.EditorPeekNextLevelEnding;
                        game.Ending.EditorUseShufflePreferences(key, -207);
                        Require(game.Ending.EditorPeekNextLevelEnding == next,
                            "Reloading a partial bag changed the next level's trap.");
                    }
                    if (level < EndingNames.Length) Choose("Start wave");
                }

                // Reload the completely exhausted bag with a seed that would otherwise
                // put its previous final trap first. The persisted boundary must prevent it.
                int previous = order[order.Count - 1];
                int repeatSeed = 0;
                for (; repeatSeed < 128; repeatSeed++)
                {
                    game.Ending.SetShuffleSeed(repeatSeed);
                    if (game.Ending.EditorPeekNextLevelEnding == previous) break;
                }
                game.Ending.EditorUseShufflePreferences(key,repeatSeed);
                Choose("Start wave");
                yield return Stage(GameStage.Countdown);
                Require(game.Stage == GameStage.Countdown && game.Ending.CurrentLevelEnding != previous,
                    "A new session immediately repeated the final trap from the previous bag.");
                MenuInput(new OVRInput.OVRControllerTouch()); Choose("Main menu");

                // Replay the six draws in fresh runs and lose on the second wave of each.
                game.Ending.SetShuffleSeed(seed);
                for (int i = 0; i < order.Count; i++)
                {
                    Choose("Start"); StandOn(MiddlePane); Choose("Start wave");
                    yield return Stage(GameStage.Countdown);
                    StandOn(game.State.ActiveWave.GuaranteedTile);
                    yield return Stage(GameStage.BetweenWaves, 10);
                    yield return Stage(GameStage.Countdown, 5);
                    yield return Frames(2);
                    string saved = PlayerPrefs.GetString(key);
                    Require(game.Stage == GameStage.Countdown && VisibleTrap() == order[i] &&
                        game.Ending.CurrentLevelEnding == order[i],
                        "The opened pane showed a different trap from the surviving run.");
                    yield return Stage(GameStage.Fall, 10);
                    Require(game.State.LastLoss != WaveLossReason.None && game.State.WavesSurvived == 1 &&
                        game.Ending.LastEndingName == EndingNames[order[i]] && PlayerPrefs.GetString(key) == saved,
                        "A confirmed loss switched away from the trap shown through the hole or consumed another draw.");
                    game.Ending.Skip(); yield return Stage(GameStage.Results);
                    Choose("Quit to main menu");
                }
                report.endingOrder = order.Select(slot => EndingNames[slot]).ToList();
                Record(waves + " survived waves kept one destination throughout each of six levels: " + string.Join(", ",report.endingOrder) +
                    ". Six real deadline losses matched their open panes in the same order. Partial and exhausted bag reloads, manual and tracking pauses, and a forced development preview preserved the rotation.");
            }
            finally
            {
                game.Ending.SetDevelopmentEnding(-1);
                game.Ending.EditorUseShufflePreferences(SmokeBagKey,seed);
                PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
