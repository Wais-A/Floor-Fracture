using UnityEngine;

namespace FloorFracture
{
    [DisallowMultipleComponent]
    public sealed class OfflineNarration : MonoBehaviour
    {
        public const string ResultsCaption = "Take a breath. Retry when you're ready.";
        const float StepAwayDistance = .16f;

        AudioSource speaker;
        Transform eye;
        Vector3 startingPosition;
        bool requested;

        void Awake() => Build();

        /// <summary>Adopts the narrator saved under this object, or creates it.</summary>
        public void Build()
        {
            var voice = SceneParts.Child(transform, "Offline narration", out bool created);
            speaker = SceneParts.Require<AudioSource>(voice.gameObject);
            if (!created) return;
            speaker.playOnAwake = false;
            speaker.spatialBlend = 0f;
            speaker.spatialize = false;
        }

        public void Configure(bool enabledByPlayer, float volume)
        {
            requested = enabledByPlayer;
            speaker.volume = Mathf.Clamp01(volume);
            if (!requested || speaker.volume <= 0f) Stop();
        }

        internal void Speak(GameStage stage, Transform trackedEye)
        {
            Stop();
            if (!requested || !isActiveAndEnabled || !trackedEye || speaker.volume <= 0f) return;
            if (stage != GameStage.Results) return;
            var clip = Resources.Load<AudioClip>("Narration/" + stage);
            if (!clip) return;
            eye = trackedEye;
            startingPosition = eye.position;
            speaker.clip = clip;
            speaker.Play();
        }

        internal void Stop()
        {
            if (speaker && speaker.clip)
            {
                speaker.Stop();
                speaker.clip = null;
            }
            eye = null;
        }

        void LateUpdate()
        {
            if (!speaker.clip) return;
            if (!eye || !speaker.isPlaying) { Stop(); return; }
            Vector3 travelled = eye.position - startingPosition;
            if (travelled.x*travelled.x + travelled.z*travelled.z >= StepAwayDistance*StepAwayDistance) Stop();
        }

        void OnApplicationPause(bool paused) { if (paused) Stop(); }
        void OnApplicationFocus(bool focused) { if (!focused && !Application.isEditor) Stop(); }
        void OnDisable() => Stop();
    }
}
