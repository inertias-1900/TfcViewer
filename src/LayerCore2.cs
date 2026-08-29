using TfcViewer.Core;
using TfcViewer.World.Region;
using TfcViewer.World.River;

namespace TfcViewer.World.Layer;

/// <summary>TypedArea（对象缓存层，等价 Java TypedArea）</summary>
public sealed class TypedArea<T>
{
    public delegate T TypedAreaSource(int x, int z);

    private readonly TypedAreaSource source;
    private readonly long[] keys;
    private readonly object?[] values;
    private readonly int mask;

    public TypedArea(TypedAreaSource source, int maxCacheSize)
    {
        maxCacheSize = Mth.SmallestEncompassingPowerOfTwo(maxCacheSize);
        this.source = source;
        this.keys = new long[maxCacheSize];
        this.values = new object[maxCacheSize];
        this.mask = maxCacheSize - 1;
        Array.Fill(keys, long.MinValue);
    }

    public T Get(int x, int z)
    {
        long key = (long)x & 0xFFFFFFFFL | ((long)z << 32);
        int index = (int)HashCommon.Mix(key) & mask;
        if (keys[index] == key)
        {
            return (T)values[index]!;
        }
        else
        {
            T value = source(x, z);
            values[index] = value;
            keys[index] = key;
            return value;
        }
    }
}

/// <summary>RegionLayer：层链坐标 → region point</summary>
public sealed class RegionLayer
{
    private readonly RegionGenerator generator;
    public RegionLayer(RegionGenerator generator) => this.generator = generator;

    public TypedArea<Region.Region.RegionPoint> Apply(long seed)
    {
        var context = new AreaContext(seed);
        return new TypedArea<Region.Region.RegionPoint>((x, z) =>
        {
            context.SetSeed(x, z);
            return generator.GetOrCreateRegionPoint(x, z);
        }, 1024);
    }
}

/// <summary>RegionBiomeLayer：point → biome id</summary>
public static class RegionBiomeLayer
{
    public static Area.Factory Apply(TypedArea<Region.Region.RegionPoint> prev)
    {
        return () => new Area((x, z) => prev.Get(x, z).Biome, 1024);
    }
}

/// <summary>AdjacentTransformLayer 辅助：四邻域迭代</summary>
public static class LayerHelpers
{
    public static Area.Factory Adjacent(long seed, Area.Factory prev, Func<AreaContext, Area, int, int, int, int, int, int> apply)
    {
        var context = new AreaContext(seed);
        var prevArea = prev();
        return () => new Area((x, z) =>
        {
            context.SetSeed(x, z);
            return apply(context, prevArea,
                prevArea.Get(x, z - 1),
                prevArea.Get(x + 1, z),
                prevArea.Get(x, z + 1),
                prevArea.Get(x - 1, z),
                prevArea.Get(x, z));
        }, 1024);
    }
}

/// <summary>RegionEdgeBiomeLayer（海岸边缘群系过渡）</summary>
public static class RegionEdgeBiomeLayer
{
    public static Area.Factory Apply(long seed, Area.Factory prev)
        => LayerHelpers.Adjacent(seed, prev, (ctx, area, north, east, south, west, center) =>
        {
            bool M(Predicate<int> p) => p(north) || p(east) || p(south) || p(west);

            if (TFCLayers.IsLow(center))
            {
                if (M(TFCLayers.IsOcean) && M(TFCLayers.IsMountains))
                {
                    return TFCLayers.OCEANIC_MOUNTAINS;
                }
                else if (M(TFCLayers.IsOcean) && M(v => v == TFCLayers.LOWLANDS))
                {
                    return TFCLayers.SALT_MARSH;
                }
            }
            if (TFCLayers.IsFlats(center))
            {
                if (M(TFCLayers.IsOcean) && M(TFCLayers.IsFlats))
                {
                    return TFCLayers.CANYONS;
                }
            }
            if (center == TFCLayers.PLATEAU || center == TFCLayers.BADLANDS)
            {
                if (M(v => v == TFCLayers.LOW_CANYONS || v == TFCLayers.LOWLANDS)) return TFCLayers.HILLS;
                else if (M(v => v == TFCLayers.PLAINS || v == TFCLayers.HILLS)) return TFCLayers.ROLLING_HILLS;
            }
            else if (TFCLayers.IsMountains(center))
            {
                if (M(TFCLayers.IsLow)) return TFCLayers.ROLLING_HILLS;
            }
            else if (center == TFCLayers.LOWLANDS || center == TFCLayers.LOW_CANYONS)
            {
                if (M(v => v == TFCLayers.PLATEAU || v == TFCLayers.BADLANDS)) return TFCLayers.HILLS;
                else if (M(TFCLayers.IsMountains)) return TFCLayers.ROLLING_HILLS;
            }
            else if (center == TFCLayers.PLAINS || center == TFCLayers.HILLS)
            {
                if (M(v => v == TFCLayers.PLATEAU || v == TFCLayers.BADLANDS)) return TFCLayers.HILLS;
                else if (M(TFCLayers.IsMountains)) return TFCLayers.ROLLING_HILLS;
            }
            else if (center == TFCLayers.DEEP_OCEAN_TRENCH)
            {
                if (M(v => !TFCLayers.IsOcean(v))) return TFCLayers.OCEAN;
            }
            return center;
        });
}

