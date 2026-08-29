using System.Collections.Concurrent;
using TfcViewer.Core;
using TfcViewer.World;
using TfcViewer.Noise;
using TfcViewer.World.Layer;

namespace TfcViewer.World.Region;

/// <summary>RegionPartition（等价 TFC record；河流边收集）</summary>
public sealed class RegionPartition
{
    public sealed class Point
    {
        public List<RiverEdge> Rivers { get; } = new();
    }

    public readonly int MinPartX;
    public readonly int MinPartZ;
    private readonly Point[] data;

    public RegionPartition(int cellX, int cellZ)
    {
        MinPartX = Units.CellToPart(cellX);
        MinPartZ = Units.CellToPart(cellZ);
        data = new Point[Units.CELL_WIDTH_IN_PARTITION * Units.CELL_WIDTH_IN_PARTITION];
        for (int i = 0; i < data.Length; i++)
            data[i] = new Point();
    }

    public Point GetFromPart(int partX, int partZ) => data[Index(partX, partZ)];
    public Point Get(int gridX, int gridZ) => data[Index(Units.GridToPart(gridX) - MinPartX, Units.GridToPart(gridZ) - MinPartZ)];

    public bool IsIn(int partX, int partZ)
    {
        int localX = partX - MinPartX;
        int localZ = partZ - MinPartZ;
        return localX >= 0 && localZ >= 0 && localX < Units.CELL_WIDTH_IN_PARTITION && localZ < Units.CELL_WIDTH_IN_PARTITION;
    }

    private int Index(int partX, int partZ)
        => (partX & Units.PARTITION_BIT_MASK) | ((partZ & Units.PARTITION_BIT_MASK) << Units.PARTITION_BITS);
}

/// <summary>简单并发缓存（等价 FastConcurrentCache 语义：key=ChunkPos.asLong）</summary>
public sealed class SimpleCache<T>
{
    private readonly ConcurrentDictionary<long, T> map = new();

    public T? GetIfPresent(int x, int z) => map.TryGetValue(AsLong(x, z), out var v) ? v : default;
    public void Set(int x, int z, T value) => map[AsLong(x, z)] = value;

    private static long AsLong(int x, int z) => (long)x & 0xFFFFFFFFL | ((long)z << 32);
}

/// <summary>BiomeNoise 静态工厂（RegionGenerator 使用的子集）</summary>
public static class BiomeNoise
{
    public static Cellular2D ContinentCellNoise(float scaleFactor, long seed)
        => new Cellular2D(seed, 2).Spread(scaleFactor / Units.CELL_WIDTH_IN_GRID);

    public static Noise2D ActiveHotSpots(long seed)
    {
        const double horizontalScale = 0.003;
        const double cutoff = 0.75;
        const double rescale = 7.2;
        return new OpenSimplex2D(seed).Map(y =>
        {
            y = y > cutoff ? y - cutoff : 0;
            return y * rescale;
        }).Octaves(3).Spread(horizontalScale);
    }

    public static Noise2D DormantHotSpots(long seed)
        => HotSpotWarp(ActiveHotSpots(seed), PlateRegions(seed), 1024, 0).Map(y => Math.Max(y - 0.1, 0) * 1.111);

    public static Noise2D ExtinctHotSpots(long seed)
        => HotSpotWarp(ActiveHotSpots(seed), PlateRegions(seed), 2048, 0.25).Map(y => Math.Max(y - 0.2, 0) * 1.25);

    public static Noise2D AncientHotSpots(long seed)
        => HotSpotWarp(ActiveHotSpots(seed), PlateRegions(seed), 3072, 0.5).Map(y => Math.Max(y - 0.3, 0) * 1.4286);

    public static Noise2D HotSpotIntensity(long seed)
        => ActiveHotSpots(seed).Max(DormantHotSpots(seed)).Max(ExtinctHotSpots(seed)).Max(AncientHotSpots(seed));

    public static Noise2D HotSpotWarp(Noise2D noiseToWarp, Cellular2D warpCells, int velocityScale, double accelScale)
        => Noise2D.Fn((x, z) =>
        {
            double ux = warpCells.Noise(x, z);
            double uz = (Math.Abs(ux * 16) % 1 > 0.5 ? 1 : -1) * (ux * 256) % 1;

            int sx = ux > 0 ? 1 : -1;
            int sz = uz > 0 ? 1 : -1;
            double vx = (ux + sx) * velocityScale;
            double vz = (uz + sz) * velocityScale;

            double ax = -(vz) * accelScale;
            double az = vx * accelScale;

            var cell = warpCells.GetCell(x, z);
            double scale = Mth.ClampedMap(cell.f2 - cell.f1, 0, 0.002, 0, 1);

            return noiseToWarp.Noise(x + vx + ax, z + vz + az) * scale;
        });

