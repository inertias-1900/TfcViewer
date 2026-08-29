namespace TfcViewer.World.Layer;

/// <summary>TFCLayers 辅助判定（hasLake/lakeFor 等，自 Java 提取）</summary>
public static partial class TFCLayers
{
    public static bool IsOcean(int value)
        => value == OCEAN || value == DEEP_OCEAN || value == DEEP_OCEAN_TRENCH || value == OCEAN_REEF || value == OCEAN_RIDGE || value == OCEANIC_VOLCANIC_ARC || value == OCEAN_ATOLLS || value == DEEP_OCEAN_ATOLLS;

    public static bool IsLake(int value)
        => value == LAKE || value == MELTWATER_LAKE || value == MOUNTAIN_LAKE || value == OCEANIC_MOUNTAIN_LAKE || value == PLATEAU_LAKE || value == OLD_MOUNTAIN_LAKE || value == SUBGLACIAL_LAKE || value == RIFT_LAKE || value == TOWER_KARST_LAKE || value == VOLCANIC_OCEANIC_MOUNTAIN_LAKE || value == VOLCANIC_MOUNTAIN_LAKE;

    public static bool IsFlatIceSheet(int value)
        => value == ICE_SHEET || value == ICE_SHEET_TUYAS || value == SUBGLACIAL_LAKE;

    public static bool HasLake(int value)
        => (!IsOcean(value) && !IsLake(value) && value != BADLANDS && value != ACTIVE_SHIELD_VOLCANO && value != DORMANT_SHIELD_VOLCANO
            && value != EXTINCT_SHIELD_VOLCANO && value != ANCIENT_SHIELD_VOLCANO && value != ICE_SHEET_MOUNTAINS
            && value != ICE_SHEET_MOUNTAINS_EDGE && value != ICE_SHEET_OCEANIC_MOUNTAINS && value != ICE_SHEET_OCEANIC_MOUNTAINS_EDGE
            && value != ICE_SHEET_SHIELD_VOLCANO && value != ICE_SHEET_SHORE && value != GLACIATED_SHIELD_VOLCANO
            && value != GLACIATED_MOUNTAINS && value != GLACIATED_OCEANIC_MOUNTAINS && value != GLACIALLY_CARVED_MOUNTAINS
            && value != GLACIALLY_CARVED_OCEANIC_MOUNTAINS && value != SALT_FLATS && value != MUD_FLATS);

    /// <summary>群系是否有河流（等价 BiomeExtension.hasRivers()：TFCBiomes 注册中无 .noRivers() 的群系）</summary>
    public static bool HasRivers(int value)
        => value != SHORE && value != TIDAL_FLATS && value != SEA_STACKS && value != TERRACE_UPPER && value != TERRACE_LOWER
            && value != SETBACK_CLIFFS && value != COASTAL_DUNES && value != ROCKY_SHORES && value != EMBAYMENTS && value != MELTWATER_LAKE;

    public static int LakeFor(int value)
    {
        if (value == MOUNTAINS || value == COLLISIONAL_MOUNTAINS) return MOUNTAIN_LAKE;
        if (value == VOLCANIC_MOUNTAINS) return VOLCANIC_MOUNTAIN_LAKE;
        if (value == OLD_MOUNTAINS || value == EXTREME_DOLINE_MOUNTAINS) return OLD_MOUNTAIN_LAKE;
        if (value == OCEANIC_MOUNTAINS) return OCEANIC_MOUNTAIN_LAKE;
        if (value == VOLCANIC_OCEANIC_MOUNTAINS) return VOLCANIC_OCEANIC_MOUNTAIN_LAKE;
        if (value == PLATEAU || value == PLATEAU_WIDE || value == ROCKY_PLATEAU || value == BURREN_PLATEAU || value == SHILIN_PLATEAU || value == DOLINE_PLATEAU || value == CENOTE_PLATEAU || value == EXTREME_DOLINE_PLATEAU) return PLATEAU_LAKE;
        if (IsFlatIceSheet(value)) return SUBGLACIAL_LAKE;
        if (value == ICE_SHEET_EDGE) return MELTWATER_LAKE;
        if (value == TOWER_KARST_CANYONS || value == TOWER_KARST_HIGHLANDS || value == TOWER_KARST_HILLS || value == TOWER_KARST_PLAINS) return TOWER_KARST_LAKE;
        return LAKE;
    }
}