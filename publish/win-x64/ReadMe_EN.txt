TFC Biome Viewer (TfcViewerGui)
================================

An offline biome viewer for TerraFirmaCraft (TFC). No Java, no game client needed —
enter a world seed to view biome / temperature / rainfall maps, with click-to-inspect
and multi-select highlighting.

This tool was fully vibecoded by 什亭之匣 (Shitingzhixia — "The Ark of Shittim").
The engine is a 0-dependency port of TFC 1.21 world generation, verified
pixel-perfect identical to the Java logic (512x512 full-image diff = 0).

----------------
Requirements
----------------
- Windows 10/11 x64
- .NET 8 Desktop Runtime (if the app won't start, install it):
  https://dotnet.microsoft.com/download/dotnet/8.0  ("Windows Desktop Runtime 8.0.x - x64")

----------------
Usage
----------------
Start: double-click TfcViewerGui.exe, or run:
    TfcViewerGui.exe <seed>
Seed accepts a number, or any string (hashed with Java hashCode like Minecraft, e.g. testseed2).
The window is fixed-size. The app icon is the vanilla MC wheat icon (from Minecraft resources).

Top toolbar:
    Seed          Enter a new seed and click "Load" (default testseed2 = -1162099057)
    Center X / Z  View center in block coordinates; use "Go" or press Enter
    Blocks/px     Blocks per pixel (default 16 = 1 chunk per pixel = 512x512 chunk view;
                  lower it for finer detail; mouse wheel also zooms)
    Layer         (replaced by the Filter modes on the right panel, see below)
    English UI ⇄ 中文界面   Language toggle (top-right corner)

Left legend (follows the active filter mode):
    Lists the categories present in the current view, each with its share of the
    visible area. Biome mode shows 中文名(tfc:xxx) / raw ids; temperature, rainfall
    and climate modes show their bins (e.g. "Mild 10~20", "Tropical rainforest").
    Click to multi-select: selected categories stay colored with a white outline,
    everything else turns grayscale. "✕ Clear selection" resets.

Right panel:
    Top: status area — center coords, view span, blocks/px, active filter, render state.
    Middle: filter modes — Biome / Temperature / Rainfall / Climate. Switching one
      (a) recolours the map, (b) rebuilds the legend, (c) changes what highlighting means.
      Temperature/rainfall/climate are evaluated at sea level (y=63); every 10 blocks
      up is roughly 1.6 °C colder.
    Bottom: overview minimap — left-click (or hold and drag) jumps the main view to
      that point.

Main canvas (512x512 px, default 512x512 chunk view):
    Click anywhere for an info bubble:
        Chunk / Block / Biome / Base temp / Year range (low ~ high) /
        Year mean (swing) / Rainfall / Worldgen zone / Köppen class /
        Winter behaviour / Confidence
    Latitude ticks are drawn on both edges (TFC latitude axis: z=0 equator,
    |z| = temperatureScale/2 → 90°).
    Click inside the bubble to dismiss it; click outside to open a new one.
    Mouse wheel zooms around the cursor.
    A gray-white overlay with "Rendering..." shows while re-rendering.

----------------
Temperature model (seasonal climate)
----------------
Displayed temperatures are the range that actually occurs in-game, not a single
world-generation value:
- Base temp: the 128x128-block climate grid, bilinearly interpolated the way TFC's
  ChunkData does (that is what the game itself reads).
- Winter low / Summer high: the full seasonal swing (month modifier -1..+1 applied
  to a latitude term of 12..18 C), the per-day random offset (+-3 C, seeded by day)
  and the diurnal term (+-0.9 C) are swept over a whole year (hourly), then the
  altitude term is applied (above y=63: -0.16225 C per block, capped at 17.822).
- Mean temp: hourly average over the year = base temp + altitude term
  (the seasonal term cancels out).
- Layers and bubbles use y=63 (sea level). Every 10 blocks up is roughly 1.6 C colder;
  use the CLI --y=<height> flag to check another altitude.
- Nothing here depends on the current game date, and the game does not need to run.
- Cross-check: read a temperature in-game; at the same position and time it should
  fall inside the reported year range (summer near the high, winter near the low).

----------------
Field notes
----------------
- Climate follows the real TFC selection thresholds:
    Ice sheet (temperature < -17 + 0.006 x rainfall)
    Ice edge (< ice line + 2.5)      Paleo ice margin (< ice line + 6)
    Desert (rainfall < 60)           Semi-arid (60-155)
    Semi-humid (250-375)             Humid (>375)
    Hot & humid (>425 and rainfall + 10 x temperature > 500)
    Temperate (everything else)
- Confidence: ratio of the ~20x20 block neighborhood matching the center biome
  (heuristic; world generation itself is deterministic).
  Below 100% happens at: biome borders/transition zones, coastlines, near rivers
  (fractal buffer), high zoom-out (one pixel = 16x16+ blocks spanning mixed areas),
  and small-scale interleaved karst biomes (cenote/doline/stone forests).
  To pinpoint, lower "Blocks/px" (or zoom in) and click again.

----------------
Rebuilding (optional)
----------------
Requires .NET 8 SDK.
    GUI:  cd gui && dotnet publish -c Release -r win-x64
    CLI:  dotnet build -c Release, then bin/Release/net8.0/tfcviewer
          tfcviewer <seed> [size=512 chunks] [blocks_per_pixel=16] [layer] [out.png]
          layer: biome | temp_mean | temp_min | temp_max | rainfall
          single point: tfcviewer --info <seed> <x> <z> [--y=63] [--days=8]
          from a save:  tfcviewer --info x <x> <z> --save=<save dir>
          self-check:   tfcviewer --selftest [seed] [days_per_month]