    public static Noise2D HotSpotAge(long seed)
        => MapAges(ActiveHotSpots(seed), DormantHotSpots(seed), ExtinctHotSpots(seed), AncientHotSpots(seed));

    public static Noise2D MapAges(Noise2D activeNoise, Noise2D youngNoise, Noise2D oldNoise, Noise2D oldestNoise)
        => Noise2D.Fn((x, z) =>
        {
            double active = activeNoise.Noise(x, z);
            double young = youngNoise.Noise(x, z);
            double old = oldNoise.Noise(x, z);
            double oldest = oldestNoise.Noise(x, z);

            if (Math.Max(Math.Max(active, young), Math.Max(old, oldest)) <= -0.8) return 0;
            else if (active > young && active > old && active > oldest) return 1;
            else if (young > active && young > old && young > oldest) return 2;
            else if (old > young && old > oldest && old > active) return 3;
            else if (oldest > young && oldest > old && oldest > active) return 4;
            else return 0;
        });

    public static Cellular2D PlateRegions(long seed)
        => new Cellular2D(seed).Spread(0.00590625f / Units.CELL_WIDTH_IN_GRID);
}

/// <summary>RegionGenerator（等价 TFC；离线视图主入口）</summary>
public sealed class RegionGenerator
{
    public enum Task
    {
        Init,
        AnnotateDistanceToCellEdge,
        AnnotateBoundaryTypes,
        AddContinents,
        FloodFillSmallOceans,
        AddHotspots,
        AnnotateDistanceToOcean,
        AnnotateDistanceToDeepOcean,
        AnnotateBaseLandHeight,
        AnnotateDistanceToWestCoast,
        AddMountains,
        AddIslands,
        AnnotateBiomeAltitude,
        AnnotateClimate,
        ChooseRocks,
        AnnotateKarstSurface,
        AddRiversAndLakes,
        ChooseBiomes,
    }

    private static readonly RegionTask[] TASKS =
    {
        Init.Instance,
        AnnotateDistanceToCellEdge.Instance,
        AnnotateBoundaryTypes.Instance,
        AddContinentsAndSetOceanDepths.Instance,
        FloodFillSmallOceans.Instance,
        AddHotspots.Instance,
        AnnotateDistanceToOcean.Instance,
        AnnotateDistanceToDeepOcean.Instance,
        AnnotateBaseLandHeight.Instance,
        AnnotateDistanceToWestCoast.Instance,
        AddMountainsAndBarrierIslands.Instance,
        AddIslands.Instance,
        AnnotateBiomeAltitude.Instance,
        AnnotateClimate.Instance,
        ChooseRocks.Instance,
        KarstSurfaceRocks.Instance,
        AddRiversAndLakes.Instance,
        ChooseBiomes.Instance,
    };

    private static double Triangle(double frequency, double value)
        => Math.Abs(4f * frequency * value + 1f - 4f * Mth.Floor(frequency * value + 0.75f)) - 1f;

    private static Noise2D BaseNoise(bool axisIsX, float scale, float constant)
    {
        float frequency = Units.GRID_WIDTH_IN_BLOCK / (2f * scale);
        return scale == 0
            ? Noise2D.Fn((x, z) => constant)
            : axisIsX
                ? Noise2D.Fn((x, z) => Triangle(frequency, x))
                : Noise2D.Fn((x, z) => Triangle(frequency, z));
    }

    public readonly Cellular2D CellNoise;
    public readonly Noise2D ContinentNoise;
    public readonly Noise2D TemperatureNoise;
    public readonly Noise2D OceanicInfluenceNoise;
    public readonly Noise2D RainfallNoise;
    public readonly Noise2D RainfallVarianceNoise;
    public readonly Settings Settings;
    public readonly Noise2D HotSpotAgeNoise;
    public readonly Noise2D HotSpotIntensityNoise;
    public readonly Cellular2D PlateRegionNoise;
    public readonly Area BiomeArea;
    public readonly Area RockArea;

    private readonly Seed seed;
    private readonly SimpleCache<Region> cellCache = new();
    private readonly SimpleCache<RegionPartition> partitionCache = new();

