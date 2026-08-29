using TfcViewer.Core;
using TfcViewer.Noise;

namespace TfcViewer.World;

/// <summary>岩石类型常量（ChooseRocks 的 TYPE 位）</summary>
public static class RockType
{
    public const int TYPE_BITS = 2;
    public const int TYPE_MASK = 3;
    public const int OCEAN = 0;
    public const int VOLCANIC = 1;
    public const int LAND = 2;
    public const int UPLIFT = 3;
}

/// <summary>RockSettings（等价 TFC record，Block 用字符串 id 代替）</summary>
public sealed record RockSettings(
    string Raw, string Hardened, string Gravel, string Cobble, string Sand, string Sandstone,
    string? Spike, string? Loose, string? MossyLoose, bool? Karst, bool? Mafic);

/// <summary>TFC 世界设置</summary>
public sealed record Settings(
    bool FlatBedrock,
    int SpawnDistance,
    int SpawnCenterX,
    int SpawnCenterZ,
    int TemperatureScale,
    float TemperatureConstant,
    int RainfallScale,
    float RainfallConstant,
    RockLayerSettings RockLayerSettings,
    float Continentalness,
    float GrassDensity,
    bool FiniteContinents);

/// <summary>岩石层设置（等价 TFC RockLayerSettings）</summary>
public sealed class RockLayerSettings
{
    public sealed record Layer(Holder<RockSettings> Rock, List<Layer> Next);
    public sealed record Data(Dictionary<string, Holder<RockSettings>> Rocks, List<string> Bottom, List<LayerData> Layers, List<string> OceanFloor, List<string> Land, List<string> Volcanic, List<string> Uplift);
    public sealed record LayerData(string Id, KeyValuePair<string, string>[] LayerMap);

    public static RockLayerSettings Decode(Data data)
    {
        var bottom = new List<Layer>();
        foreach (string id in data.Bottom)
        {
            if (!data.Rocks.TryGetValue(id, out var rock)) throw new InvalidOperationException("No rock with id: " + id);
            bottom.Add(new Layer(rock, bottom));
        }

        var layers = new Dictionary<string, List<Layer>>
        {
            ["bottom"] = bottom
        };

        foreach (var layer in data.Layers)
        {
            var baked = new List<Layer>();
            foreach (var entry in layer.LayerMap)
            {
                if (!layers.TryGetValue(entry.Value, out var next)) throw new InvalidOperationException("No layer with id: " + entry.Value);
                if (!data.Rocks.TryGetValue(entry.Key, out var rock)) throw new InvalidOperationException("No rock with id: " + entry.Key);
                baked.Add(new Layer(rock, next));
            }
            layers[layer.Id] = baked;
        }

        var oceanFloor = ProcessTopLevel(layers, data.OceanFloor);
        var land = ProcessTopLevel(layers, data.Land);
        var volcanic = ProcessTopLevel(layers, data.Volcanic);
        var uplift = ProcessTopLevel(layers, data.Uplift);
        return new RockLayerSettings(oceanFloor, land, volcanic, uplift, data);
    }

    private static List<Layer> ProcessTopLevel(Dictionary<string, List<Layer>> layers, List<string> entries)
    {
        var baked = new List<Layer>();
        foreach (string id in entries)
        {
            if (layers.TryGetValue(id, out var layer))
                baked.AddRange(layer);
            else
                throw new InvalidOperationException("No layer with id: " + id);
        }
        return baked;
    }

    private readonly List<Layer> oceanFloor, land, volcanic, uplift;

    private RockLayerSettings(List<Layer> oceanFloor, List<Layer> land, List<Layer> volcanic, List<Layer> uplift, Data data)
    {
        this.oceanFloor = oceanFloor;
        this.land = land;
        this.volcanic = volcanic;
        this.uplift = uplift;
    }

    public RockSettings SampleAtLayer(int pointRock, int layerN)
    {
        // 注意：与 Java 版完全一致 —— XoroshiroRandomSource(pointRock >> 2)
        var source = new XoroshiroRandomSource(pointRock >> RockType.TYPE_BITS);
        var rootLayers = (pointRock & RockType.TYPE_MASK) switch
        {
            RockType.OCEAN => oceanFloor,
            RockType.VOLCANIC => volcanic,
            RockType.LAND => land,
            _ => uplift,
        };

        var layer = rootLayers[source.NextInt(rootLayers.Count)];
        for (int i = 0; i < layerN; i++)
        {
            layer = layer.Next[source.NextInt(layer.Next.Count)];
        }
        return layer.Rock.Value;
    }

    public Func<RockSettings> Sampler(int pointRock)
    {
        var initialLayer = (pointRock & RockType.TYPE_MASK) switch
        {
            RockType.OCEAN => oceanFloor,
            RockType.VOLCANIC => volcanic,
            RockType.LAND => land,
            _ => uplift,
        };
        var source = new XoroshiroRandomSource(pointRock >> RockType.TYPE_BITS);
        List<Layer> layer = initialLayer;
        return () =>
        {
            var chosen = layer[source.NextInt(layer.Count)];
            layer = chosen.Next;
            return chosen.Rock.Value;
        };
    }
}

