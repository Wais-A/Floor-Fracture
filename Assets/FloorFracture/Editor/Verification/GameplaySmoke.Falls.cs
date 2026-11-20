using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        private static IEnumerator ObserveSelectedFall(string ending, bool reduced, int nextEndingPresses)
        {
            Choose("Main menu"); Choose("Settings");
            if ((PlayerPrefs.GetInt("ff.reduced", 0) != 0) != reduced) Choose("Fall motion");
            Choose("Back"); Choose("Development");
            for (int i = 0; i < nextEndingPresses; i++) Choose("Next ending");
            Choose("Seed 379 · level 1"); yield return Stage(GameStage.Ready);

            // A real deadline loss shatters the occupied tile before the rim shot.
            StandOn(0);
            Vector3 bearing = Vector3.ProjectOnPlane(game.BoardView.Board.position - head.position, Vector3.up);
            SetHead(new Pose(head.position, Quaternion.LookRotation(bearing, Vector3.up) * Quaternion.Euler(32f, 0f, 0f)));
            Pose trackedAtStart = head;
            Choose("Start wave"); yield return Stage(GameStage.Countdown);
            yield return Stage(GameStage.Fall, 8);
            Require(!game.State.IsIntact(0) && game.Ending.IsPlaying && game.Ending.LastEndingName == ending,
                "The chosen ending did not follow the real starting tile's deadline loss: " + ending);

            var witness = new FallWitness(ending, reduced, trackedAtStart);
            report.falls.Add(witness.evidence);
            yield return FallFrame(witness, "rim", 0.10, SmokeView.FallRim);
            yield return FallFrame(witness, "early-shaft", reduced ? 0.40 : 0.65);
            yield return FallFrame(witness, "mid-shaft", reduced ? 0.70 : 1.75, SmokeView.FallDown, SmokeView.FallForward);
            yield return FallFrame(witness, "late-shaft", reduced ? 0.76 : 2.98, SmokeView.FallDown);
            yield return FallFrame(witness, "contact", reduced ? 0.94 : 3.27, SmokeView.FallDown, SmokeView.FallRim);
            Require(witness.SecondaryEffect(), ending + " produced no visible contact effect at the first post-impact sample.");
            witness.CheckArrival();
            double immersionAt = reduced ? 2.95 : ending == "Water" ? 5.80 : 5.45;
            yield return FallFrame(witness, "immersion", immersionAt, SmokeView.FallDown);
            witness.CheckImmersion();
            while (game.Stage == GameStage.Fall)
            {
                witness.Observe();
                Require(witness.Age < witness.expectedFinish + 2.0, "The selected ending did not complete naturally.");
                yield return null;
            }
            Require(game.Stage == GameStage.Results, "The ending did not reach its results screen.");
            witness.Finish();
            Choose("Restart"); yield return Stage(GameStage.Ready);
            Record(reduced
                ? ending + " reduced motion kept the chamber geometry stationary before contact and reached results."
                : ending + " raised the shaft walls, fixed-spaced landmarks and floor together before impact. The tracked head stayed still through immersion and results.");
        }

        private static IEnumerator FallFrame(FallWitness witness, string phase, double at, params SmokeView[] views)
        {
            while (witness.Age < at)
            {
                Require(game.Stage == GameStage.Fall, "Ended during fall evidence phase " + phase);
                witness.Observe();
                yield return null;
            }
            witness.Sample(phase);
            foreach (SmokeView view in views)
            {
                string angle = view == SmokeView.FallForward ? "forward" : view == SmokeView.FallRim ? (phase == "rim" ? "opening" : "steep") : "down";
                Capture($"fall-{witness.evidence.ending.ToLowerInvariant()}-{witness.evidence.motion}-{phase}-{angle}", view);
            }
            Save();
        }

        private sealed class FallWitness
        {
            internal readonly FallEvidence evidence;
            internal readonly double expectedFinish;
            internal double Age => Now - start;
            private readonly double start, contact;
            private readonly bool reduced, water;
            private readonly Pose eyePose;
            private readonly Transform chamber, eye;
            private readonly MeshRenderer marks, walls, floor, flames;
            private readonly float firstMarks, firstWalls, firstFloor, firstSpan;
            private int lastFrame = -1;
            private float lastMarks;
            private double lastAge;

            internal FallWitness(string ending, bool reduced, Pose initialEye)
            {
                this.reduced = reduced; water = ending == "Water";
                start = Now; contact = reduced ? 0.82 : 3.15; expectedFinish = reduced ? 3.93 : 6.72;
                eyePose = initialEye; eye = game.Rig.centerEyeAnchor;
                var chamberObject = GameObject.Find("Fall chamber");
                Require(chamberObject, "The active fall chamber is missing.");
                chamber = chamberObject.transform;
                marks = Renderer("Shaft landmarks"); walls = Renderer("Shaft walls"); floor = Renderer("Hazard surface"); flames = Renderer("Flames or crystals");
                firstMarks = lastMarks = marks.transform.position.y; firstWalls = walls.transform.position.y; firstFloor = floor.transform.position.y;
                firstSpan = marks.bounds.size.y;
                Require(firstSpan > 2f, "The shaft landmark mesh lacks vertically separated geometry.");
                float openingY = game.BoardView.Board.position.y;
                Require(Mathf.Abs(firstFloor - (openingY - (reduced ? 0f : 5f))) < 0.10f,
                    $"The hazard began at {firstFloor:F2} m for an opening at {openingY:F2} m.");
                evidence = new FallEvidence { ending = ending, motion = reduced ? "reduced" : "full", plannedContact = contact,
                    landmarkSpan = firstSpan, floorOpeningY = openingY, startingHazardY = firstFloor };
                Observe();
            }

            private MeshRenderer Renderer(string name)
            {
                var child = chamber.Find(name);
                var renderer = child ? child.GetComponent<MeshRenderer>() : null;
                Require(renderer, "Missing fall evidence renderer: " + name);
                return renderer;
            }

            internal bool SecondaryEffect()
            {
                if (!water) return flames.enabled && flames.gameObject.activeInHierarchy;
                var splash = chamber.Find("Water contact splash");
                if (!splash || !splash.gameObject.activeInHierarchy) return false;
                var system = splash.GetComponent<ParticleSystem>();
                return system && (system.isPlaying || system.IsAlive(true));
            }

            internal void Observe()
            {
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                double age = Age;
                float markY = marks.transform.position.y;
                float travel = markY - firstMarks;
                float eyeTravel = Vector3.Distance(eyePose.position, eye.position);
                float eyeAngle = Quaternion.Angle(eyePose.rotation, eye.rotation);
                evidence.maxTrackedHeadDisplacement = Mathf.Max(evidence.maxTrackedHeadDisplacement, eyeTravel);
                evidence.maxTrackedHeadRotation = Mathf.Max(evidence.maxTrackedHeadRotation, eyeAngle);
                Require(eyeTravel < 0.002f && eyeAngle < 0.2f, $"The tracked head moved {eyeTravel:F3} m / {eyeAngle:F2}° during the loss.");
                bool effect = SecondaryEffect();
                if (effect && evidence.firstSecondaryEffectAt < 0) evidence.firstSecondaryEffectAt = age;
                // Allow one rendered frame between the game's Begin/Update and the Editor observer.
                if (age < contact - 0.075)
                {
                    float floorError = Mathf.Abs((floor.transform.position.y - firstFloor) - travel);
                    float wallError = Mathf.Abs((walls.transform.position.y - firstWalls) - travel);
                    float spacingError = Mathf.Abs(marks.bounds.size.y - firstSpan);
                    evidence.maxPreContactOffsetError = Mathf.Max(evidence.maxPreContactOffsetError, Mathf.Max(floorError, Mathf.Max(wallError, spacingError)));
                    Require(evidence.maxPreContactOffsetError < 0.035f, $"Shaft, bands, or floor diverged by {evidence.maxPreContactOffsetError:F3} m before contact at {age:F3} s.");
                    Require(!effect, $"A secondary hazard started at {age:F3} s, before contact at {contact:F2} s.");
                    if (reduced) Require(Mathf.Abs(travel) < 0.04f, "The reduced tableau made a sustained vertical movement before contact.");
                    else if (lastAge > 0 && age > lastAge)
                    {
                        float step = markY - lastMarks;
                        Require(step >= -0.02f && step <= 0.055f + 7f * (age - lastAge),
                            $"The shaft jumped {step:F3} m in {age - lastAge:F3} s at {age:F3} s.");
                    }
                }
                lastMarks = markY; lastAge = age;
            }

            internal void Sample(string phase)
            {
                Observe();
                evidence.samples.Add(new FallMeasurement { phase = phase, seconds = Age, landmarkY = marks.transform.position.y,
                    wallY = walls.transform.position.y, floorY = floor.transform.position.y, landmarkTravel = marks.transform.position.y - firstMarks,
                    landmarksVisible = marks.enabled && marks.gameObject.activeInHierarchy, secondaryEffectActive = SecondaryEffect() });
            }

            internal void CheckArrival()
            {
                evidence.contactHazardY = floor.transform.position.y;
                Require(Mathf.Abs(evidence.contactHazardY - evidence.floorOpeningY) < 0.10f,
                    $"The secondary hazard started at {evidence.contactHazardY:F2} m before reaching the opening at {evidence.floorOpeningY:F2} m.");
            }

            internal void CheckImmersion()
            {
                if (!water)
                {
                    Require(SecondaryEffect() && floor.transform.position.y > evidence.floorOpeningY + 0.5f,
                        "Lava's post-impact rise and surrounding flames were missing from the immersion frame.");
                    return;
                }
                var veil = eye.Find("View fade").GetComponent<MeshRenderer>();
                var material = new MaterialPropertyBlock();
                veil.GetPropertyBlock(material);
                evidence.immersionTintAlpha = material.GetColor("_Tint").a;
                Require(veil.enabled && evidence.immersionTintAlpha > 0.25f, "The Water immersion frame did not contain the underwater tint.");
                Require(reduced ? Mathf.Abs(floor.transform.position.y - evidence.floorOpeningY) < 0.04f : floor.transform.position.y > eye.position.y + 0.04f,
                    "The Water immersion frame did not correspond to the expected tracked-eye and surface heights.");
            }

            internal void Finish()
            {
                evidence.finishedAt = Age;
                Require(Math.Abs(evidence.finishedAt - expectedFinish) < 0.65, $"The {evidence.motion} ending finished at {Age:F2} s.");
                Require(evidence.firstSecondaryEffectAt >= contact - 0.075 && evidence.firstSecondaryEffectAt < contact + 0.3,
                    $"First visible impact effect was recorded at {evidence.firstSecondaryEffectAt:F3} s.");
                if (!reduced)
                {
                    var early = evidence.samples.Find(s => s.phase == "early-shaft");
                    var mid = evidence.samples.Find(s => s.phase == "mid-shaft");
                    var late = evidence.samples.Find(s => s.phase == "late-shaft");
                    Require(mid.landmarksVisible && mid.landmarkTravel - early.landmarkTravel > 0.60f && late.landmarkTravel - mid.landmarkTravel > 0.60f &&
                        late.landmarkTravel >= 4.1f, $"The full descent did not traverse its marked shaft: {early.landmarkTravel:F2}, {mid.landmarkTravel:F2}, {late.landmarkTravel:F2} m.");
                }
            }
        }

        [Serializable]
        private sealed class FallEvidence
        {
            public string ending, motion;
            public double plannedContact, finishedAt, firstSecondaryEffectAt = -1;
            public float landmarkSpan, floorOpeningY, startingHazardY, contactHazardY, immersionTintAlpha, maxPreContactOffsetError, maxTrackedHeadDisplacement, maxTrackedHeadRotation;
            public List<FallMeasurement> samples = new List<FallMeasurement>();
        }

        [Serializable]
        private sealed class FallMeasurement
        {
            public string phase;
            public double seconds;
            public float landmarkY, wallY, floorY, landmarkTravel;
            public bool landmarksVisible, secondaryEffectActive;
        }
    }
}
