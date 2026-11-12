using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.World.Region;
using RegionPoint = TfcViewer.World.Region.Region.RegionPoint;

namespace TfcViewer.Climate;

/// <summary>
/// Java <c>java.util.Random</c> 的等价实现（48 位 LCG）。
/// TFC 的日温度变化用 <c>new Random(long)</c> 产生，必须逐位一致才能对齐游戏内温度。
/// </summary>
public sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    private long _seed;

    public JavaRandom(long seed) => _seed = (seed ^ Multiplier) & Mask;

    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return unchecked((int)((ulong)_seed >> (48 - bits)));
    }

    /// <summary>Java <c>nextFloat()</c>：next(24) / 2^24</summary>
    public float NextFloat() => Next(24) / (float)(1 << 24);

    public long NextLong() => ((long)Next(32) << 32) + Next(32);

    /// <summary>Java <c>nextInt(bound)</c>（含拒绝采样分支，与 JDK 一致）</summary>
    public int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound));
        int r = Next(31);
        int m = bound - 1;
        if ((bound & m) == 0)
        {
            r = (int)((bound * (long)r) >> 31);
        }
        else
        {
            for (int u = r; u - (r = u % bound) + m < 0; u = Next(31)) { }
        }
        return r;
    }
}

/// <summary>
/// Minecraft <c>net.minecraft.util.LinearCongruentialGenerator.next(long, long)</c>。
/// 实现取自本机 1.20.1 SRG 客户端 jar 的字节码（<c>seed*(seed*A+B)+salt</c>），非记忆猜测。
/// </summary>
public static class Trend
{
    public const long A = 6364136223846793005L;
    public const long B = 1442695040888963407L;

    public static long Next(long seed, long salt) => unchecked(seed * (seed * A + B) + salt);
}

/// <summary>TFC 月份（枚举顺序与 <c>Month.java</c> 一致：1 月 → 12 月）</summary>
public enum TfcMonth
{
    January, February, March, April, May, June,
    July, August, September, October, November, December,
}

public enum TfcSeason { Spring, Summer, Fall, Winter }

public static class TfcMonths
{
    /// <summary>Month.java 的 temperatureModifier（1.20.x 原文逐值）</summary>
    private static readonly float[] Modifiers =
    {
        -1f, -0.866f, -0.5f, 0f, 0.5f, 0.866f,
        1f, 0.866f, 0.5f, 0f, -0.5f, -0.866f,
    };

    private static readonly TfcSeason[] Seasons =
    {
        TfcSeason.Winter, TfcSeason.Winter, TfcSeason.Spring, TfcSeason.Spring,
        TfcSeason.Spring, TfcSeason.Summer, TfcSeason.Summer, TfcSeason.Summer,
        TfcSeason.Fall, TfcSeason.Fall, TfcSeason.Fall, TfcSeason.Winter,
    };

    private static readonly string[] ZhName = { "1月", "2月", "3月", "4月", "5月", "6月", "7月", "8月", "9月", "10月", "11月", "12月" };
    private static readonly string[] EnName = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };

    public static float Modifier(TfcMonth month) => Modifiers[(int)month];

    public static TfcMonth Next(TfcMonth month) => month == TfcMonth.December ? TfcMonth.January : (TfcMonth)((int)month + 1);

    public static TfcSeason SeasonOf(TfcMonth month) => Seasons[(int)month];

    public static string Name(TfcMonth month, bool english) => english ? EnName[(int)month] : ZhName[(int)month];

    public static string SeasonName(TfcSeason season, bool english) => season switch
    {
        TfcSeason.Spring => english ? "Spring" : "春",
        TfcSeason.Summer => english ? "Summer" : "夏",
        TfcSeason.Fall => english ? "Autumn" : "秋",
        _ => english ? "Winter" : "冬",
    };
}

/// <summary>
/// TFC 日历换算（<c>ICalendar.java</c> 常量与方法逐条对应）。
/// 1 小时 = 1000 tick，1 天 = 24000 tick，1 年 = 12 × daysInMonth 天，纪年从 1000 年起算。
/// </summary>
public static class TfcCalendar
{
    public const int TicksInHour = 1000;
    public const int HoursInDay = 24;
    public const int TicksInDay = TicksInHour * HoursInDay;
    public const int MonthsInYear = 12;
    public const float TicksInMinute = TicksInHour / 60f;
    public const long StartingYear = 1000;