/// <summary>ShoreAndRiverLayer</summary>
public static class ShoreAndRiverLayer
{
    public static Area.Factory Apply(long seed, Area.Factory prev)
        => LayerHelpers.Adjacent(seed, prev, (ctx, area, north, east, south, west, center) =>
        {
            bool M(Predicate<int> p) => p(north) || p(east) || p(south) || p(west);

            if (TFCLayers.HasShore(center))
            {
                if (M(TFCLayers.IsOcean)) return TFCLayers.ShoreFor(center);
            }
            else if (center == TFCLayers.RIVER_VALLEY)
            {
                if (M(TFCLayers.IsMountains)) return TFCLayers.PLATEAU_WIDE;
                else if (M(TFCLayers.IsOcean)) return TFCLayers.SALT_MARSH;
            }
            return center;
        });
}

/// <summary>MoreShoresLayer</summary>
public static class MoreShoresLayer
{
    public static Area.Factory Apply(long seed, Area.Factory prev)
        => LayerHelpers.Adjacent(seed, prev, (ctx, area, north, east, south, west, center) =>
        {
            bool M(Predicate<int> p) => p(north) || p(east) || p(south) || p(west);

            if (M(TFCLayers.IsOcean))
            {
                if (M(v => v == TFCLayers.TERRACE_LOWER)) return TFCLayers.TERRACE_UPPER;
                if (M(v => v == TFCLayers.SEA_STACKS)) return TFCLayers.SEA_STACKS;
                if (M(v => v == TFCLayers.TIDAL_FLATS)) return TFCLayers.SHORE;
                if (M(v => v == TFCLayers.COASTAL_DUNES)) return TFCLayers.COASTAL_DUNES;
                if (M(v => v == TFCLayers.SETBACK_CLIFFS)) return TFCLayers.SETBACK_CLIFFS;
                if (M(v => v == TFCLayers.ROCKY_SHORES)) return TFCLayers.ROCKY_SHORES;
                if (M(v => v == TFCLayers.EMBAYMENTS)) return TFCLayers.EMBAYMENTS;
            }
            return center;
        });
}

