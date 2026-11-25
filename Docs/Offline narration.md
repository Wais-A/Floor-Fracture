# Offline narration audition

Turn on **Narration** in Settings to audition the voice. It starts off on a fresh install. The master volume controls its level independently of the saved narration preference. A centered, non-spatial Unity `AudioSource` plays the recordings from the APK. Each line also appears in the panel text, including when narration is off or muted.

| Screen | Spoken line and caption | Duration | Source WAV bytes |
| --- | --- | ---: | ---: |
| Practice stepping | When your pane cracks, step onto clear glass beside it. | 3.82 s | 168,492 |
| Stone practice | Drop or gently toss the stone onto an intact pane. | 3.56 s | 157,228 |
| Run complete | Take a breath. Restart when you're ready. (caption now reads “Retry when you're ready”; re-record to match) | 2.29 s | 100,908 |

Changing screens, stepping 16 cm horizontally, suspending the app, or disabling narration stops the current line. A skipped fall goes straight to the captioned result. A naturally completed fall plays the results line when enabled. Restart clears it immediately.

Playing these clips needs audio output only. The project manifest removes Android's microphone-recording permission from the merged Quest package. MRUK still requests spatial data to fit the glass to the room. [Android manifest merge rules](https://developer.android.com/build/manage-manifests#node_markers)

## Quest 3 offline playback

On November 20, 2026, a physical Quest 3 played all three lines from a test APK that lacked Android’s internet permission. Android also refused a local loopback socket. The compressed clips matched the retail APK from game revision `72faf40` byte for byte. At 75% master volume, the narration source produced 43 audio buffers over 0.92 seconds of each line while the panel displayed its caption. Muting stopped speech and preserved the caption. Skipping a fall showed silent Results, and Restart stopped the Results line. [On-device playback receipt](<./Verification/Quest offline narration.json>)

The headset was unworn. The test used a synthetic 4 × 4 board, selected the actions behind the displayed menu buttons, and reached Results through the built-in ending preview. The three source WAVs, their Unity import settings, and the narration component match the tested revision in boundary-fix commit `599e1b6`. The `1da914c` retail APK also contains the same three compressed audio payloads. A wearer still needs to choose the final voice, judge its loudness alongside the game sounds, and try the controls by hand or controller.

## Source and permission

Bryce Beattie released the US-English `LJSpeech (medium)` voice model into the public domain. His model card describes training it from scratch. Its source is Keith Ito’s LJ Speech dataset, which contains Linda Johnson’s LibriVox recordings. The dataset publisher places the recordings, text and annotations in the US public domain. [Trainer’s voice release](https://brycebeattie.com/files/tts/) · [Pinned model card](https://huggingface.co/rhasspy/piper-voices/blob/217ddc79818708b078d0d14a8fae9608b9d77141/en/en_US/ljspeech/medium/MODEL_CARD) · [LJ Speech dataset and terms](https://keithito.com/LJ-Speech-Dataset/)

The three lines were written for Floor Fracture and synthesized locally with that public-domain voice. The release terms support including these rendered recordings in the class build. The Piper project also identifies this voice’s full training chain as public domain. The GPL covers the authoring engine. Its output clause applies to output containing covered material. The APK contains the finished audio clips. [Piper voice licensing](https://github.com/OHF-Voice/piper1-gpl/blob/main/docs/COMMERCIAL_USE.md) · [GNU GPLv3 section 2](https://www.gnu.org/licenses/gpl-3.0.html#section2)

The Meta Text to Speech AI Building Block uses hosted ElevenLabs or OpenAI synthesis. Floor Fracture’s existing spatial audio prefab localizes pane breaks in the room. These instruction lines use local menu-style playback. [Meta AI Building Blocks](https://developers.meta.com/vr/documentation/unity/unity-ai-building-blocks-overview)

## Authoring and import

Generation used Python 3.9.6 on an Apple Silicon Mac, `piper-tts==1.4.2`, ONNX Runtime 1.19.2 on CPU, and Rhasspy model revision `217ddc79818708b078d0d14a8fae9608b9d77141`. The source ONNX SHA-256 is `6f52a751e2349abe7a76735eb09dc1875298c77ea2342ffd2fef79ff81b87f22`. The adjacent JSON SHA-256 is `141d612cc0a95ed7efc1ca936b845c2364967f2e9217c5dbfcf69fc4d6c65860`. [Model files](https://huggingface.co/rhasspy/piper-voices/tree/217ddc79818708b078d0d14a8fae9608b9d77141/en/en_US/ljspeech/medium) · [Piper release](https://pypi.org/project/piper-tts/1.4.2/)

With the ONNX and its matching `.onnx.json` in a local working directory, the following regenerates a line. Use the table above for the other two lines. Piper’s noise sampling can vary slightly across renders.

```python
import wave
import numpy as np
from piper import PiperVoice, SynthesisConfig

voice = PiperVoice.load("en_US-ljspeech-medium.onnx", use_cuda=False)
settings = SynthesisConfig(length_scale=1.02, noise_scale=.57, noise_w_scale=.75)
line = "When your pane cracks, step onto clear glass beside it."
path = "PracticeStep.wav"
with wave.open(path, "wb") as output:
    voice.synthesize_wav(line, output, syn_config=settings)
with wave.open(path, "rb") as source:
    rate = source.getframerate()
    samples = np.frombuffer(source.readframes(source.getnframes()), dtype="<i2").astype(float)
active = np.flatnonzero(np.abs(samples) > 95)
samples = samples[max(0, active[0] - int(.055 * rate)):active[-1] + int(.12 * rate)]
samples = np.rint(samples * ((10 ** (-3 / 20) * 32767) / np.abs(samples).max())).astype("<i2")
with wave.open(path, "wb") as output:
    output.setparams((1, 2, rate, 0, "NONE", "not compressed"))
    output.writeframes(samples.tobytes())
```

The three source files total **426,628 bytes** and contain mono 16-bit PCM at 22,050 Hz with peaks at −3 dBFS. Unity imports them from `Assets/FloorFracture/Resources/Narration` as Vorbis at 70% quality, preserving their sample rate and using Decompress On Load. `GameplaySmoke.Narration.cs` exercises the menu, tutorial, gameplay, and results controls in Play Mode and checks the imported waveforms and captions.
