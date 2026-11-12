# Biome Viewer for TerraFirmaCraft

> **100% pixel-verified offline seed previewer** — enter a world seed and instantly see biome, temperature, and rainfall maps. No Java, no Minecraft client required.

---

## What It Does

This is a standalone Windows desktop tool (with a CLI alternative) that **re-implements the TerraFirmaCraft world generation engine** in C# and renders biome maps offline. It produces output **pixel-perfect identical** to what the game would generate — verified against the Java reference on multiple seeds with zero differences across 262,144 pixels.

**Use case:** Preview any TFC world seed before committing to a long-term survival world. See where you'll spawn, find specific biomes, check climate zones, and trace river systems — all without launching the game.

---

## Features

### Interactive GUI

- **Biome / Temperature / Rainfall layers** — switch between map views with a single click
- **Click-to-inspect** — click any point to see chunk coordinates, biome name, temperature, rainfall, climate zone, and a confidence indicator
- **Multi-select highlight** — select multiple biome types in the legend; unselected biomes turn grayscale, selected ones keep their color with a white outline for easy spotting
- **Overview minimap** — a small overview panel shows the full map with a white frame marking your current viewport; click or drag to jump
- **Drag mode** — hold and drag to pan the canvas; re-renders on release for smooth navigation
- **Mouse-wheel zoom** — zoom in/out around the cursor position
- **Bilingual UI** — toggle between English and 中文 with the button in the top-right corner

### Command-Line Interface

Render PNG maps from the terminal — ideal for scripting or quick checks:

```
tfcviewer <seed> [size] [blocks_per_pixel] [layer] [output.png]
```

Example: `tfcviewer myseed 512 16 biome map.png`

---

## How Confidence Works

Each info bubble shows a **confidence percentage** (0–100%). This is a heuristic: the fraction of the ~20×20 block neighborhood around the clicked point that shares the same biome as the center.

**Confidence drops below 100% when:**

| Scenario | Why |
|---|---|
| Biome borders / transition zones | Large edges (mountain ↔ plains, forest ↔ grassland) mix several biomes |
| Coastlines | The 1–2 pixel band between ocean and coastal biomes |
| Near rivers | River fractal buffers overlay multiple biomes |
| High zoom-out | One pixel covers a large area spanning multiple biomes |
| Fragmented karst biomes | Cenote, doline, and stone forest biomes are naturally small and scattered |

> **Note:** World generation is deterministic — the same seed and position always produces the same result. Low confidence does not mean the tool is wrong; it means the sampled spot is in a mixed region. Zoom in or lower the blocks-per-pixel value and click again for a more precise reading.

---

## Verification

This tool was verified against the original Java/TFC implementation with the following results:

| Test | Result |
|---|---|
| Seed `testseed2` (= -1162099057), 512×512 full image | **0 / 262,144 pixel diffs** |
| Seed `12345`, 512×512 full image | **0 / 262,144 pixel diffs** |
| River generation | 562 rivers — identical count and position |
| Region data dump (15,251 points) | 0 diffs across biome/land/ocean/river/lake flags |
| Random-state probe after each generation stage | Identical sequence on both sides |

---

## Download

**Windows x64** (single exe, ~350 KB):
[`publish/TfcViewerGui_win-x64.zip`](publish/TfcViewerGui_win-x64.zip)

Requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0).

Run: `TfcViewerGui.exe <seed>` — type any numeric seed or a string (hashed like Minecraft).

---

## Build from Source

Requires .NET 8 SDK.

```bash
# GUI (Windows viewer; cross-compile from Linux works)
cd gui
dotnet publish TfcViewerGui.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true

# CLI (engine + command-line rendering; Linux/Windows)
cd ..
dotnet build -c Release
./bin/Release/net8.0/tfcviewer testseed2 512 16 biome out.png
```

---

## Project Layout

```
src/          Engine (Core, Mth, Random, Noise, Settings, Region tasks, river generation, biome layer chain) + CLI + query API
gui/          WinForms viewer (shares engine sources)
publish/      Pre-built Windows release package
```

---

## License

An independent offline re-implementation of the world generation logic of [TerraFirmaCraft](https://www.curseforge.com/minecraft/mc-mods/terrafirmacraft) (EUPL-1.2). Uses official biome color data and the official `zh_cn` translations for Chinese biome names.

No third-party libraries. No game assets included.

---

*Fully vibecoded by [什亭之匣 (Shittim Chest)](https://github.com/inertias-1900/TfcViewer).*
