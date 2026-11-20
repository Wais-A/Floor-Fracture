# Offline coach experiment

On November 20, 2026, Meta’s local text provider produced identical replies across two CPU and two Metal GPU runs. Only the Memory reply fully explained its loss. None of the three replies gave an explicit next move. The rules-based Results screen displayed the correct recorded cause in all 12 control visits. Floor Fracture ships the rules text. The experimental models and harness stayed in an isolated project. [Prompts, verbatim replies and measurements](<./Offline coach results.json>)

## What the player would read

| Synthetic outcome | Rules-based Results text | Generated coaching |
| --- | --- | --- |
| Marked starting pane shatters at zero | At zero, the glass under the ring shattered. | Identified the marked starting pane and adjacent clear glass. Omitted the break and when to step. |
| A previously revealed Memory warning is hidden when the pane breaks | At zero, the ring was on glass that cracked during the Memory reveal. | Repeated the entire event, including the hidden warning and the break at zero. Gave no next move. |
| The ring stays over a gap for 0.31 s with two seconds left | The ring was over an open hole for 0.3 seconds. | Named the gap and its 0.31 s duration. Omitted the expired grace period and how to regain support. |

For each case, a complete coach reply needed to explain the decisive event and give a specific action the player could take next time. The same system instruction, event text, tokenizer and greedy decoding ran on both Unity backends. Each control visit injected the corresponding recorded loss reason, skipped the ending, and read the text from the real Results panel. These three fixtures assess this prompt and model configuration. [Case-by-case grading](<./Offline coach results.json>)

## Time and responsiveness on the Mac

| Results configuration | First provider warm-up | Repeat provider warm-up | Time to first text | Time to complete reply |
| --- | ---: | ---: | ---: | ---: |
| Rules text only | No model | No model | 1.87–3.33 ms from Skip | Same frame as the first text |
| Meta provider, CPU | 246 ms | 435 ms | 159–230 ms | 0.93–1.98 s |
| Meta provider, Metal GPU | 3,033 ms | 432 ms | 36–89 ms | 0.74–2.09 s |

Each native backend ran in two fresh Unity 6000.6.9f1 Editor processes on an Apple M4 Max with 14 CPU cores and 36 GB of unified memory. The Editor used batch mode and Metal. The rules text appeared on the first game frame after Skip in every visit. Model warm-up started after the Results panel was visible and the tokenizer files had been read. Generation timings start at the text request and exclude model warm-up. The OS and graphics caches persisted between processes.

During generation, the longest sampled game-frame interval was 163 ms on CPU and 37 ms on GPU. During the first Metal warm-up it reached 3.04 s. Restart accepted a selection one game frame into a fourth request in all four sessions. Cancelling on Restart settled the task within 5.3–16.9 ms of sending the request. These are local Editor measurements. Standalone headset latency, memory, thermal behavior and comfort remain unmeasured. [Per-session frame and control timings](<./Offline coach results.json>)

## Model and provider

The installed Meta XR Core 209.0.0 LLM Building Block supports `UnityInferenceEngineProvider` in Chat mode. Its text runner consumes token IDs, attention masks, position IDs and past keys and values from 30 layers. It reads logits and the updated cache from each layer. The trial submitted only the three synthetic game-event strings through `ChatRequest.text`. Inference ran locally without credentials, images, tracking coordinates or room geometry. [Meta’s Inference Engine guide](https://developers.meta.com/vr/documentation/unity/unity-ai-unity-inference-engine/) · [Project’s Unity 2.3.0 compatibility settings](<../Meta Building Blocks.md#inference-engine-for-the-llm-block>)

The ready-made 163,523,112-byte UINT8 Unity export exposed three inputs and only logits. Both backends failed at warm-up when Meta requested `past_key_values.0.key`. A separate 538,868,107-byte FP32 ONNX export exposed all 63 inputs and 61 outputs with standard ONNX operators. Unity’s 2.3.0 importer converted this graph. `ModelQuantizer.QuantizeWeights(QuantizationType.Uint8, ref model)` and `ModelWriter.Save` produced the tested 163,397,684-byte model. The three tokenizer files added 1,270,605 bytes, bringing the uncompressed runtime input files to **157.04 MiB**. [Ready-made Unity export](https://huggingface.co/dtaddis/SmolLM2-135M-Instruct-Unity/tree/d3c7f779fe470e3260026764b7a34b1f377f4060) · [Cache-capable ONNX export](https://huggingface.co/onnx-community/SmolLM2-135M-Instruct-ONNX/tree/b8a5c0f183b78c55955a5364f610c36668b5e681/onnx) · [Unity’s quantization documentation](https://docs.unity3d.com/Packages/com.unity.ai.inference@2.3/manual/quantize-a-model.html)

The export configuration names Hugging Face’s SmolLM2-135M-Instruct as its source. The model owner lists Apache 2.0. The pinned cache-capable export has no separate license card. The trial used 30 layers, three key/value heads, a 64-value head dimension, EOS token 2, the owner’s ChatML format, a 256-token input limit and a 48-token generation limit. Meta’s nonblocking scheduler used 150 steps per frame. [Owner’s model card](https://huggingface.co/HuggingFaceTB/SmolLM2-135M-Instruct/blob/12fd25f77366fa6b3b4b768ec3050bf629380bac/README.md) · [Export configuration](https://huggingface.co/onnx-community/SmolLM2-135M-Instruct-ONNX/blob/b8a5c0f183b78c55955a5364f610c36668b5e681/config.json) · [Owner’s chat template](https://huggingface.co/HuggingFaceTB/SmolLM2-135M-Instruct/blob/12fd25f77366fa6b3b4b768ec3050bf629380bac/tokenizer_config.json)

A separate CPU cross-check used the owner’s 137,147,867-byte INT8 ONNX file with ONNX Runtime 1.23.2 and four threads. Its three replies echoed instructions, repeated a phrase, or described the assignment. All missed the event-specific causes. It uses a different quantization and execution engine, so its timings are reported separately. [Owner’s INT8 file](https://huggingface.co/HuggingFaceTB/SmolLM2-135M-Instruct/blob/12fd25f77366fa6b3b4b768ec3050bf629380bac/onnx/model_int8.onnx) · [Standalone outputs and timings](<./Offline coach results.json>)
