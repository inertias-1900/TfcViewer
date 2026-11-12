namespace TfcViewer.Climate;

/// <summary>
/// TFC 原版气候分类（<c>KoppenClimateClassification</c>，1.20.x 源码逐条对应）。
/// <para>
/// 注意：TFC 自己注释「purely used for decoration / 因为只有年降水没有月降水分布而大幅简化」，
/// 所以它没有柯本分类里的 Am（热带季风）这类档位，热带只分雨林与稀树草原两种。
/// </para>
/// </summary>
public static class KoppenClimate
{
    // 顺序必须与 Java 枚举声明顺序一致
    public const int Arctic = 0;
    public const int Tundra = 1;
    public const int Subarctic = 2;
    public const int ColdDesert = 3;
    public const int HotDesert = 4;
    public const int Temperate = 5;
    public const int Subtropical = 6;
    public const int HumidSubtropical = 7;
    public const int HumidOceanic = 8;
    public const int HumidSubarctic = 9;
    public const int TropicalSavanna = 10;
    public const int TropicalRainforest = 11;

    public const int Count = 12;

    /// <summary>
    /// 等价 <c>KoppenClimateClassification.classify(float averageTemperature, float rainfall)</c>。
    /// 参数是年平均温度（不含季节摆动）与年降水。
    /// </summary>
    public static int Classify(float averageTemperature, float rainfall)
    {
        if (averageTemperature < -20f) return Arctic;
        if (rainfall < 150f) return averageTemperature > 4f ? HotDesert : ColdDesert;
        if (averageTemperature < -14f) return rainfall > 300f ? Subarctic : Tundra;
        if (averageTemperature > 18f) return rainfall > 300f ? TropicalRainforest : TropicalSavanna;
        if (rainfall > 350f)
        {
            if (averageTemperature > 12f) return HumidSubtropical;
            if (averageTemperature > -5f) return HumidOceanic;
            return HumidSubarctic;
        }
        if (averageTemperature > 12f) return Subtropical;
        if (averageTemperature > -5f) return Temperate;
        return Subarctic;
    }

    private static readonly string[] Zh =
    {
        "极地气候", "苔原气候", "亚寒带气候", "冷沙漠气候", "热沙漠气候", "温带气候",
        "亚热带气候", "亚热带湿润气候", "温带海洋性气候", "湿润亚寒带气候", "热带草原气候", "热带雨林气候",
    };

    private static readonly string[] En =
    {
        "Arctic", "Tundra", "Subarctic", "Cold desert", "Hot desert", "Temperate",
        "Subtropical", "Humid subtropical", "Humid oceanic", "Humid subarctic", "Tropical savanna", "Tropical rainforest",
    };

    /// <summary>枚举名（与 Java 侧一致，便于对照）</summary>
    private static readonly string[] Keys =
    {
        "arctic", "tundra", "subarctic", "cold_desert", "hot_desert", "temperate",
        "subtropical", "humid_subtropical", "humid_oceanic", "humid_subarctic", "tropical_savanna", "tropical_rainforest",
    };

    public static string Name(int id, bool english) => english ? En[id] : Zh[id];

    public static string Key(int id) => Keys[id];
}
