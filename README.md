# 🌐 PanoProjection — Panoramic Image Painting Studio

A **GPU-accelerated panoramic image painting tool** built in Unity. Load equirectangular panorama images, view them with perspective and fisheye projection, and draw directly on the spherical surface with distortion-aware brushes.

---

## ✨ Features

### 🎨 Painting System
- **Spherical Brush** — GPU-rendered brush that maintains correct shape on the sphere surface
- **Spherical Eraser** — Erase paint strokes with geometry-aware brush
- **Brush Opacity** — Build up color gradually with adjustable opacity
- **Brush Hardness** — Control edge softness from sharp to feathered
- **Smart Brush** — Automatically scales brush size with zoom level
- **Straight Line Tool** — Hold `Alt` and drag for great-circle lines
- **Eyedropper** — Press `I` and click to sample color from the panorama
- **Color History** — Last 12 used colors tracked and clickable

### 📐 Perspective Ruler
- **3-Point Snap** — Lock strokes to 3-point perspective vanishing lines
- **45° Diagonal Snap** — Extended diagonal vanishing point snapping
- **Visual Grid Overlay** — Three-axis grid with customizable appearance
- **Ghost Line Preview** — Real-time snap-locked direction preview

### 📷 Projection Engine
- **Perspective ↔ Fisheye** — Blend between rectilinear and stereographic fisheye
- **Wide FOV Range** — 6° telephoto to 160° ultra-wide
- **Full 360° Navigation** — Pan, tilt, and roll the virtual camera

### 🖥️ User Interface
- **Dark Theme Panel** — Modern semi-transparent UI with teal accent colors
- **Collapsible Sections** — Click headers to collapse/expand sections
- **Scrollable Panel** — Panel scrolls when content exceeds screen height
- **Full HSV Color Picker** — Hue + Saturation + Brightness + quick presets
- **Help Overlay** — Press `H` for complete keyboard/mouse reference
- **Minimap** — Real-time thumbnail showing viewport position
- **Status Bar** — Bottom bar with active tool and navigation hints
- **View Presets** — Quick views (Front/Right/Back/Left/Top/Bottom) + zoom presets
- **Vignette Effect** — Adjustable edge darkening
- **Animated Cursor** — Pulsing anti-aliased brush ring with inner glow
- **Crosshair** — Optional center indicator

### 💾 File Operations
- **Save/Load Panorama** — JPEG export with paint baked in, load any equirectangular image
- **Export Overlay** — Save paint layer as transparent PNG
- **Clear Canvas** — Wipe all paint with undo support
- **Undo / Redo** — Up to 20 history steps

---

## ⌨️ Controls

### Navigation (Blender-style)

| Input | Action |
|---|---|
| `Middle Mouse Drag` | Orbit / Rotate View |
| `Shift + MMB Drag` | Roll |
| `Ctrl + MMB Drag` | Zoom |
| `Scroll Wheel` | Zoom in/out |
| `Space + LMB Drag` | Orbit (alternative) |

### View Presets

| Key | Action |
|---|---|
| `Numpad 1` | Front view |
| `Numpad 3` | Right view |
| `Numpad 7` | Top view |
| `Numpad 5` / `Home` | Reset view |

### Painting

| Key | Action |
|---|---|
| `Q` / `B` / `P` | Brush tool |
| `E` | Eraser tool |
| `I` | Eyedropper (pick color) |
| `Alt + Drag` | Straight Line tool |
| `Ctrl + Z` | Undo |
| `Ctrl + Shift + Z` | Redo |

### File

| Key | Action |
|---|---|
| `Ctrl + S` | Save Panorama |
| `Ctrl + D` | Load Panorama |

### Grid & Snapping

| Key | Action |
|---|---|
| `G` | Toggle Grid |
| `Shift + G` | Align Grid to view |
| `S` | Toggle Snap |
| `F` | Toggle 45° Snap |
| `Shift` (hold) | Temporary Snap |

### UI

| Key | Action |
|---|---|
| `W` | Toggle Panel |
| `H` | Toggle Help overlay |
| `M` | Toggle Minimap |

---

## 🚀 Getting Started

### Requirements
- **Unity 2021.3 LTS** or newer (Built-in Render Pipeline)
- Windows (uses Win32 file dialogs)

### Setup
1. Clone or download this repository
2. Open the project in Unity
3. Open `Assets/Scenes/SampleScene.unity`
4. Assign a panoramic image to `Panorama Texture` on the Main Camera
5. Press **Play** and start painting!

### Project Structure
```
Assets/
├── Scripts/
│   ├── PanoramaPaintGPU.cs         # Core painting engine
│   ├── PanoramaProjectionEffect.cs # Camera projection post-process
│   ├── PanoramaUI.cs               # Dark-themed UI system
│   └── SimpleFileBrowser.cs        # Windows file dialog wrapper
├── Shaders/
│   ├── PanoramaBrush.shader        # Spherical brush (draw)
│   ├── PanoramaEraser.shader       # Spherical eraser
│   ├── PanoramaGridComposite.shader # Grid overlay + snap visualization
│   └── PanoramaProjection.shader   # Projection + vignette + cursor
└── Scenes/
    └── SampleScene.unity
```

---

## 🛠️ Tech Stack

| Component | Technology |
|---|---|
| Engine | Unity (Built-in Render Pipeline) |
| Language | C#, HLSL/CG |
| Rendering | `OnRenderImage` post-processing, `GL` immediate mode |
| UI | Unity IMGUI with custom dark styling |
| File I/O | Win32 P/Invoke |

---

## 📝 License

This project is part of a minor project submission.
