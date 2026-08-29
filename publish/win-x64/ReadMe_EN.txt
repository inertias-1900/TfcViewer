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
    Layer         Biome / Temperature / Rainfall
    English UI ⇄ 中文界面   Language toggle (top-right corner)

Left legend (biome layer only):
    Lists biomes present in the current view; click to multi-select.
    Selected biomes stay colored with a white outline; everything else turns grayscale.
    "✕ Clear selection" resets. In the English UI biome names are shown as raw
    tfc:xxxx ids; in the Chinese UI they show as 中文名(tfc:xxxx).

Right panel:
    Top: status area — center coords, view span (chunks/blocks/km), blocks/px, layer, render state.
    Bottom: overview minimap (8x the main view span) with a white frame marking the
    current view; click or drag inside to jump. Above it is the "Drag mode" toggle
    (stays pressed while active): in drag mode, click/pan the main canvas with LMB —
    the frame moves while dragging and the main view re-renders when you release.

Main canvas (512x512 px, default 512x512 chunk view):
    View mode (default): click anywhere for an info bubble:
        Chunk: (chunkX, chunkZ) / Block: (bx, bz)
        Biome / Temperature / Rainfall / Climate / Confidence
    Click inside the bubble to dismiss it; click outside to open a new one.
    Mouse wheel zooms around the cursor (view mode only).
    A gray-white overlay with "Rendering..." shows while re-rendering.

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