    public RegionGenerator(Settings settings, Seed seed)
    {
        this.Settings = settings;
        this.seed = seed;

        this.CellNoise = ContinentCellNoise(1f);

        float min = settings.Continentalness * 10f - 2.5f; // [0,1] 默认 0.5 -> 2.5
        this.ContinentNoise = CellNoise.Then(c => 1 - c.f1 / (0.37f + c.f2))
            .LazyProduct(new OpenSimplex2D(seed.Next())
                .Spread(0.24f)
                .Scaled(min, 8.7f)
                .Octaves(4));

        this.TemperatureNoise = BaseNoise(false, settings.TemperatureScale, settings.TemperatureConstant)
            .Scaled(-20f, 30f)
            .Add(new OpenSimplex2D(seed.Next())
                .Octaves(2)
                .Spread(0.15f)
                .Scaled(-3f, 3f));

        this.OceanicInfluenceNoise = new OpenSimplex2D(seed.Next())
            .Spread(0.02f);

        this.RainfallNoise = BaseNoise(true, settings.RainfallScale, settings.RainfallConstant)
            .Scaled(0f, 500f)
            .Add(new OpenSimplex2D(seed.Next())
                .Octaves(2)
                .Spread(0.15f)
                .Scaled(-80f, 40f));

        this.RainfallVarianceNoise = new OpenSimplex2D(seed.Next())
            .Octaves(2)
            .Spread(0.1f)
            .Scaled(0f, 20f);

        this.HotSpotAgeNoise = BiomeNoise.HotSpotAge(seed.SeedValue).Spread(128);
        this.HotSpotIntensityNoise = BiomeNoise.HotSpotIntensity(seed.SeedValue).Spread(128);
        this.PlateRegionNoise = BiomeNoise.PlateRegions(seed.SeedValue).Spread(128);

        // 层链（与游戏一致的种子消费顺序）
        var biomeAreaFactory = TfcViewer.World.Layer.TFCLayers.CreateUniformLayer(seed, 2);
        var rockAreaFactory = TfcViewer.World.Layer.TFCLayers.CreateUniformLayer(seed, 3);
        this.BiomeArea = biomeAreaFactory();
        this.RockArea = rockAreaFactory();

        // 保持与游戏一致的 seed 消费序列（RegionChunkDataGenerator 的 5 个噪声种子）
        seed.Next();
        seed.Next();
        seed.Next();
        seed.Next();
        seed.Next();

        // biome layer chain: 懒创建（等价 Java ConcurrentArea）
        // 注：种子状态与 Java 一致（region tasks 不消耗此 seed 对象）
    }

    public Seed Seed => seed;

    private Area? biomeLayerArea;
    /// <summary>层链输出（等价 RegionBiomeSource 的 biomeLayer；懒创建）</summary>
    public Area BiomeLayerArea => biomeLayerArea ??= TfcViewer.World.Layer.TFCLayers.CreateRegionBiomeLayer(this, seed)();

    public Cellular2D ContinentCellNoise(float scaleFactor)
        => BiomeNoise.ContinentCellNoise(scaleFactor, seed.SeedValue);

    public double GetDivergence(Region.RegionPoint point)
    {
        var thisCell = CellNoise.GetCell(point.X, point.Z);
        double thisX = thisCell.x;
        double thisY = thisCell.y;
        double adjacentX = thisCell.nx;
        double adjacentY = thisCell.ny;
        var adjacentCell = CellNoise.GetCell(adjacentX, adjacentY);

        double thisVX = GetVX(thisCell.noise);
        double thisVY = GetVY(thisCell.noise);
        double adjacentVX = GetVX(adjacentCell.noise);
        double adjacentVY = GetVY(adjacentCell.noise);

        double convergeX = GetDivergence(thisX, adjacentX, thisVX, adjacentVX);
        double convergeY = GetDivergence(thisY, adjacentY, thisVY, adjacentVY);
        return convergeX + convergeY;
    }

    public static double GetDivergence(double x0, double x1, double v0, double v1)
    {
        double dvx = v1 - v0;
        return x0 < x1 ? dvx : -dvx;
    }

    public double GetVX(double noise) => Helpers.HashDouble(noise, 95274);
    public double GetVY(double noise) => Helpers.HashDouble(noise, 894824);

    public float ContinentFactor(Region.RegionPoint point)
    {
        if (Settings.FiniteContinents)
        {
            int scaleX = Settings.RainfallScale;
            int scaleZ = Settings.TemperatureScale;
            return Math.Min(
                scaleX == 0 ? 1f : (float)Mth.ClampedMap(Math.Abs(Units.GridToBlock(point.X)), scaleX, 1.2f * scaleX, 1, 0),
                scaleZ == 0 ? 1f : (float)Mth.ClampedMap(Math.Abs(Units.GridToBlock(point.Z) - 0.5f * scaleZ), scaleZ, 1.2f * scaleZ, 1, 0));
        }
        return 1f;
    }

    public RockSettings GetSurfaceRock(Region.RegionPoint point)
        => Settings.RockLayerSettings.SampleAtLayer(point.Rock, 0);

    public RegionPartition.Point GetOrCreatePartitionPoint(int gridX, int gridZ)
        => GetOrCreatePartition(gridX, gridZ).Get(gridX, gridZ);