    public static long TicksInMonth(int daysInMonth) => (long)TicksInDay * daysInMonth;

    public static long TicksInYear(int daysInMonth) => (long)TicksInDay * daysInMonth * MonthsInYear;

    public static long TotalHours(long time) => time / TicksInHour;

    public static long TotalDays(long time) => time / TicksInDay;

    public static long TotalYears(long time, int daysInMonth) => StartingYear + time / TicksInYear(daysInMonth);

    public static int HourOfDay(long time) => (int)(time / TicksInHour % HoursInDay);

    public static int MinuteOfHour(long time) => (int)(time % TicksInHour / TicksInMinute);

    public static int DayOfMonth(long time, int daysInMonth) => 1 + (int)(time / TicksInDay % daysInMonth);

    /// <summary>月内进度 [0,1)</summary>
    public static float FractionOfMonth(long time, int daysInMonth)
    {
        long ticksInMonth = TicksInMonth(daysInMonth);
        return (float)(time % ticksInMonth) / ticksInMonth;
    }

    public static float FractionOfYear(long time, int daysInMonth)
    {
        long ticksInYear = TicksInYear(daysInMonth);
        return (float)(time % ticksInYear) / ticksInYear;
    }

    public static TfcMonth MonthOfYear(long time, int daysInMonth)
        => (TfcMonth)(int)(time / TicksInMonth(daysInMonth) % MonthsInYear);

    public static int DayOfYear(long time, int daysInMonth) => (int)(time / TicksInDay % ((long)daysInMonth * MonthsInYear));

    /// <summary>该时刻所在年份的起始 tick（用于扫全年极值）</summary>
    public static long StartOfYear(long time, int daysInMonth)
    {
        long year = TicksInYear(daysInMonth);
        return time / year * year;
    }

    /// <summary>"1000 年 6 月 1 日 06:04" 之类的可读时间</summary>
    public static string Describe(long time, int daysInMonth, bool english)
    {
        var month = MonthOfYear(time, daysInMonth);
        int day = DayOfMonth(time, daysInMonth);
        int hour = HourOfDay(time);
        int minute = MinuteOfHour(time);
        long year = TotalYears(time, daysInMonth);
        return english
            ? $"{TfcMonths.Name(month, true)} {day}, {year} {hour:00}:{minute:00}"
            : $"{year} 年 {TfcMonths.Name(month, false)}{day} 日 {hour:00}:{minute:00}";
    }
}

/// <summary>
/// 一年的温度季节包络：把「月温度 + 日随机 + 日内变化」这三块与坐标无关的部分预先按小时算好，
/// 之后对任意坐标只需 <c>基础温度 + 纬度系数 × 表项</c> 就能得到年最低/年最高/年均。
/// <para>
/// 与 <see cref="ClimateModel.GetTemperature"/> 完全等价（同一套 Month 修正值、同一套 JavaRandom 种子链），
/// 只是把 O(年小时数) 的循环拆成「一次预计算 + 每次两次乘加」，供图层逐像素使用。
/// </para>
/// </summary>
public sealed class ClimateProfile
{
    /// <summary>包络基准年：纪年第 1000 年（ticks 从 0 起算）。日随机量随天数变化，故每年极值有 ~±0.3℃ 的差异。</summary>
    public const long ReferenceYearStartTicks = 0;

    public int DaysInMonth { get; }
    public int DaysInYear { get; }
    public long ClimateSeed { get; }
    public int TemperatureScale { get; }

    private readonly float[] _monthFactor;    // 每小时：月内插值后的月份修正值
    private readonly float[] _randomOffset;   // 每小时：当日随机偏移 (r1-r2)*3
    private readonly float[] _hourTerm;       // 每小时：日内项 0.9*hourModifier

