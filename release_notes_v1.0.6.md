# TFC Biome Viewer v1.0.6

An offline biome viewer for TerraFirmaCraft (MC 1.21 / NeoForge 21.1). Enter a world seed → biome/temperature/rainfall maps with click-to-inspect, multi-select highlighting and an overview minimap. **Fully vibecoded by 什亭之匣 (Shitingzhixia).**

## Download

- **Windows x64**: `TfcViewerGui_win-x64.zip` — single exe + 中/EN usage notes (requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0))
- Run: `TfcViewerGui.exe <seed>` (numeric, or any string — Java-hashCode like Minecraft, e.g. `testseed2`)

## What's in v1.0.6

- Engine verified **pixel-perfect 100% identical** to TFC Java logic (512×512 full image, 0/262144 diffs on seeds `testseed2` & `12345`; rivers 562/task probes/dumps all match)
- GUI:
  - Biome / Temperature / Rainfall layers; wheel zoom; click info bubble (chunk/block coords, biome, temperature, rainfall, climate, confidence)
  - Left legend with **multi-select highlight** (selected biomes keep color + white outline, others grayscale) & "✕ clear selection"
  - Right panel: status area (top) + overview minimap with white view frame & **Drag mode** toggle (pan canvas, re-render on release)
  - **中英双语 UI** (top-right toggle): biome names as 中文名(tfc:xxx) in zh, raw `tfc:xxx` in en; climate names localized (Ice sheet/冰盖 etc.)
  - Click inside the info bubble to dismiss it; fixed window; wheat icon; "Rendering..." overlay while recomputing
- CLI included in source (`tfcviewer <seed> [size] [blocks/px] [layer] [out.png]`)
- Docs: README (en) + README.zh-CN.md + 使用说明.txt + ReadMe_EN.txt; confidence semantics documented (when it drops below 100%)

## Confidence

Heuristic: % of the ~20×20 block neighborhood matching the center biome. Below 100% at biome borders, coastlines, near rivers, high zoom-out, and fragmented karst biomes. Generation is deterministic — low confidence ≠ possible error, it just means the sampled spot is in a mixed region (zoom in / lower blocks-per-pixel and click again to pinpoint).