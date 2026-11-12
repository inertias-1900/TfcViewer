# TFC Biome Viewer v1.1.0

An offline biome **and climate** viewer for TerraFirmaCraft (MC 1.21 / NeoForge 21.1 world generation; the seasonal climate engine is ported from the 1.20.x branch that shipped in TerraFirmaCraft-Forge-1.20.1-3.2.22). Enter a world seed → biome / temperature / rainfall / climate maps with click-to-inspect, multi-select highlighting and an overview minimap. **Fully vibecoded by 什亭之匣 (Shitingzhixia).**

## Download

- **Windows x64**: `TfcViewerGui_win-x64.zip` — single exe + 中/EN usage notes (requires [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0))
- Source: this repository (engine is zero-dependency .NET 8; GUI needs `net8.0-windows`)

## Headline: in-game seasonal climate, offline

v1.0.x showed the **world-generation** temperature/rainfall values. v1.1.0 adds TFC's **runtime climate model**, so the numbers you see are the ones the game actually uses.

```
temperature(y, x, z, calendarTicks)
  = averageTemp(x, z)                     // 128-block climate grid, 4-corner bilinear interp
  + monthFactor(calendarTicks) × triangle(-3, 15, 1/(2·temperatureScale), z)
  + dailyVariation(calendarTicks)          // ±3.9 °C, seeded by day
  − altitudeCorrection(y)                  // sea level 63, −0.16225 °C/block, cap 17.822
```

Everything that was previously guessed is now read out of the actual artefacts rather than memory:

- **Month modifier table** (12 values, January −1 → July +1) taken from the 1.20.x source and cross-checked against the shipped jar constants.
- **`java.util.Random` and Minecraft's `LinearCongruentialGenerator`** reimplemented bit-exactly; the LCG was verified by disassembling `LinearCongruentialGenerator.class` from a local 1.20.1 SRG client jar (`seed*(seed*A+B)+salt`).
- **`SEA_LEVEL_Y = 63`**, read from `TFCChunkGenerator.class` field `ConstantValue` — not assumed 64.
- **Save files are readable without launching the game**: `level.dat` seed + `data/tfc_calendar.dat`. Verified on a real save (`calendarTime = 966074` → year 1000, 6 月 1 日 06:04).

### Why the UI does not ask for a date

The viewer reports **date-independent ranges**: year-low / year-high / year-mean for a coordinate, computed by sweeping a full year at hourly resolution (seasonal envelope precomputed once, then two multiply-adds per coordinate). You never need to pick a season, and the map remains a stable climate map.

## Engine

- `src/Climate.cs` — `JavaRandom`, `LinearCongruentialGenerator`, `TfcMonth` / `TfcCalendar`, `ClimateModel` (`OverworldClimateModel` port), `ClimateProfile` (hourly seasonal envelope → year min/max/mean), `ClimateQuery` (`ChunkData.getAverageTemp`/`getRainfall` semantics: 128-block grid + per-chunk bilinear interpolation).
- `src/KoppenClimate.cs` — TFC's real climate classification (`KoppenClimateClassification`, 12 classes, thresholds copied 1:1).
- `src/Nbt.cs` — gzip/NBT reader + `TfcSave` (seed + calendar state from a save directory).
- CLI:
  - `tfcviewer --info <seed> <x> <z> [--y=63] [--days=8] [--ticks=…] [--save=<save dir>]`
  - `tfcviewer --selftest [seed] [days/month]` — cross-checks the envelope against the per-tick formula (7/7 points, max error 0.0000)
  - `tfcviewer <seed> [size] [blocks/px] [temp_mean|temp_min|temp_max|rainfall|biome] [out.png]`

## GUI

- **Filter modes replace the layer button**: `Biome / Temperature / Rainfall / Climate`. A filter mode recolours the map, rebuilds the legend, and changes what click-highlighting selects. The legend shows each category's share of the visible area.
- **Köppen climate classification** in the info bubble, alongside the worldgen zone (the old "climate" line was actually the biome-selection threshold lines; it is now labelled **worldgen zone / 预测群系类型**).
- Info bubble: chunk/block coords, biome, base temperature, **year range (low ~ high)**, year mean + swing, rainfall, worldgen zone, Köppen class, winter behaviour (freezes / snow settles / no frost), confidence.
- **Latitude ticks** on both canvas edges (TFC latitude axis: `z = 0` is the equator, `|z| = temperatureScale/2` maps to 90°).
- **Parallel rendering**: the view is split into row bands, each rendered by its own `RegionGenerator` (the engine's caches are not thread-safe, and construction costs only ~15 ms). Roughly an order of magnitude faster than the v1.0.x single-threaded path.
- Fixed minimap click mapping — X and Y previously used different world scales on a non-square widget, which made jumps land off-target.
- Removed: the drag-mode toggle and right-drag panning. Navigation is minimap-driven again.

## Migration notes

- "Temperature" in the bubble/legend now means **seasonal** temperature at y=63 (sea level). The world-generation value is still shown as **base temperature**.
- The `temperature` / `temp_min` / `temp_max` CLI layers correspond to year-mean / year-low / year-high.

## Known limits

- River humidity (+275 rainfall near wide rivers, applied in `RegionChunkDataGenerator`) is **not** modelled — rainfall is the interpolated grid value.
- Köppen in TFC has **no monsoon (Am) class**; the mod itself only distinguishes tropical rainforest vs tropical savanna and its source comments call the classification "simplified, purely decorative".
- Altitude-dependent temperatures use a fixed `y=63` for map layers; use `--y=` in the CLI (or read the bubble note) for other heights.
- GUI interaction was verified by compilation and logic review; the rendering timings quoted above are estimates from sampling cost, not measured on the release machine.

## Confidence

Unchanged from v1.0.x: % of the ~20×20 block neighbourhood matching the center biome. Generation is deterministic — low confidence means the sampled spot sits in a mixed region, not that the result may be wrong.