/// <summary>默认配置（源自 v4.2.8 overworld.json；层顺序按 JSON 声明）</summary>
public static class Defaults
{
    private static RockSettings Rock(string id, bool karst = false)
        => new("tfc:" + id, "tfc:" + id, "tfc:" + id, "tfc:" + id, "tfc:" + id, "tfc:" + id, null, null, null, karst, null);

    /// <summary>按 JSON 顺序构造有序键值对（Java 侧 LinkedHashMap 语义）</summary>
    private static KeyValuePair<string, string>[] Lm(params string[] kv)
    {
        var list = new List<KeyValuePair<string, string>>();
        for (int i = 0; i < kv.Length; i += 2) list.Add(new KeyValuePair<string, string>(kv[i], kv[i + 1]));
        return list.ToArray();
    }

    public static RockLayerSettings DefaultRockLayerSettings()
    {
        var rocks = new Dictionary<string, Holder<RockSettings>>
        {
            ["andesite"] = Holder<RockSettings>.Direct(Rock("andesite")),
            ["basalt"] = Holder<RockSettings>.Direct(Rock("basalt")),
            ["chalk"] = Holder<RockSettings>.Direct(Rock("chalk", true)),
            ["chert"] = Holder<RockSettings>.Direct(Rock("chert")),
            ["claystone"] = Holder<RockSettings>.Direct(Rock("claystone")),
            ["conglomerate"] = Holder<RockSettings>.Direct(Rock("conglomerate")),
            ["dacite"] = Holder<RockSettings>.Direct(Rock("dacite")),
            ["diorite"] = Holder<RockSettings>.Direct(Rock("diorite")),
            ["dolomite"] = Holder<RockSettings>.Direct(Rock("dolomite", true)),
            ["gabbro"] = Holder<RockSettings>.Direct(Rock("gabbro")),
            ["gneiss"] = Holder<RockSettings>.Direct(Rock("gneiss")),
            ["granite"] = Holder<RockSettings>.Direct(Rock("granite")),
            ["limestone"] = Holder<RockSettings>.Direct(Rock("limestone", true)),
            ["marble"] = Holder<RockSettings>.Direct(Rock("marble", true)),
            ["phyllite"] = Holder<RockSettings>.Direct(Rock("phyllite")),
            ["quartzite"] = Holder<RockSettings>.Direct(Rock("quartzite")),
            ["rhyolite"] = Holder<RockSettings>.Direct(Rock("rhyolite")),
            ["schist"] = Holder<RockSettings>.Direct(Rock("schist")),
            ["shale"] = Holder<RockSettings>.Direct(Rock("shale")),
            ["slate"] = Holder<RockSettings>.Direct(Rock("slate")),
            ["tuff"] = Holder<RockSettings>.Direct(Rock("tuff")),
        };

        var data = new RockLayerSettings.Data(
            rocks,
            new List<string> { "gneiss", "schist", "diorite", "granite", "gabbro" },
            new List<RockLayerSettings.LayerData>
            {
                new("felsic", Lm("granite", "bottom")),
                new("intermediate", Lm("diorite", "bottom")),
                new("mafic", Lm("gabbro", "bottom")),
                new("igneous_extrusive", Lm("andesite", "intermediate", "basalt", "mafic", "dacite", "intermediate", "rhyolite", "felsic")),
                new("igneous_extrusive_x2", Lm("andesite", "igneous_extrusive", "basalt", "igneous_extrusive", "dacite", "igneous_extrusive", "rhyolite", "igneous_extrusive")),
                new("igneous_intrusive", Lm("diorite", "intermediate", "gabbro", "mafic", "granite", "felsic")),
                new("high_grade", Lm("gneiss", "bottom", "schist", "bottom")),
                new("low_grade", Lm("phyllite", "high_grade", "slate", "high_grade")),
                new("marble", Lm("marble", "bottom")),
                new("quartzite", Lm("quartzite", "bottom")),
                new("sedimentary", Lm("chalk", "marble", "chert", "quartzite", "claystone", "low_grade", "conglomerate", "low_grade", "dolomite", "marble", "limestone", "marble", "shale", "low_grade")),
                new("uplift", Lm("diorite", "low_grade", "gabbro", "low_grade", "gneiss", "high_grade", "granite", "low_grade", "marble", "bottom", "phyllite", "high_grade", "quartzite", "bottom", "schist", "high_grade", "slate", "high_grade")),
            },
            new List<string> { "igneous_extrusive" },
            new List<string> { "sedimentary", "sedimentary", "sedimentary", "igneous_extrusive" },
            new List<string> { "igneous_extrusive", "igneous_extrusive_x2", "igneous_intrusive" },
            new List<string> { "uplift", "uplift", "uplift", "sedimentary" });

        return RockLayerSettings.Decode(data);
    }

    public static Settings DefaultSettings()
    {
        return new Settings(
            false,        // FlatBedrock
            4000,         // SpawnDistance
            0,            // SpawnCenterX
            0,            // SpawnCenterZ
            20000,        // TemperatureScale
            0f,           // TemperatureConstant
            20000,        // RainfallScale
            0f,           // RainfallConstant
            DefaultRockLayerSettings(),
            0.5f,         // Continentalness
            0.5f,         // GrassDensity
            false         // FiniteContinents
        );
    }
}