    public ClimateProfile(ClimateModel model, int daysInMonth, long yearStartTicks = ReferenceYearStartTicks)
    {
        DaysInMonth = daysInMonth;
        DaysInYear = daysInMonth * TfcCalendar.MonthsInYear;
        ClimateSeed = model.ClimateSeed;
        TemperatureScale = model.TemperatureScale;

        int hours = DaysInYear * TfcCalendar.HoursInDay;
        _monthFactor = new float[hours];
        _randomOffset = new float[hours];
        _hourTerm = new float[hours];

        // 每天只算一次随机量（TFC 的日随机只依赖天数）
        var dailyOffset = new float[DaysInYear];
        for (int day = 0; day < DaysInYear; day++)
        {
            var random = model.SeededRandom(day, 1986239412341L);
            dailyOffset[day] = (random.NextFloat() - random.NextFloat()) * 3f;
        }

        for (int day = 0; day < DaysInYear; day++)
        {
            for (int hour = 0; hour < TfcCalendar.HoursInDay; hour++)
            {
                long ticks = yearStartTicks + (long)day * TfcCalendar.TicksInDay + (long)hour * TfcCalendar.TicksInHour;
                var month = TfcCalendar.MonthOfYear(ticks, daysInMonth);
                float delta = TfcCalendar.FractionOfMonth(ticks, daysInMonth);
                float factor = Mth.Lerp(delta, TfcMonths.Modifier(month), TfcMonths.Modifier(TfcMonths.Next(month)));

                int i = day * TfcCalendar.HoursInDay + hour;
                _monthFactor[i] = factor;
                _randomOffset[i] = dailyOffset[day];
                int hourOfDay = hour > 12 ? 24 - hour : hour;
                _hourTerm[i] = 0.3f * (hourOfDay / 6f - 1f) * 3f;
            }
        }
    }

    /// <summary>
    /// 某坐标一年的温度范围（默认 y 为海平面）。
    /// <paramref name="averageTemperature"/> 为世界生成阶段的基础温度。
    /// </summary>
    public TemperatureRange Range(int z, int y, float averageTemperature)
    {
        // 海平面以上：海拔项是常数，季节/日项线性可叠加 → 走快路径
        if (y > ClimateModel.SeaLevelY)
        {
            float latitude = TemperatureScale == 0 ? 0f : ClimateModel.Triangle(-3f, 15f, 1f / (2f * TemperatureScale), z);
            float min = float.MaxValue, max = float.MinValue, sum = 0f;
            for (int i = 0; i < _monthFactor.Length; i++)
            {
                float v = _monthFactor[i] * latitude + _randomOffset[i] + _hourTerm[i];
                if (v < min) min = v;
                if (v > max) max = v;
                sum += v;
            }

            float elevation = Mth.Clamp((y - ClimateModel.SeaLevelY) * 0.16225f, 0f, 17.822f);
            float baseline = averageTemperature - elevation;
            return new TemperatureRange(baseline + min, baseline + max, baseline + sum / _monthFactor.Length);
        }

        // 海平面及以下：季节/日变化随深度衰减，逐时刻调原公式（慢路径，但只用于低海拔点）
        float low = float.MaxValue, high = float.MinValue, acc = 0f;
        for (int i = 0; i < _monthFactor.Length; i++)
        {
            long ticks = ReferenceYearStartTicks + (long)i * TfcCalendar.TicksInHour;
            float monthTemp = _monthFactor[i] * ClimateModel.Triangle(-3f, 15f, 1f / (2f * TemperatureScale), z);
            float daily = _randomOffset[i] + _hourTerm[i];
            float v = AtElevation(y, averageTemperature, monthTemp, daily);
            if (v < low) low = v;
            if (v > high) high = v;
            acc += v;
        }
        return new TemperatureRange(low, high, acc / _monthFactor.Length);
    }

    /// <summary>与 ClimateModel.AdjustTemperatureByElevation 同一公式（静态计算，避免重新构造模型）</summary>
    private static float AtElevation(int y, float averageTemperature, float monthTemperature, float dailyTemperature)
    {
        if (y > ClimateModel.SeaLevelY)
        {
            float elevationTemperature = Mth.Clamp((y - ClimateModel.SeaLevelY) * 0.16225f, 0f, 17.822f);
            return averageTemperature + monthTemperature - elevationTemperature + dailyTemperature;
        }
        if (y > 0)
        {
            float monthInfluence = ClimateModel.InverseLerp(y, 0f, ClimateModel.SeaLevelY);
            float dailyInfluence = Mth.Clamp(monthInfluence * 3f - 2f, 0f, 1f);
            return averageTemperature
                + Mth.Lerp(monthInfluence, 0f, monthTemperature)
                + Mth.Lerp(dailyInfluence, 0f, dailyTemperature);
        }
        float depthInfluence = ClimateModel.InverseLerp(y, ClimateModel.DepthLevel, 0f);
        return Mth.Lerp(depthInfluence, ClimateModel.LavaLevelTemperature, averageTemperature);
    }
}

