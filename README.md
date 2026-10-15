# New Vision

![Platform](https://img.shields.io/badge/platform-Meta%20Quest%203%20%7C%203S-1c1e20)
![Unity](https://img.shields.io/badge/Unity-6000.0.66f2-1c1e20?logo=unity)
![Inference](https://img.shields.io/badge/inference-Unity%20Inference%20Engine-5cfd9b)
![License](https://img.shields.io/badge/license-AGPL--3.0%20%2B%20Meta%20licenses-blue)

**Real-time object detection in mixed reality for Meta Quest 3.**
New Vision shows your surroundings through passthrough and outlines objects — chairs, tables, plants, people, cars
and 70+ more — with crisp, horizon-level boxes and readable labels like `Chair 87%`. Everything runs on the headset;
no PC or network connection is needed.

It is built on Meta's [Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)
(`MultiObjectDetection` scene), reworked for speed, accuracy, readability and walking around freely, including outdoors.

## Features

- **On-device YOLO detection** on the Quest GPU via Unity Inference Engine — about 10 updates per second.
- **Switchable models on the fly**: YOLO11n / YOLO11s at 320 or 416 px, chosen in the in-headset menu.
- **Choose what to detect**: in-headset menu with all 80 COCO classes and presets (all / none / furniture & plants), saved between launches.
- **Fewer false positives without losing range**: a box appears only after the object is detected several times in a row,
  and detections with an impossible real-world size (from depth) are rejected.
- **Works beyond depth-sensor range**: far objects are placed using their typical size, up to 60 m.
- **Readable overlay**: sharp rectangular frames that stay level with the horizon, smooth motion,
  labels that keep the same apparent size at any distance.
- **Walk around freely**: the boundary (Guardian) is hidden while passthrough is on.

## How it works

```mermaid
flowchart LR
    A[Passthrough camera<br/>800×600] --> B[Letterbox to<br/>320 / 416 square]
    B --> C[YOLO11 on GPU<br/>Unity Inference Engine]
    C --> D[Class filter<br/>+ NMS]
    D --> E{Depth hit?}
    E -- yes --> F[Real-size check<br/>reject impossible sizes]
    E -- no --> G[Distance from<br/>typical object size]
    F --> H[Confirm over<br/>3 detections]
    G --> H
    H --> I[World-space box<br/>level, smoothed, labeled]
```

## Requirements

| | |
|---|---|
| Headset | Meta Quest 3 or 3S, Horizon OS v74+, [developer mode](https://developers.meta.com/horizon/documentation/native/android/mobile-device-setup/) enabled |
| Unity | **6000.0.66f2** with *Android Build Support* (OpenJDK, Android SDK & NDK) |
| Git | [Git LFS](https://git-lfs.com/) — the models are stored in LFS |
| Optional | Python 3.10+ with `ultralytics`, only to export additional models |

## Getting started

1. **Clone with LFS**
   ```bash
   git lfs install
   git clone <repository-url>
   ```
   Check that the files in `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/` are megabytes,
   not ~130-byte LFS pointers. If they are pointers, run `git lfs pull`.
2. **Open** the project folder in Unity Hub (first import takes a while).
3. In **File → Build Profiles**, switch to **Android**. The build contains only the `MultiObjectDetection` scene.
4. Run the editor menu items:
   - **New Vision → Apply App Settings** — app name, package id `com.newvision.app`, icon, boundary-visibility support.
   - **New Vision → Convert Models** — converts the ONNX models to Sentis and makes them selectable in the app.
5. Connect the headset over USB and press **Build And Run**.
6. In the headset, allow **camera** and **spatial data** access. If the prompt doesn't appear:
   ```bash
   adb shell pm grant com.newvision.app horizonos.permission.HEADSET_CAMERA
   adb shell pm grant com.newvision.app com.oculus.permission.USE_SCENE
   adb shell pm grant com.newvision.app horizonos.permission.USE_SCENE
   ```

Detection starts right away.

## Controls

| Button | Outside the menu | In the menu |
|---|---|---|
| **Y** or **☰** (left controller) | open the menu | close the menu |
| **Thumbstick** | — | move up/down; left/right jumps 10 rows |
| **A** (or index pinch) | place 3D markers on detected objects | toggle the selected row |
| **X** / **trigger** | — | toggle the selected row |
| **B** (or middle-finger pinch) | remove the markers | close the menu |

The menu has a **Model** row (cycles through the models), quick presets, and a checkbox per object class.

## Models

All `.onnx` files in `Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/Model/`
(except Meta's original `yolov9onnx.onnx`) are converted by **Convert Models** and appear in the menu.
The choice is remembered by model name; the default is `yolo11n_416`.

Measured on Quest 3 (GPU, median time per inference including letterboxing):

| Model | Input | Time | Updates/s | Notes |
|---|---|---|---|---|
| `yolo11n_320` | 320 | ~89 ms | ~11 | fastest |
| **`yolo11n_416`** | 416 | **~96 ms** | **~10** | **default** — sees farther at almost the same speed |
| `yolo11s_320` | 320 | ~167 ms | ~6 | more accurate |
| `yolo11s_416` | 416 | ~193 ms | ~5 | most accurate, slowest |

Weights are stored as Float16 (near-original accuracy).

### Adding a model

```bash
pip install ultralytics
python Tools/export_model.py yolo11m 416                       # any Ultralytics model
python Tools/export_model.py path/to/best.pt 416 my_model_416  # your own trained model
```

Then **New Vision → Convert Models** and build. The model must output `(1, 4 + classes, N)` like Ultralytics exports and
detect the same classes, in the same order, as `SentisYoloClasses.txt`. A model with different classes also needs its own
labels file and typical sizes in `ObjectSizes.cs`.

## Configuration

Tunable in the Inspector of `SentisInferenceManagerPrefab`:

| Component | Field | Default | Effect |
|---|---|---|---|
| Sentis Inference Run Manager | Score Threshold | 0.23 | minimum confidence |
| Sentis Inference Ui Manager | Box Smoothing Speed | 15 | higher = snappier, lower = smoother boxes |
| | Frame Thickness Per Meter | 0.004 | line thickness |
| | Label Scale Per Meter | 0.0002 | label size |
| | Hits To Confirm | 3 | detections in a row before a box is shown |
| | Max Gap Between Hits | 0.4 s | max time between detections counted "in a row" |
| | Min / Max Size Ratio | 0.3 / 3 | allowed real size vs. typical size of the class |
| | Max Estimated Distance | 60 m | farthest placement for objects beyond depth range |

## Using it outdoors

> [!WARNING]
> **Never leave the headset with its lenses facing the sun**, not even for a few seconds. The lenses focus sunlight
> onto the displays and permanently burn them. When you take the headset off outside, put it lenses-down or in its case.

- Do not use it near traffic or while crossing streets: passthrough has latency, distortion and a narrower field of view.
- Tracking works best in daylight; it can be lost at dusk, in bright direct sun, or in large featureless areas.
- The depth sensor reaches only a few meters. Farther objects are placed by their typical size, so their distance is an estimate.
- If the boundary still appears, it can be turned off in the headset's developer settings.

> Outdoor use has not been tested extensively yet.

## Troubleshooting

| Problem | Fix |
|---|---|
| "This sample requires SPATIAL DATA and CAMERA permissions" | grant them (see *Getting started*, step 6) |
| Build fails with `There is not enough space on the disk` | free 20+ GB; then close Unity and delete `Library/Bee` and `Library/BurstCache` |
| Burst `AccessViolationException` during build | same as above: close Unity, delete `Library/Bee` and `Library/BurstCache` |
| Models fail to load / are tiny files | LFS pointers instead of files: `git lfs pull` |
| See inference timing | `adb logcat -s Unity \| grep NewVision` |

## Project structure

New Vision's own files (everything else comes from Meta's sample, partly modified):

```
Assets/NewVision/
├── AppIcon.png                          app icon (Tools/make_icon.py)
├── Editor/NewVisionAppSettings.cs       "New Vision" editor menu: app settings, model conversion
└── Resources/NewVisionLetterbox.shader  letterboxing of the camera image
Assets/PassthroughCameraApiSamples/MultiObjectDetection/SentisInference/
├── Model/yolo11*_*.onnx / .sentis       YOLO11 models (Git LFS)
└── Scripts/
    ├── ClassFilterMenu.cs               in-headset menu: model, classes
    ├── ObjectSizes.cs                   typical real-world sizes of the 80 classes
    └── SharpFrame.cs                    crisp rectangular box outline
Tools/
├── export_model.py                      export an Ultralytics model into the project
└── make_icon.py                         draw the app icon
docs/META_SAMPLE_README.md               Meta's original README
```

Modified Meta files: `SentisInferenceRunManager.cs` (GPU inference, letterbox, model switching, NMS with class filter),
`SentisInferenceUiManager.cs` (box placement, confirmation, size check, visuals), `DetectionUiMenuManager.cs`,
`DetectionManager.cs`, `SentisModelEditorConverter.cs`, `SentisYoloClasses.txt`, `SentisInferenceManagerPrefab.prefab`.

## License

This repository combines components under different licenses:

| Component | License |
|---|---|
| New Vision's own code and assets (files marked `SPDX-License-Identifier: AGPL-3.0-only`, `Tools/`, `Assets/NewVision/`) | [AGPL-3.0](LICENSE) |
| YOLO11 models (`yolo11*.onnx`, `yolo11*.sentis`) by [Ultralytics](https://github.com/ultralytics/ultralytics) | [AGPL-3.0](LICENSE) |
| Meta's sample code and assets, including the modified Meta files | [Oculus SDK License](LICENSE.txt) and, where marked, [MIT](Assets/PassthroughCameraApiSamples/LICENSE.txt) — see [Meta's README](docs/META_SAMPLE_README.md#license) |
| Meta's original YOLOv9 model (`yolov9onnx.onnx`, `yolov9sentis.sentis`) | [MIT](https://github.com/MultimediaTechLab/YOLO/blob/main/LICENSE) |
| Meta XR SDK, Unity packages | their own licenses (installed via the Unity Package Manager, not included here) |

> This is not legal advice. Combining AGPL-licensed components with code under the Oculus SDK License may have
> implications for distributing builds; check the license terms before shipping the app commercially.
> Using the YOLO11 models in closed-source or commercial products requires an
> [Ultralytics Enterprise License](https://www.ultralytics.com/license).

## Acknowledgements

- [Meta — Unity-PassthroughCameraApiSamples](https://github.com/oculus-samples/Unity-PassthroughCameraApiSamples)
- [Ultralytics YOLO11](https://github.com/ultralytics/ultralytics)
- [Unity Inference Engine](https://docs.unity3d.com/Packages/com.unity.ai.inference@latest)
- Object classes from the [COCO dataset](https://cocodataset.org/)
