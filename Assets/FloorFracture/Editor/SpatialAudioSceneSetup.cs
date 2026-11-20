using System;
using Meta.XR.BuildingBlocks;
using Meta.XR.BuildingBlocks.Editor;
using UnityEditor;
using UnityEngine;

namespace FloorFracture.Editor
{
    public static class SpatialAudioSceneSetup
    {
        const string BlockPath = "Packages/com.meta.xr.sdk.core/Editor/BuildingBlocks/BlockData/SpatialAudio/SpatialAudio.asset";
        const string BlockId = "6737262b-4fab-4779-a38b-fe74012fa505";
        const string PrefabPath = "Assets/FloorFracture/Resources/" + GameSound.SpatialVoiceResource + ".prefab";

        public static void Install()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BlockData>(BlockPath);
            if (!definition || definition.Id != BlockId)
                throw new InvalidOperationException("Missing Meta Spatial Audio block definition.");

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing && HasSpatialAudio(existing, definition)) return;
            var emitter = new GameObject(GameSound.SpatialVoiceResource);
            try
            {
                var installed = MetaRoomBlocks.Install(definition, emitter);
                if (installed.Count != 1 || installed[0] != emitter || !HasSpatialAudio(emitter, definition))
                    throw new InvalidOperationException("Meta's Spatial Audio installer did not configure the emitter.");
                var source = emitter.GetComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 1f;
                source.spatialize = true;
                source.dopplerLevel = 0f;
                source.minDistance = 1.15f;
                source.maxDistance = 16f;
                source.rolloffMode = AudioRolloffMode.Logarithmic;
                if (!PrefabUtility.SaveAsPrefabAsset(emitter, PrefabPath))
                    throw new InvalidOperationException("Could not save the Meta Spatial Audio emitter prefab.");
            }
            finally { UnityEngine.Object.DestroyImmediate(emitter); }
        }

        static bool HasSpatialAudio(GameObject emitter, BlockData definition)
        {
            var marker = emitter.GetComponent<BuildingBlock>();
            return marker && marker.BlockId == definition.Id && marker.Version == definition.Version
                && emitter.GetComponent<AudioSource>() && emitter.GetComponent<MetaXRAudioSource>()
                && emitter.GetComponent<MetaXRAudioSourceExperimentalFeatures>();
        }
    }
}