/// <summary>IceSheetEdgeLayer</summary>
public static class IceSheetEdgeLayer
{
    public static Area.Factory Apply(long seed, Area.Factory prev)
        => LayerHelpers.Adjacent(seed, prev, (ctx, area, north, east, south, west, center) =>
        {
            bool M(Predicate<int> p) => p(north) || p(east) || p(south) || p(west);

            if (center == TFCLayers.KNOB_AND_KETTLE || center == TFCLayers.PATTERNED_GROUND || center == TFCLayers.INVERTED_PATTERNED_GROUND || center == TFCLayers.STONE_CIRCLES)
            {
                if (M(v => v == TFCLayers.ICE_SHEET_TUYAS)) return TFCLayers.ICE_SHEET_TUYAS_EDGE;
                else if (M(TFCLayers.IsFlatIceSheet)) return TFCLayers.ICE_SHEET_EDGE;
            }
            if (center == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS || center == TFCLayers.ICE_SHEET_VOLCANIC_OCEANIC_MOUNTAINS)
            {
                if (M(IsNotIceSheet)) return TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS_EDGE;
            }
            if (center == TFCLayers.ICE_SHEET_MOUNTAINS || center == TFCLayers.ICE_SHEET_VOLCANIC_MOUNTAINS)
            {
                if (M(IsNotIceSheet)) return TFCLayers.ICE_SHEET_MOUNTAINS_EDGE;
            }
            if (center == TFCLayers.ICE_SHEET_EDGE && M(v => v == TFCLayers.ICE_SHEET_MOUNTAINS || v == TFCLayers.ICE_SHEET_MOUNTAINS_EDGE || v == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS || v == TFCLayers.ICE_SHEET_OCEANIC))
            {
                return TFCLayers.KNOB_AND_KETTLE;
            }
            if (center == TFCLayers.LAKE && M(TFCLayers.IsFlatIceSheet) && !M(v => v == TFCLayers.ICE_SHEET_MOUNTAINS || v == TFCLayers.ICE_SHEET_MOUNTAINS_EDGE || v == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS || v == TFCLayers.ICE_SHEET_OCEANIC))
            {
                return TFCLayers.SUBGLACIAL_LAKE;
            }
            if (TFCLayers.IsFlatIceSheet(center) && M(v => v == TFCLayers.MELTWATER_LAKE))
            {
                if (M(IsNotIceSheet)) return TFCLayers.SUBGLACIAL_LAKE;
            }
            if (TFCLayers.IsFlatIceSheet(center) && M(v => v == TFCLayers.OCEAN || v == TFCLayers.OCEAN_REEF || v == TFCLayers.DEEP_OCEAN || v == TFCLayers.DEEP_OCEAN_TRENCH || v == TFCLayers.ICE_SHEET_SHORE))
            {
                return TFCLayers.ICE_SHEET_OCEANIC;
            }
            if (IsNotIceSheetOrGlaciated(center))
            {
                if (M(v => v == TFCLayers.GLACIATED_MOUNTAINS)) return TFCLayers.GLACIALLY_CARVED_MOUNTAINS;
                else if (M(v => v == TFCLayers.GLACIATED_OCEANIC_MOUNTAINS)) return TFCLayers.GLACIALLY_CARVED_OCEANIC_MOUNTAINS;
            }
            if (center == TFCLayers.PLATEAU || center == TFCLayers.BADLANDS || center == TFCLayers.BURREN_BADLANDS || center == TFCLayers.BURREN_BADLANDS_TALL || center == TFCLayers.GLACIATED_SHIELD_VOLCANO)
            {
                if (M(v => v == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS_EDGE)) return TFCLayers.GLACIATED_OCEANIC_MOUNTAINS;
            }
            if (center == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS || center == TFCLayers.ICE_SHEET_VOLCANIC_OCEANIC_MOUNTAINS)
            {
                if (M(v => v == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS_EDGE)) return TFCLayers.ICE_SHEET_OCEANIC;
            }
            if (center == TFCLayers.ICE_SHEET_MOUNTAINS)
            {
                if (M(v => v == TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS_EDGE)) return TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS;
            }
            return center;
        });

    public static bool IsNotIceSheet(int value)
        => value != TFCLayers.ICE_SHEET && value != TFCLayers.ICE_SHEET_TUYAS && value != TFCLayers.SUBGLACIAL_LAKE && value != TFCLayers.ICE_SHEET_MOUNTAINS && value != TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS
            && value != TFCLayers.ICE_SHEET_SHIELD_VOLCANO && value != TFCLayers.ICE_SHEET_VOLCANIC_MOUNTAINS && value != TFCLayers.ICE_SHEET_VOLCANIC_OCEANIC_MOUNTAINS;

    public static bool IsNotIceSheetOrGlaciated(int value)
        => IsNotIceSheet(value) && value != TFCLayers.GLACIATED_MOUNTAINS && value != TFCLayers.GLACIATED_OCEANIC_MOUNTAINS && value != TFCLayers.GLACIATED_SHIELD_VOLCANO && value != TFCLayers.GLACIATED_VOLCANIC_MOUNTAINS && value != TFCLayers.GLACIATED_VOLCANIC_OCEANIC_MOUNTAINS;
}

/// <summary>TFCLayers 补齐：群系判定与 createRegionBiomeLayer 工厂</summary>
public static partial class TFCLayers
{
    public static bool IsLow(int value)
        => value == PLAINS || value == HILLS || value == LOW_CANYONS || value == LOWLANDS || value == SALT_MARSH || value == MUD_FLATS || value == SALT_FLATS || value == DUNE_SEA || value == RIFT_VALLEY;

    public static bool IsMountains(int value)
        => value == MOUNTAINS || value == OCEANIC_MOUNTAINS || value == OLD_MOUNTAINS || value == VOLCANIC_MOUNTAINS || value == VOLCANIC_OCEANIC_MOUNTAINS || value == COLLISIONAL_MOUNTAINS;

    public static bool IsFlats(int value) => value == MUD_FLATS || value == SALT_FLATS;

    public static bool HasShore(int value)
        => !IsOcean(value) && value != LOW_CANYONS && value != CANYONS && value != OCEANIC_MOUNTAINS && value != VOLCANIC_OCEANIC_MOUNTAINS
            && value != TOWER_KARST_BAY && value != SUNKEN_SHIELD_VOLCANO && value != GLACIALLY_CARVED_OCEANIC_MOUNTAINS && value != ICE_SHEET_OCEANIC_MOUNTAINS_EDGE
            && value != ICE_SHEET_SHIELD_VOLCANO && value != GLACIATED_SHIELD_VOLCANO && value != GUANO_ISLAND && value != GLACIALLY_CARVED_VOLCANIC_OCEANIC_MOUNTAINS
            && value != VOLCANIC_MOUNTAIN_ISLANDS;

