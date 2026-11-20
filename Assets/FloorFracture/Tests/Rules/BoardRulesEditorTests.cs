using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FloorFracture.Tests
{
    public sealed class BoardRulesEditorTests
    {
        [Test] public void CoordinatesAndSeams() => BoardRuleChecks.CoordinatesAndSeams();
        [Test] public void DeadlinesAndPause() => BoardRuleChecks.DeadlinesAndPause();
        [Test] public void ModesAndReset() => BoardRuleChecks.ModesAndReset();
        [Test] public void SlidersAndGrace() => BoardRuleChecks.SlidingAndGrace();
        [Test] public void EverySeededSurvivalPath() => Debug.Log(BoardRuleChecks.CheckSequences());
        [Test] public void LargerBoardSurvivalPaths() => Debug.Log(BoardRuleChecks.CheckLargerBoards());

        [MenuItem("Tools/Floor Fracture/Run board rule checks")]
        public static void RunAll() => Debug.Log(BoardRuleChecks.RunAll());
    }
}
