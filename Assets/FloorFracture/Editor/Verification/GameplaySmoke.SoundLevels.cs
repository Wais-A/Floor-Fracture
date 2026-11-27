using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FloorFracture.Editor
{
    public static partial class GameplaySmoke
    {
        // Every sound level the game uses, as GameSound's Inspector fields.
        private static readonly string[] SoundLevels =
        {
            "menuClick", "levelComplete", "countdownTick", "countdownGo", "music", "lampHum", "openingDraft", "thunder",
            "paneImpact", "paneImpactMany", "glassFragments", "shaftEcho", "glassStrain",
            "fallAir", "fallRoar", "endingAmbience", "landingImpact", "softLandingImpact", "tarSuction"
        };

        /// <summary>Each sound level is a GameSound field, and the volume the game gives a sound follows it. Runs without Play.</summary>
        [MenuItem("Tools/Floor Fracture/Check sound levels")]
        public static void CheckSoundLevels()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run this check from Edit Mode.");
            var sound = Locate("Floor Fracture").GetComponent<GameSound>();
            var missing = SoundLevels.Where(name => new SerializedObject(sound).FindProperty(name) == null).ToArray();
            Require(missing.Length == 0, "GameSound has no Inspector field for: " + string.Join(", ", missing) + ".");

            // Test values are set on the object itself, so the scene is not marked changed, and put back afterwards.
            const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            FieldInfo Field(string name) => typeof(GameSound).GetField(name, Private);
            var kept = SoundLevels.ToDictionary(name => name, name => (float)Field(name).GetValue(sound));
            var lamp = Locate("Confirmed glass board/Overhead lights/Lamp 0");
            var savedHum = lamp.GetComponentInChildren<AudioSource>(true);
            float keptHum = savedHum.volume, keptReach = savedHum.minDistance;
            var keptPosition = savedHum.transform.position;
            try
            {
                Field("lampHum").SetValue(sound, .5f); Field("thunder").SetValue(sound, .9f); Field("music").SetValue(sound, .2f);
                Field("fallAir").SetValue(sound, .7f); Field("fallRoar").SetValue(sound, .6f); Field("endingAmbience").SetValue(sound, .4f);
                sound.Build();
                float master = sound.MasterVolume;
                AudioSource Voice(string name) => sound.transform.Find(name).GetComponent<AudioSource>();
                void Expect(AudioSource voice, float level, string what) =>
                    Require(Mathf.Abs(voice.volume - level * master) < .0005f,
                        $"{what} plays at {voice.volume:0.###}, not its level {level} times the player's volume {master}.");

                const float Pitch = .75f, PoolRadius = .55f * Pitch;
                var hum = sound.CreateLampVoice(lamp, PoolRadius);
                sound.SetLampLevel(hum, 1f);
                Expect(hum, .5f, "A fully lit lamp's hum");

                // The hum comes from its spotlight: full while the player stands in the lit spot, much quieter two panes away.
                var ear = Object.FindAnyObjectByType<AudioListener>().transform.position;
                float Loudness(Vector3 pool)
                {
                    sound.PlaceLampVoice(hum, pool);
                    float distance = Vector3.Distance(ear, hum.transform.position);
                    return Mathf.Min(1f, hum.minDistance / distance); // Unity's logarithmic rolloff
                }
                var under = new Vector3(ear.x, 0f, ear.z);
                Require(Loudness(under) >= .999f, "Standing in the middle of a spotlight does not hear its hum at full volume.");
                Require(Loudness(under + Vector3.right * PoolRadius) >= .999f, "Standing at the edge of a spotlight does not hear its hum at full volume.");
                float away = Loudness(under + Vector3.right * 2 * Pitch);
                Require(away <= .4f, $"Two panes from the spotlight the hum is still at {away:0.00} of full volume.");
                Expect(Voice("Thunder 0"), .9f, "Thunder");
                Expect(Voice("Background music"), .2f, "The music");
                typeof(GameSound).GetMethod("MixFall", Private).Invoke(sound, new object[] { 1f, 1f });
                Expect(Voice("Shaft air"), .7f, "Shaft air at full fall speed");
                Expect(Voice("Shaft roar"), .6f, "Shaft roar at full fall speed");
                Expect(Voice("Ending ambience"), .4f, "The ending ambience");
                typeof(GameSound).GetMethod("StopFall", Private).Invoke(sound, null);
                sound.SetLampLevel(hum, 0f);
            }
            finally
            {
                foreach (var level in kept) Field(level.Key).SetValue(sound, level.Value);
                sound.Build();
                savedHum.volume = keptHum;
                savedHum.minDistance = keptReach;
                savedHum.transform.position = keptPosition;
            }
            // Thunder starts fading only beyond its Min Distance. A strike is 9 m from the board centre, so a player
            // standing up to 3 m from the centre must be inside it to hear the full level.
            float strike = (float)typeof(GameSound).Assembly.GetType("FloorFracture.Lightning")
                .GetField("StrikeDistance", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
            foreach (var name in new[] { "Thunder 0", "Thunder 1", "Thunder 2" })
            {
                float reach = sound.transform.Find(name).GetComponent<AudioSource>().minDistance;
                Require(reach >= strike + 3f, $"{name} starts fading {reach} m away, so thunder {strike} m from the board centre is quieter than its level.");
            }
            Debug.Log("[Floor Fracture verification] Sound levels check passed.");
        }
    }
}
