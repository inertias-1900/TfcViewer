# TFC Biome Viewer

An **offline biome viewer** for TerraFirmaCraft (MC 1.21 / NeoForge 21.1): no Java, no game client required — type in a world seed and get biome, temperature and rainfall maps, with click-to-inspect, multi-select highlighting, and an overview minimap.

> This tool (engine port + CLI + WinForms GUI) was **fully vibecoded by 什亭之匣 (Shitingzhixia — "The Ark of Shittim")**.
> The engine is a zero-dependency .NET 8 port verified **pixel-perfect 100% identical** to the Java/TFC logic (512×512 full-image diff = 0 on seeds `testseed2` and `12345`).

## Features

- **Main canvas**: Biome / Temperature / Rainfall layers; mouse-wheel zoom around cursor; click for an info bubble in default view mode
- **Click bubble**: chunk coords, block coords, biome, temperature, rainfall, climate (TFC threshold-based), confidence (heuristic)
- **Left legend**: lists biomes present in the current view (Chinese names with IDs in zh UI, raw `tfc:xxx` in English UI); click to **multi-select** — unselected biomes turn grayscale, selected keep their color with a white outline; "✕ Clear selection" deselects all
- **Overview minimap** (right panel, bottom): white frame marks the current view; click/drag to jump. "Drag mode" toggle button above it — hold LMB to pan the canvas (re-renders on release)
- **Language**: toggle button in the top-right corner — `English UI` ⇄ `中文界面`
- **CLI**: render PNGs from the command line (`tfcviewer <seed> [size] [blocks_per_pixel] [layer] [out.png]`)

## Download & Usage

- Windows x64 build: `publish/TfcViewerGui_win-x64.zip` (requires .NET 8 Desktop Runtime)
- Run: `TfcViewerGui.exe <seed>` (numeric, or any string — hashed like Minecraft does, e.g. `testseed2`)

## Confidence & Preview Uncertainty

"Confidence" in the info bubble is a **heuristic**: the fraction of the 5×5 quart grid (≈20×20 blocks) around the clicked position that shares the center biome (0–100%). It is not a probability — it measures how homogeneous the sampled area is.

**Confidence drops below 100% when:**

- **Biome borders / transition zones** — mountain↔plains, forest↔grassland and other large edges mix several biomes nearby;
- **Coastlines** — the 1–2 pixel band between ocean and coastal biomes;
- **Near rivers** — a river and its fractal buffer overlays several biomes;
- **High zoom-out** — with a large blocks/pixel value, one pixel represents a 16×16 block area or more, possibly spanning multiple biomes;
- **Small-scale interleaved biomes** — karst families (cenote / doline / stone forests) are naturally fragmented with tiny homogeneous patches.

Note: world generation is **deterministic** — the same seed and position always yields the same result. Low confidence does not mean the result may be wrong; it means the sampled spot sits in a mixed region. To pinpoint, lower the "Blocks/px" value (or zoom in with the wheel) and click again.

## Building from Source

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

## Repository Layout

- `src/` — engine (Core/Mth/random, Noise, Settings, Region tasks, river generation, biome layer chain) + CLI + query API
- `gui/` — WinForms viewer (shares engine sources)
- `publish/` — Windows release package

## Verification

- River generation: 562 rivers, identical to Java; per-value random-state probe identical
- Region data: 15,251-point full dump (biome/land/ocean/river/lake flags) — 0 diffs
- 512×512 rendering: seeds `testseed2` (= -1162099057) and `12345` — **0/262,144 pixel diffs**

## License Notes

An independent offline re-implementation of the world-gen logic of [TerraFirmaCraft](https://www.curseforge.com/minecraft/mc-mods/terrafirmacraft) (EUPL-1.2), using official biome color data and the official zh_cn translations for Chinese names. No third-party libraries, no game assets included.