/// <summary>某坐标的一年温度范围（℃）</summary>
public readonly record struct TemperatureRange(float Min, float Max, float Mean)
{
    public float Span => Max - Min;
}

/// <summary>某坐标在某一年的温度极值</summary>
public readonly record struct TemperatureExtremes(float Min, float Max, long MinTicks, long MaxTicks);

/// <summary>
/// TFC 运行时气候模型（<c>OverworldClimateModel</c> 的等价移植，1.20.x）。
/// <para>
/// 与地形生成层的关系：基础温度 <c>averageTemperature</c> 来自世界生成的 Region 气候网格（见
/// <see cref="ClimateQuery.AverageTempAt"/>），本类只负责「季节 + 日变化 + 海拔」的修正。
/// </para>
/// </summary>
public sealed class ClimateModel
{
    public const float SnowFreezeTemperature = -2f;
    public const float SnowMeltTemperature = 2f;
    public const float IceFreezeTemperature = -4f;
    public const float IceMeltTemperature = 2f;
    public const float IcicleMinFreezeTemperature = -10f;
    public const float IcicleMaxFreezeTemperature = -2f;
    public const float IcicleDripTemperature = 0f;
    public const float IcicleMeltTemperature = 4f;
    public const float LavaLevelTemperature = 15f;

    /// <summary>TFC 海平面 y（温度海拔修正的起点）</summary>
    public const int SeaLevelY = 63;

    /// <summary>世界底部的温度基准高度</summary>
    public const int DepthLevel = -64;

    private const long ClimateSeedSalt = 719283741234L;
    private const long DailyTemperatureSalt = 1986239412341L;

    public long WorldSeed { get; }
    public int TemperatureScale { get; }

    /// <summary>气候随机种子：LCG(世界种子, 719283741234)。日随机数由它派生。</summary>
    public long ClimateSeed { get; }

    public ClimateModel(long worldSeed, int temperatureScale)
    {
        WorldSeed = worldSeed;
        TemperatureScale = temperatureScale;
        ClimateSeed = Trend.Next(worldSeed, ClimateSeedSalt);
    }

    /// <summary>
    /// 等价 <c>OverworldClimateModel.getTemperature(level, pos, data, calendarTicks, daysInMonth)</c>。
    /// </summary>
    /// <param name="averageTemperature">世界生成阶段该坐标的 averageTemp（128 格网格插值后）</param>
    /// <param name="y">方块 Y 高度</param>
    /// <param name="z">方块 Z（纬度项按 Z 变化）</param>
    /// <param name="calendarTicks">TFC 日历 tick（存档 tfc_calendar.dat 的 calendarTime）</param>
    /// <param name="daysInMonth">每月天数（存档里记载，默认 8）</param>
    public float GetTemperature(float averageTemperature, int y, int z, long calendarTicks, int daysInMonth)
    {
        var month = TfcCalendar.MonthOfYear(calendarTicks, daysInMonth);
        float delta = TfcCalendar.FractionOfMonth(calendarTicks, daysInMonth);
        float monthFactor = Mth.Lerp(delta, TfcMonths.Modifier(month), TfcMonths.Modifier(TfcMonths.Next(month)));
        float monthTemperature = CalcMonthlyTemperature(z, monthFactor);
        float dailyTemperature = CalcDailyTemperature(calendarTicks);
        return AdjustTemperatureByElevation(y, averageTemperature, monthTemperature, dailyTemperature);
    }

    /// <summary>只知道月份因子时的月度平均温度（等价 <c>getAverageMonthlyTemperature</c>）</summary>
    public float GetAverageMonthlyTemperature(int z, int y, float averageTemperature, float monthFactor)
        => AdjustTemperatureByElevation(y, averageTemperature, CalcMonthlyTemperature(z, monthFactor), 0f);

