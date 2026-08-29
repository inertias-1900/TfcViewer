using TfcViewer.Core;
using TfcViewer.Noise;

namespace TfcViewer.World.Region;

/// <summary>坐标转换（等价 TFC Units）</summary>
public static class Units
{
    public const int PARTITION_BITS = 5;
    public const int PARTITION_BIT_MASK = (1 << PARTITION_BITS) - 1;
    public const int PARTITION_WIDTH_IN_GRID = 3;

    public const int CELL_WIDTH_IN_PARTITION = (1 << PARTITION_BITS);
    public const int CELL_WIDTH_IN_GRID = CELL_WIDTH_IN_PARTITION * PARTITION_WIDTH_IN_GRID;

    public const int REGION_RADIUS_IN_GRID = CELL_WIDTH_IN_GRID + 5;
    public const int REGION_WIDTH_IN_GRID = 1 + 2 * REGION_RADIUS_IN_GRID;

    public const int QUART_BITS = 2;
    public const int GRID_BITS = 7;

    public const int GRID_WIDTH_IN_BLOCK = (1 << GRID_BITS);
    public const int GRID_WIDTH_IN_QUART = (1 << (GRID_BITS - QUART_BITS));

    public static int CellToPart(int cell) => cell << PARTITION_BITS;
    public static int PartToCell(int part) => part >> PARTITION_BITS;
    public static int GridToCell(int grid) => Mth.FloorDiv(grid, CELL_WIDTH_IN_GRID);
    public static int CellToGrid(int cell) => cell * CELL_WIDTH_IN_GRID;
    public static int GridToPart(int grid) => Mth.FloorDiv(grid, PARTITION_WIDTH_IN_GRID);
    public static int PartToGrid(int part) => part * PARTITION_WIDTH_IN_GRID;
    public static int QuartToGrid(int quart) => quart >> (GRID_BITS - QUART_BITS);
    public static double QuartToGridExact(double quart) => quart / GRID_WIDTH_IN_QUART;
    public static int GridToQuart(int grid) => grid << (GRID_BITS - QUART_BITS);
    public static double BlockToGridExact(double block) => block / GRID_WIDTH_IN_BLOCK;
    public static int BlockToGrid(int block) => block >> GRID_BITS;
    public static int GridToBlock(int grid) => grid << GRID_BITS;
    public static int Index(int x, int z) => (x & 15) | ((z & 15) << 4);
}

/// <summary>Region（等价 TFC Region + RegionPoint）</summary>
public sealed class Region
{
    private readonly double cellX;
    private readonly double cellY;
    private readonly double noise;
    private int minX;
    private int minZ;
    private int maxX;
    private int maxZ;
    private int sizeX;
    private int sizeZ;
    private RegionPoint[] data = System.Array.Empty<RegionPoint>();
    private List<RiverEdge>? rivers;

    public Region(Cellular2D.Cell cell)
    {
        this.cellX = cell.x;
        this.cellY = cell.y;
        this.noise = cell.noise;

        int cx = FNLite.FastRound(cell.x);
        int cz = FNLite.FastRound(cell.y);

        this.minX = cx - Units.REGION_RADIUS_IN_GRID;
        this.minZ = cz - Units.REGION_RADIUS_IN_GRID;
        this.maxX = cx + Units.REGION_RADIUS_IN_GRID;
        this.maxZ = cz + Units.REGION_RADIUS_IN_GRID;

        this.sizeX = 1 + maxX - minX;
        this.sizeZ = 1 + maxZ - minZ;
    }

    public IEnumerable<RegionPoint> Points()
    {
        for (int i = 0; i < data.Length; i++)
            if (data[i] != null)
                yield return data[i];
    }

    public RegionPoint? Random(RandomSource random) => data[random.NextInt(data.Length)];
    public int DataLength => data.Length;

    public RegionPoint? At(int gridX, int gridZ) => IsIn(gridX, gridZ) ? data[IndexOf(gridX, gridZ)] : null;

    public RegionPoint AtIndex(int index) => data[index];

    public RegionPoint? AtOffset(int index, int dx, int dz)
    {
        int localX = dx + (index % sizeX);
        int localZ = dz + (index / sizeX);
        return localX >= 0 && localX < sizeX && localZ >= 0 && localZ < sizeZ
            ? data[localX + sizeX * localZ]
            : null;
    }

    public void SetRivers(List<RiverEdge> rivers)
    {
        if (this.rivers != null) throw new InvalidOperationException();
        this.rivers = rivers;
    }

