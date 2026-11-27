using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

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
            float keptHum = lamp.GetComponentInChildren<AudioSource>(true).volume;
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

                var hum = sound.CreateLampVoice(lamp);
                sound.SetLampLevel(hum, 1f);
                Expect(hum, .5f, "A fully lit lamp's hum");
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
                lamp.GetComponentInChildren<AudioSource>(true).volume = keptHum;
            }
            Debug.Log("[Floor Fracture verification] Sound levels check passed.");
        }
    }
}