    /// <summary>纬度项：monthFactor × triangle(-3, 15, 1/(2·temperatureScale), z)。周期 = 2·temperatureScale 格。</summary>
    public float CalcMonthlyTemperature(int z, float monthTemperatureModifier)
    {
        if (TemperatureScale == 0) return 0f;
        float frequency = 1f / (2f * TemperatureScale);
        return monthTemperatureModifier * Triangle(-3f, 15f, frequency, z);
    }

    /// <summary>日变化：范围约 -3.9 ~ 3.9 ℃（日内正弦 + 当天随机偏移，随机种子只依赖 ClimateSeed 与天数）</summary>
    public float CalcDailyTemperature(long calendarTime)
    {
        int hourOfDay = TfcCalendar.HourOfDay(calendarTime);
        if (hourOfDay > 12) hourOfDay = 24 - hourOfDay;
        float hourModifier = hourOfDay / 6f - 1f;

        long day = TfcCalendar.TotalDays(calendarTime);
        var random = SeededRandom(day, DailyTemperatureSalt);
        return ((random.NextFloat() - random.NextFloat()) + 0.3f * hourModifier) * 3f;
    }

    public JavaRandom SeededRandom(long day, long salt)
        => new(Trend.Next(Trend.Next(ClimateSeed, day), salt));

    /// <summary>
    /// 海拔修正：海平面以上每格 -0.16225 ℃（上限 17.822）；海平面到 y=0 之间季节/日变化衰减；
    /// y ≤ 0 向岩浆层温度 15 ℃ 过渡（季节与日变化完全消失）。
    /// </summary>
    public float AdjustTemperatureByElevation(int y, float averageTemperature, float monthTemperature, float dailyTemperature)
    {
        if (y > SeaLevelY)
        {
            float elevationTemperature = Mth.Clamp((y - SeaLevelY) * 0.16225f, 0f, 17.822f);
            return averageTemperature + monthTemperature - elevationTemperature + dailyTemperature;
        }

        if (y > 0)
        {
            float monthInfluence = InverseLerp(y, 0f, SeaLevelY);
            float dailyInfluence = Mth.Clamp(monthInfluence * 3f - 2f, 0f, 1f);
            return averageTemperature
                + Mth.Lerp(monthInfluence, 0f, monthTemperature)
                + Mth.Lerp(dailyInfluence, 0f, dailyTemperature);
        }

        float depthInfluence = InverseLerp(y, DepthLevel, 0f);
        return Mth.Lerp(depthInfluence, LavaLevelTemperature, averageTemperature);
    }

    /// <summary>
    /// 全年温度极值与出现时刻：逐日取 hour=0（最冷）与 hour=12（最热）两个端点。
    /// 用于「这块地一年里最高/最低几度、会不会结冰/下雪」这类判断。
    /// </summary>
    /// <param name="startOfYearTicks">年起始 tick，见 <see cref="TfcCalendar.StartOfYear"/></param>
    public TemperatureExtremes GetYearExtremes(float averageTemperature, int y, int z, long startOfYearTicks, int daysInMonth)
    {
        int daysInYear = daysInMonth * TfcCalendar.MonthsInYear;
        float min = float.MaxValue, max = float.MinValue;
        long minTicks = startOfYearTicks, maxTicks = startOfYearTicks;

        for (int day = 0; day < daysInYear; day++)
        {
            long dayStart = startOfYearTicks + (long)day * TfcCalendar.TicksInDay;
            for (int i = 0; i < 2; i++)
            {
                long ticks = dayStart + (i == 0 ? 0 : 12 * TfcCalendar.TicksInHour);
                float t = GetTemperature(averageTemperature, y, z, ticks, daysInMonth);
                if (t < min) { min = t; minTicks = ticks; }
                if (t > max) { max = t; maxTicks = ticks; }
            }
        }

        return new TemperatureExtremes(min, max, minTicks, maxTicks);
    }

    /// <summary>Helpers.triangle(amplitude, midpoint, frequency, value)</summary>
    public static float Triangle(float amplitude, float midpoint, float frequency, float value)
        => midpoint + amplitude * (MathF.Abs(4f * frequency * value + 1f - 4f * Mth.Floor(frequency * value + 0.75f)) - 1f);