    private RegionPartition GetOrCreatePartition(int gridX, int gridZ)
    {
        int cellX = Units.GridToCell(gridX);
        int cellZ = Units.GridToCell(gridZ);

        var entry = partitionCache.GetIfPresent(cellX, cellZ);
        if (entry == null)
        {
            entry = CreatePartition(cellX, cellZ);
            partitionCache.Set(cellX, cellZ, entry);
        }
        return entry;
    }

    private RegionPartition CreatePartition(int cellX, int cellZ)
    {
        var nearbyRegions = GetAllRegionsIn3x3CellArea(cellX, cellZ);
        var partition = new RegionPartition(cellX, cellZ);

        foreach (var region in nearbyRegions)
        {
            foreach (var edge in region.Rivers)
            {
                for (int partX = edge.MinPartX; partX <= edge.MaxPartX; partX++)
                {
                    for (int partZ = edge.MinPartZ; partZ <= edge.MaxPartZ; partZ++)
                    {
                        if (partition.IsIn(partX, partZ))
                        {
                            partition.GetFromPart(partX, partZ).Rivers.Add(edge);
                        }
                    }
                }
            }
        }
        return partition;
    }

    private List<Region> GetAllRegionsIn3x3CellArea(int cellX, int cellZ)
    {
        var regions = new List<Region>(9);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                int gx = Units.CellToGrid(cellX + dx);
                int gz = Units.CellToGrid(cellZ + dz);
                var regionCell = SampleCell(gx, gz);
                regions.Add(GetOrCreateRegion(gx, gz, regionCell));
            }
        }
        return regions;
    }

    public Region.RegionPoint GetOrCreateRegionPoint(int gridX, int gridZ)
        => GetOrCreateRegion(gridX, gridZ).AtOrThrow(gridX, gridZ);

    internal int PeekDataLength(Region.RegionPoint p) => GetOrCreateRegion(p.X, p.Z).DataLength;

    public Region GetOrCreateRegion(int gridX, int gridZ)
    {
        if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
            Interlocked.Increment(ref _regionGetOrCreateCalls);
        return GetOrCreateRegion(gridX, gridZ, SampleCell(gridX, gridZ));
    }

    private Region GetOrCreateRegion(int gridX, int gridZ, Cellular2D.Cell cell)
    {
        int cellX = BitConverter.SingleToInt32Bits((float)cell.x);
        int cellZ = BitConverter.SingleToInt32Bits((float)cell.y);

        var entry = cellCache.GetIfPresent(cellX, cellZ);
        if (entry == null)
        {
            if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
                Interlocked.Increment(ref _regionCreateCount);
            entry = CreateRegion(cell);
            cellCache.Set(cellX, cellZ, entry);
        }
        return entry;
    }

    private static int _regionCreateCount;
    private static int _regionGetOrCreateCalls;

    private Region CreateRegion(Cellular2D.Cell regionCell)
    {
        if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
        {
            int cx = BitConverter.SingleToInt32Bits((float)regionCell.x);
            int cz = BitConverter.SingleToInt32Bits((float)regionCell.y);
            Console.Error.WriteLine($"[REGION-DBG] Creating region cell=({cx},{cz})");
        }
        return new Context(this, regionCell, seed).RunTasks().Region;
    }

    public Cellular2D.Cell SampleCell(int gridX, int gridZ)
        => CellNoise.GetCell(gridX, gridZ);

    public sealed class Context
    {
        public readonly Cellular2D.Cell RegionCell;
        public readonly RandomSource Random;
        public readonly Region Region;

        private readonly RegionGenerator generator;

        public Context(RegionGenerator generator, Cellular2D.Cell regionCell, Seed seed)
        {
            this.generator = generator;
            this.RegionCell = regionCell;
            this.Region = new Region(regionCell);

            long regionSeed = seed.SeedValue ^ (BitConverter.SingleToInt32Bits((float)regionCell.noise) * 7189234123L);
            this.Random = new XoroshiroRandomSource(regionSeed);
        }

        public Context RunTasks()
        {
            foreach (var task in TASKS)
            {
                task.Apply(this);
                if (Environment.GetEnvironmentVariable("TFC_TASK_LOG") != null || File.Exists("/tmp/cs_task_marker"))
                {
                    var probe = RandomSourceDump.TryClone(Random);
                    File.AppendAllText("/tmp/cs_tasks.log", $"[TASK] {task.GetType().Name} -> probeNL={probe!.NextLong()}\n");
                }
            }
            return this;
        }

        public RegionGenerator Generator => generator;

        public Settings Settings => generator.Settings;
    }
}

/// <summary>RegionTask 接口</summary>
public interface RegionTask
{
    void Apply(RegionGenerator.Context context);
}