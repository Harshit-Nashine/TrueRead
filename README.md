# TrueRead — On-Device Hindi Character Recognition
### An end-to-end AI system for learning Devanagari, built for low-end Android devices

<p align="center">
  <img src="results/demo_banner.png" alt="TrueRead Demo" width="700"/>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/Platform-Android-3DDC84?logo=android&logoColor=white" />
  <img src="https://img.shields.io/badge/Engine-Unity%202022.3%20LTS-000000?logo=unity&logoColor=white" />
  <img src="https://img.shields.io/badge/ML%20Framework-TensorFlow%202.15-FF6F00?logo=tensorflow&logoColor=white" />
  <img src="https://img.shields.io/badge/Inference-Unity%20Sentis%20(ONNX)-blueviolet" />
  <img src="https://img.shields.io/badge/Test%20Accuracy-99%25-brightgreen" />
  <img src="https://img.shields.io/badge/Macro%20F1-0.99-brightgreen" />
</p>

---

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Inference Pipeline](#inference-pipeline)
- [Results & Evaluation](#results--evaluation)
- [Project Structure](#project-structure)
- [Setup & Training](#setup--training)
- [Unity Deployment](#unity-deployment)
- [Key Engineering Decisions](#key-engineering-decisions)
- [Roadmap](#roadmap)

---

## Overview

TrueRead is a real-time, camera-based Hindi character recognition app. Point your phone camera at any handwritten or printed Devanagari character and the app identifies it instantly — then shows a 3D visualization of the character, associated vocabulary words, and a spaced-repetition quiz system.

The entire ML pipeline runs **on-device** with no internet connection required, using a quantized ONNX model executed via Unity Sentis.

**What makes this challenging:**
- **46 visually similar classes** — Devanagari consonants share structural elements (e.g., ब/व, ध/घ, थ/य)
- **Low-end hardware target** — Helio G85 SoC, 4 GB RAM, no dedicated NPU
- **Real-world variance** — handwritten input vs. clean printed training data
- **On-device inference** — no cloud round-trip; must run in <1.5s per scan

---

## Architecture

The model is a **Mini-ResNet with Residual Connections**, chosen over a Depthwise Separable CNN because residual skip connections learn more discriminative features for visually similar character pairs.

```python
def residual_block(x, filters, stride=1, name='rb'):
    """
    Standard ResNet residual block.
    Main path:  Conv(3×3) → BN → ReLU → Conv(3×3) → BN
    Shortcut:   identity  (or 1×1 projection if shape changes)
    Output:     Add(main, shortcut) → ReLU
    """
    shortcut = x
    x = layers.Conv2D(filters, 3, strides=stride, padding='same', use_bias=False)(x)
    x = layers.BatchNormalization()(x)
    x = layers.ReLU()(x)
    x = layers.Conv2D(filters, 3, strides=1, padding='same', use_bias=False)(x)
    x = layers.BatchNormalization()(x)

    if stride != 1 or int(shortcut.shape[-1]) != filters:
        shortcut = layers.Conv2D(filters, 1, strides=stride, padding='same', use_bias=False)(shortcut)
        shortcut = layers.BatchNormalization()(shortcut)

    return layers.ReLU()(layers.Add()([x, shortcut]))


def build_trueread_model(num_classes=46, img_size=64):
    inputs = keras.Input(shape=(img_size, img_size, 1), name='input')
    x = layers.Conv2D(32, 3, padding='same', use_bias=False, name='stem_conv')(inputs)
    x = layers.BatchNormalization(name='stem_bn')(x)
    x = layers.ReLU(name='stem_relu')(x)
    x = residual_block(x, 32,  stride=1, name='rb1')   # 64×64×32
    x = residual_block(x, 64,  stride=2, name='rb2')   # 32×32×64
    x = residual_block(x, 128, stride=2, name='rb3')   # 16×16×128
    x = residual_block(x, 128, stride=1, name='rb4')   # 16×16×128
    x = residual_block(x, 256, stride=2, name='rb5')   #  8×8×256
    x = layers.GlobalAveragePooling2D(name='gap')(x)
    x = layers.Dropout(0.4, name='dropout')(x)
    outputs = layers.Dense(num_classes, activation='softmax', name='predictions')(x)
    return keras.Model(inputs, outputs, name='TrueRead_MiniResNet_v3')
```

**Model summary:**

| Layer | Output Shape | Parameters |
|---|---|---|
| Stem Conv2D + BN | 64×64×32 | 320 |
| ResBlock 1 (×2 conv) | 64×64×32 | ~18k |
| ResBlock 2 (stride 2) | 32×32×64 | ~74k |
| ResBlock 3 (stride 2) | 16×16×128 | ~295k |
| ResBlock 4 | 16×16×128 | ~295k |
| ResBlock 5 (stride 2) | 8×8×256 | ~1.18M |
| GlobalAveragePooling | 256 | 0 |
| Dropout (0.4) | 256 | 0 |
| Dense (softmax) | 46 | 11,822 |

**Input:** 64×64 grayscale image, normalized to [0, 1]  
**Output:** Softmax probability vector over 46 classes

---

## Inference Pipeline

A critical design principle: **training preprocessing and inference preprocessing are completely separate pipelines.** The training dataset is pre-cleaned (64×64, black background, white strokes). The inference pipeline handles the messiness of real-world camera input.

```
Camera Frame (1280×720)
        │
        ▼
┌───────────────────────────┐
│  1. Grayscale conversion  │  Rec. 601 luma formula
│  2. Downsample → 256×256  │  Bilinear — 6.6× less work for all downstream steps
│  3. Median blur (3×3)     │  Removes paper texture noise without blurring strokes
│  4. Otsu binarization     │  Adaptive threshold — works in any lighting condition
│  5. Auto-inversion        │  Detects white-on-black vs black-on-white automatically
│  6. Largest component     │  BFS — removes edge noise, preserves full character
│  7. BBox crop + 15% pad   │  Tight crop with safety margin
│  8. Aspect-ratio pad      │  Square pad without distortion
│  9. Resize → 64×64        │  Bilinear resize to model input size
│  10. Final Otsu pass      │  Re-binarizes after resize interpolation blur
└───────────────────────────┘
        │
        ▼
ONNX Model (Unity Sentis / GPUCompute backend)
        │
        ▼
Softmax[46] → confidence gate (≥0.70) → ShowCharacter() or "Hold steady..."
```

**Key engineering notes:**
- Step 2 (downsample to 256×256 before BFS) reduced per-scan CPU time by ~6.6× — critical for the Helio G85
- Step 6 uses pre-allocated BFS arrays (`bool[]`, `int[]`) — zero heap allocation per scan, eliminating GC pauses
- Step 5 (auto-inversion) is the most failure-prone step; a debug visualizer is built into the pipeline
- The confidence gate (0.70) prevents the model from confidently displaying wrong characters

---

## Results & Evaluation

The model was evaluated on a held-out test set of **11,730 samples** (46 classes, ~255 samples per class).

### Summary Metrics

| Metric | Score |
|---|---|
| **Test Accuracy** | **99.0%** |
| **Macro Avg Precision** | **0.99** |
| **Macro Avg Recall** | **0.99** |
| **Macro Avg F1-Score** | **0.99** |
| **Weighted Avg F1-Score** | **0.99** |

### Confusion Matrix

<p align="center">
  <img src="results/confusion_matrix.png" alt="Confusion Matrix — TrueRead v3" width="720"/>
</p>

> The matrix is almost entirely diagonal, indicating strong per-class discrimination across all 46 characters.

### Top Confusions (errors > 1)

These are the hardest pairs — all are visually similar Devanagari characters:

| True Label | Predicted As | Errors | Why |
|---|---|---|---|
| `character_17_tha` (थ) | `character_26_yaw` (य) | 7 | Shared horizontal crossbar structure |
| `character_11_taamatar` (ट) | `digit_8` (८) | 7 | Similar circular top element |
| `character_29_waw` (व) | `character_16_tabala` (त) | 6 | Similar triangular base |
| `character_19_dha` (ध) | `character_4_gha` (घ) | 6 | Near-identical loop structure |
| `character_14_dhaa` (ढ) | `character_18_da` (द) | 6 | Same base, differing upper hook |
| `character_23_ba` (ब) | `character_20_na` (न) | 4 | Similar left vertical stroke |

### Per-Class F1 Highlights

| Class | F1 | Class | F1 |
|---|---|---|---|
| क (Ka) | 1.00 | ब (Ba) | 0.98 |
| ख (Kha) | 1.00 | थ (Tha) | 0.98 |
| ह (Ha) | 1.00 | ध (Dha) | 0.99 |
| All digits (०–९) | 1.00 | ढ (Dhaa) | 0.98 |

All 46 classes achieved F1 ≥ 0.98. Digits achieved perfect 1.00 F1 across the board.

---

## Project Structure

```
TrueRead/
│
├── ml/
│   ├── TrueRead_v3_Training_Pipeline.ipynb   # Full training notebook (Google Colab)
│   ├── class_mapping.json                    # Index↔label mapping (shared with Unity)
│   └── requirements.txt                      # Python dependencies
│
├── unity/
│   ├── Assets/
│   │   ├── Scripts/
│   │   │   ├── SentisInferenceManager.cs     # ONNX inference + preprocessing pipeline
│   │   │   ├── CameraManager.cs              # WebCamTexture capture + orientation fix
│   │   │   ├── ScanManager.cs                # Scan loop, auto-dismiss, gallery support
│   │   │   ├── ScanSceneController.cs        # AR display setup (URP single-camera)
│   │   │   ├── ModelDisplayManager.cs        # 3D CharPackage spawning
│   │   │   ├── PackageController.cs          # Character animation + audio chain
│   │   │   ├── DigitController.cs            # Digit-specific bounce animation
│   │   │   ├── QuizManager.cs                # Spaced repetition quiz engine
│   │   │   ├── StatsManager.cs               # XP, levels, badges, day streaks
│   │   │   ├── StatsDisplay.cs               # Stats scene UI population
│   │   │   ├── SceneNavigator.cs             # Scene transitions + EventSystem guard
│   │   │   ├── TutorialManager.cs            # First-launch onboarding cards
│   │   │   ├── CharacterData.cs              # ScriptableObject per character
│   │   │   ├── GalleryImageBackground.cs     # Gallery image as AR background
│   │   │   └── BootstrapLoader.cs            # Bootstrap → MainMenu scene loader
│   │   ├── Models/
│   │   │   └── trueread_v3_model.onnx        # ← copy output from Colab here
│   │   └── StreamingAssets/
│   │       └── class_mapping.json            # ← copy output from Colab here
│
├── results/
│   ├── confusion_matrix.png                  # Generated by Colab Cell 10
│   └── training_curves.png                   # Generated by Colab Cell 10
│
└── README.md
```

---

## Setup & Training

### Prerequisites

- Google Colab account (free tier works; T4 GPU recommended)
- Google Drive with dataset uploaded
- Python 3.10 (managed by Colab)

### Dataset Layout

```
TrueRead_Dataset/
├── train/
│   ├── character_1_ka/        (~2000 images)
│   ├── character_2_kha/
│   ├── ...
│   ├── character_36_gya/
│   ├── digit_0/
│   └── ...
│       digit_9/
└── test/
    └── (same structure)
```

Folder naming convention: `character_{number}_{phonetic}` and `digit_{0-9}`. The natural sort in Cell 5 guarantees a deterministic class-to-index mapping regardless of filesystem order.

### Run Training

1. Open `ml/TrueRead_v3_Training_Pipeline.ipynb` in Google Colab
2. Set `Runtime → Change runtime type → T4 GPU`
3. Edit `DRIVE_DATASET_PATH` in **Cell 3** to point to your Drive folder
4. Run all cells (`Runtime → Run all`)
5. Collect outputs from `model_outputs_v3/` on your Drive:
   - `trueread_v3_model.onnx`
   - `trueread_v3_model.tflite`
   - `class_mapping.json`

**Estimated training time:** 30–50 minutes on T4 GPU.

### Install Dependencies (local evaluation only)

```bash
pip install -r ml/requirements.txt
```

---

## Unity Deployment

### Steps

1. Copy `trueread_v3_model.onnx` → `Assets/Models/`
2. Copy `class_mapping.json` → `Assets/StreamingAssets/`
3. In Unity, select the `.onnx` file → Inspector → Import as **Sentis Model Asset**
4. Drag the Model Asset into the `SentisInferenceManager` Inspector slot
5. Run **Colab Cell 16** — copy the printed tensor names into the Inspector:
   - `Input Tensor Name`
   - `Output Tensor Name`
6. Build for Android (`File → Build Settings → Android → Build And Run`)

### ONNX Export Details

| Property | Value |
|---|---|
| Input tensor name | `input` (verify from Cell 16) |
| Output tensor name | `predictions` (verify from Cell 16) |
| Input shape | `[1, 64, 64, 1]` (batch=1, H, W, channels=1) |
| Output shape | `[1, 46]` (batch=1, num_classes) |
| Opset version | 13 |
| Backend | GPUCompute (falls back to CPU if unavailable) |

### Confidence Threshold

The `SentisInferenceManager` has a `minConfidence` slider (default **0.70**). If genuine characters are being rejected, lower to 0.60. If wrong characters are occasionally displayed, raise to 0.75. The confidence gate was implemented because Label Smoothing (ε=0.1) produces calibrated uncertainty — the model genuinely outputs lower confidence on ambiguous inputs, making the gate meaningful.

---

## Key Engineering Decisions

### Why Mini-ResNet over MobileNet / DepthwiseSeparable CNN?

Residual skip connections allow the network to learn **difference features** between visually similar classes. For the ब/व and ध/घ confusion pairs, the discriminating stroke is a single small loop or hook. Skip connections preserve these fine-grained gradients during backpropagation. MobileNet achieves smaller parameter count but showed higher confusion rates on look-alike pairs during experiments.

### Why Focal Loss + Label Smoothing?

v2 of the model achieved 99% test accuracy but was confidently wrong on real handwriting. The root cause was over-confident softmax outputs from hard one-hot training — the model learned to push the correct class to 0.99+ regardless of input ambiguity.

- **Label Smoothing (ε=0.1):** Redistributes 10% of probability mass to wrong classes. Forces calibrated confidence — ambiguous inputs now output ~0.55 instead of ~0.94.
- **Focal Loss (γ=2, α=0.25):** Downweights easy examples, forcing the model to focus training on hard look-alike pairs.

### Why downsample to 256×256 before BFS?

The camera crop is ~660×660 pixels. BFS on 660×660 = 435k pixel operations. BFS on 256×256 = 65k operations — a **6.6× reduction**. Combined with pre-allocated BFS arrays (no heap allocation), this eliminated the overheating issue on Helio G85 (MediaTek) devices during continuous scan.

### Why was MorphOpen removed from inference preprocessing?

v5 used 3×3 erosion (MorphOpen) to remove noise before BFS. Devanagari characters like **द** have thin connecting strokes (~2–3px at 256px resolution). Erosion requires all 8 neighbours to be white — this severed connecting strokes, splitting the character into two blobs. BFS then kept only the larger blob, discarding part of the character. Removing MorphOpen fixed this; LargestComponent alone handles noise because small noise blobs are naturally smaller than any character.

### Single-camera URP rendering

Unity's Universal Render Pipeline does not support multiple cameras in the same way as the Built-in pipeline. Instead of a second background camera, the camera feed canvas is attached to the main camera at `planeDistance=100m` with `sortingOrder=-10`. The 3D models render at 3m depth, so they appear in front of the background canvas.

---

## Roadmap

- [ ] Vowels and matras (dependent vowel signs) — 16 additional classes
- [ ] Two-word and compound character recognition
- [ ] Cloud leaderboard (Firebase) to replace local PlayerPrefs
- [ ] Hindi word recognition mode (full word segmentation pipeline)
- [ ] iOS build via Unity Cloud Build

---

## License

This project is released under the **MIT License**. The Devanagari character dataset used for training is not included in this repository — see the notebook for dataset preparation instructions.

---

<p align="center">
  Built with TensorFlow · Unity Sentis · TextMeshPro · NativeGallery
</p>