    /// <summary>Helpers.inverseLerp(value, min, max) = (value - min) / (max - min)</summary>
    public static float InverseLerp(float value, float min, float max) => (value - min) / (max - min);

    /// <summary>Helpers.lerp4：先沿 delta1 插值 00→01 / 10→11，再沿 delta0 在两组之间插值</summary>
    public static float Lerp4(float value00, float value01, float value10, float value11, float delta0, float delta1)
    {
        float value0 = value00 + (value01 - value00) * delta1;
        float value1 = value10 + (value11 - value10) * delta1;
        return value0 + (value1 - value0) * delta0;
    }

    public static double Lerp4(double value00, double value01, double value10, double value11, double delta0, double delta1)
    {
        double value0 = value00 + (value01 - value00) * delta1;
        double value1 = value10 + (value11 - value10) * delta1;
        return value0 + (value1 - value0) * delta0;
    }
}

/// <summary>
/// 把世界生成的 Region 气候网格接到运行时气候模型上：
/// 复刻 TFC <c>ChunkData.getAverageTemp/getRainfall</c> 的「128 格网格四角 + chunk 内双线性插值」。
/// </summary>
public static class ClimateQuery
{
    /// <summary>
    /// 等价 <c>ChunkData.getAverageTemp(blockX, blockZ)</c>：
    /// 取该方块所在 128×128 格网格的 4 个角点温度，先缩放到 chunk（16 格）范围，再在 chunk 内双线性插值。
    /// </summary>
    public static float AverageTempAt(RegionGenerator rg, int blockX, int blockZ)
    {
        var (layer00, layer01, layer10, layer11) = LayerCorners(rg, blockX, blockZ, (p) => p.Temperature);
        return SampleLayer(layer00, layer01, layer10, layer11, blockX, blockZ);
    }

    /// <summary>等价 <c>ChunkData.getRainfall</c>（未含河流增湿，河流影响属于生成后的 chunk 数据修正）</summary>
    public static float RainfallAt(RegionGenerator rg, int blockX, int blockZ)
    {
        var (layer00, layer01, layer10, layer11) = LayerCorners(rg, blockX, blockZ, (p) => p.Rainfall);
        return SampleLayer(layer00, layer01, layer10, layer11, blockX, blockZ);
    }

    private static (float, float, float, float) LayerCorners(
        RegionGenerator rg, int blockX, int blockZ, Func<RegionPoint, float> selector)
    {
        int gridX = Units.BlockToGrid(blockX);
        int gridZ = Units.BlockToGrid(blockZ);

        float v00 = selector(rg.GetOrCreateRegionPoint(gridX, gridZ));
        float v01 = selector(rg.GetOrCreateRegionPoint(gridX, gridZ + 1));
        float v10 = selector(rg.GetOrCreateRegionPoint(gridX + 1, gridZ));
        float v11 = selector(rg.GetOrCreateRegionPoint(gridX + 1, gridZ + 1));

        // 该 chunk 左下角在网格里的精确位置，以及 chunk 宽度（= 16/128 = 0.125 格）
        double exactGridX = Units.BlockToGridExact(blockX);
        double exactGridZ = Units.BlockToGridExact(blockZ);
        double deltaX = exactGridX - gridX;
        double deltaZ = exactGridZ - gridZ;
        const double dG = 16.0 / Units.GRID_WIDTH_IN_BLOCK;

        // LerpFloatLayer.scaled(originX, originZ, width)
        float s00 = (float)ClimateModel.Lerp4(v00, v01, v10, v11, deltaX, deltaZ);
        float s01 = (float)ClimateModel.Lerp4(v00, v01, v10, v11, deltaX, deltaZ + dG);
        float s10 = (float)ClimateModel.Lerp4(v00, v01, v10, v11, deltaX + dG, deltaZ);
        float s11 = (float)ClimateModel.Lerp4(v00, v01, v10, v11, deltaX + dG, deltaZ + dG);
        return (s00, s01, s10, s11);
    }

    private static float SampleLayer(float v00, float v01, float v10, float v11, int blockX, int blockZ)
    {
        float deltaX = (blockX & 15) / 16f;
        float deltaZ = (blockZ & 15) / 16f;
        return ClimateModel.Lerp4(v00, v01, v10, v11, deltaX, deltaZ);
    }
}