    public List<RiverEdge> Rivers => rivers ?? throw new InvalidOperationException();
    public double NoiseValue => noise;
    public int MinX => minX;
    public int MinZ => minZ;
    public int MaxX => maxX;
    public int MaxZ => maxZ;
    public int SizeX => sizeX;
    public int SizeZ => sizeZ;
    public int Size => sizeX * sizeZ;

    internal RegionPoint AtOrThrow(int gridX, int gridZ)
    {
        var point = data[IndexOf(gridX, gridZ)];
        if (point == null) throw new InvalidOperationException($"Region does not contain point at ({gridX}, {gridZ})");
        return point;
    }

    internal int IndexOf(int gridX, int gridZ)
    {
        int localX = gridX - minX;
        int localZ = gridZ - minZ;
        return localX + sizeX * localZ;
    }

    internal void SetRegionArea(int minX, int minZ, int maxX, int maxZ)
    {
        this.sizeX = 1 + maxX - minX;
        this.sizeZ = 1 + maxZ - minZ;
        this.data = new RegionPoint[sizeX * sizeZ];
        this.minX = minX;
        this.minZ = minZ;
        this.maxX = maxX;
        this.maxZ = maxZ;
    }

    internal void Init(int gridX, int gridZ)
    {
        int index = IndexOf(gridX, gridZ);
        data[index] = new RegionPoint(gridX, gridZ, index);
    }

    private bool IsIn(int gridX, int gridZ) => gridX >= minX && gridX <= maxX && gridZ >= minZ && gridZ <= maxZ;

    /// <summary>RegionPoint（等价 TFC 内部类）</summary>
    public sealed class RegionPoint
    {
        private const short FLAG_LAND = 0b1;
        private const short FLAG_ISLAND = 0b10;
        private const short FLAG_RIVER = 0b100;
        private const short FLAG_LAKE = 0b1000;
        private const short FLAG_MOUNTAIN = 0b10000;
        private const short FLAG_COASTAL_MOUNTAIN = 0b100000;
        private const short FLAG_VOLCANIC = 0b1000000;
        private const short FLAG_BARRIER_ISLAND = 0b10000000;

        public sbyte DistanceToOcean = 0;
        public sbyte DistanceToDeepOcean = 0;
        public sbyte DistanceToEdge = 0;
        public sbyte DistanceToWestCoast = 0;
        public sbyte DistanceToLand = 0;
        public sbyte BaseLandHeight = 0;
        public sbyte BiomeAltitude = 0;
        public sbyte OceanDepth = 0;

        public float Rainfall;
        public float RainfallVariance;
        public float Temperature;
        public float Divergence;

        public int Biome;
        public int Rock;
        public bool IsSurfaceRockKarst = false;
        public sbyte HotSpotAge = 0;

        private short flags;

        public readonly int X, Z;
        public readonly int Index;

        public RegionPoint(int x, int z, int index)
        {
            X = x;
            Z = z;
            Index = index;
        }

        public bool Land => (flags & FLAG_LAND) != 0;
        public bool Island => (flags & FLAG_ISLAND) != 0;
        public bool Shore => DistanceToOcean == -2;
        public bool River => (flags & FLAG_RIVER) != 0;
        public bool Lake => (flags & FLAG_LAKE) != 0;
        public bool Mountain => (flags & FLAG_MOUNTAIN) != 0;
        public bool CoastalMountain => (flags & FLAG_COASTAL_MOUNTAIN) != 0;
        public bool Volcanic => (flags & FLAG_VOLCANIC) != 0;
        public bool BarrierIsland => (flags & FLAG_BARRIER_ISLAND) != 0;

        public int DiscreteBiomeAltitude => Mth.FloorDiv(BiomeAltitude, AnnotateBiomeAltitude.WIDTH);

        public void SetLand() => flags |= FLAG_LAND;
        public void SetIsland() => flags |= FLAG_ISLAND;
        public void SetShore() => DistanceToOcean = -2;
        public void SetRiver() => flags |= FLAG_RIVER;
        public void SetLake() => flags |= FLAG_LAKE;
        public void SetMountain() => flags |= FLAG_MOUNTAIN;
        public void SetCoastalMountain() => flags |= FLAG_COASTAL_MOUNTAIN;
        public void SetVolcanic() => flags |= FLAG_VOLCANIC;
        public void SetBarrierIsland() => flags |= FLAG_BARRIER_ISLAND;
    }
}