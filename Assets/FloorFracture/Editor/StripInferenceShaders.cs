using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;

namespace FloorFracture.Editor
{
    // The game never runs Inference Engine: it is embedded for Unity AI Assistant in the Editor, but Meta's
    // AI Building Blocks assembly references it, which pulls its shaders into every player build.
    // shortcut: strips every Inference Engine shader from players; delete this if the game adopts an AI block or Inference Engine.
    internal sealed class StripInferenceShaders : IPreprocessShaders, IPreprocessComputeShaders
    {
        private const string Package = "Packages/com.unity.ai.inference/";
        public int callbackOrder => 0;

        internal static bool FromInferenceEngine(Object shader) => AssetDatabase.GetAssetPath(shader).StartsWith(Package);

        public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
        {
            if (FromInferenceEngine(shader)) data.Clear();
        }

        public void OnProcessComputeShader(ComputeShader shader, string kernelName, IList<ShaderCompilerData> data)
        {
            if (FromInferenceEngine(shader)) data.Clear();
        }
    }
}