    public static int ShoreFor(int value)
    {
        if (value == LOWLANDS || value == SALT_MARSH) return SALT_MARSH;
        if (value == MOUNTAINS || value == COLLISIONAL_MOUNTAINS) return OCEANIC_MOUNTAINS;
        if (value == VOLCANIC_MOUNTAINS) return VOLCANIC_OCEANIC_MOUNTAINS;
        if (value == TOWER_KARST_LAKE) return TOWER_KARST_BAY;
        if (value == ACTIVE_SHIELD_VOLCANO) return SHIELD_VOLCANO_SHORE;
        if (value == DORMANT_SHIELD_VOLCANO || value == EXTINCT_SHIELD_VOLCANO || value == ANCIENT_SHIELD_VOLCANO) return OLD_SHIELD_VOLCANO_SHORE;
        if (IsFlatIceSheet(value) || value == ICE_SHEET_EDGE || value == ICE_SHEET_OCEANIC) return ICE_SHEET_SHORE;
        if (value == ICE_SHEET_OCEANIC_MOUNTAINS || value == ICE_SHEET_VOLCANIC_OCEANIC_MOUNTAINS) return ICE_SHEET_OCEANIC_MOUNTAINS_EDGE;
        if (value == GLACIATED_OCEANIC_MOUNTAINS || value == GLACIATED_MOUNTAINS || value == GLACIALLY_CARVED_MOUNTAINS) return GLACIALLY_CARVED_OCEANIC_MOUNTAINS;
        if (value == GLACIATED_VOLCANIC_OCEANIC_MOUNTAINS || value == GLACIATED_VOLCANIC_MOUNTAINS || value == GLACIALLY_CARVED_VOLCANIC_MOUNTAINS) return GLACIALLY_CARVED_VOLCANIC_OCEANIC_MOUNTAINS;
        if (value == OLD_MOUNTAINS || value == EXTREME_DOLINE_MOUNTAINS) return TERRACE_LOWER;
        if (value == PLATEAU || value == EXTREME_DOLINE_PLATEAU || value == BURREN_PLATEAU || value == SHILIN_PLATEAU) return SEA_STACKS;
        if (value == PLATEAU_WIDE || value == ROCKY_PLATEAU || value == DOLINE_PLATEAU) return SETBACK_CLIFFS;
        if (value == HIGHLANDS || value == CENOTE_HIGHLANDS || value == DOLINE_HIGHLANDS || value == SHILIN_HIGHLANDS || value == TOWER_KARST_HIGHLANDS) return ROCKY_SHORES;
        if (value == ROLLING_HILLS || value == DOLINE_ROLLING_HILLS || value == CENOTE_ROLLING_HILLS) return EMBAYMENTS;
        if (value == HILLS || value == CENOTE_HILLS || value == DOLINE_HILLS || value == SHILIN_HILLS || value == TOWER_KARST_HILLS || value == GRASSY_DUNES || value == DUNE_SEA) return COASTAL_DUNES;
        if (value == VOLCANIC_ISLAND) return SHORE;
        return TIDAL_FLATS; // 注意 fallback 是 TIDAL_FLATS 不是 SHORE（对齐 Java shoreFor）
    }

    /// <summary>createRegionBiomeLayer（与游戏完全一致的全链）</summary>
    public static Area.Factory CreateRegionBiomeLayer(RegionGenerator generator, Seed seed)
    {
        var regionLayer = new RegionLayer(generator).Apply(seed.Next());

        Area.Factory mainLayer = RegionBiomeLayer.Apply(regionLayer);

        // Grid scale
        mainLayer = RegionEdgeBiomeLayer.Apply(seed.Next(), mainLayer);
        mainLayer = ZoomLayer.ApplyNormal(seed.Next(), mainLayer);

        // 4x4 Chunk Scale
        mainLayer = ShoreAndRiverLayer.Apply(seed.Next(), mainLayer);
        mainLayer = MoreShoresLayer.Apply(seed.Next(), mainLayer);
        mainLayer = IceSheetEdgeLayer.Apply(seed.Next(), mainLayer);
        mainLayer = ZoomLayer.ApplyNormal(seed.Next(), mainLayer);
        mainLayer = ZoomLayer.ApplyNormal(seed.Next(), mainLayer);

        // Chunk scale
        mainLayer = ZoomLayer.ApplyNormal(seed.Next(), mainLayer);
        mainLayer = ZoomLayer.ApplyNormal(seed.Next(), mainLayer);

        // Quart scale
        mainLayer = SmoothLayer.Apply(seed.Next(), mainLayer);

        return mainLayer;
    }
}