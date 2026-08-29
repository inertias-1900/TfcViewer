using TfcViewer.Core;
using TfcViewer.Noise;
using TfcViewer.World.Layer;
using TfcViewer.World.River;

namespace TfcViewer.World.Region;

/// <summary>Init：初始化 region 点阵（按 cell 归属）</summary>
public sealed class Init : RegionTask
{
    public static readonly Init Instance = new();
    private Init() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var cell = new HashSet<int>();
        // 等价 Java BitSet：用 HashSet<int> 存置位索引

        int minX = int.MaxValue, minZ = int.MaxValue;
        int maxX = int.MinValue, maxZ = int.MinValue;

        for (int dx = 0; dx <= 2 * Units.REGION_RADIUS_IN_GRID; dx++)
        {
            for (int dz = 0; dz <= 2 * Units.REGION_RADIUS_IN_GRID; dz++)
            {
                int gridX = region.MinX + dx;
                int gridZ = region.MinZ + dz;
                int index = region.IndexOf(gridX, gridZ);
                var otherCell = context.Generator.SampleCell(gridX, gridZ);

                if (otherCell.x == context.RegionCell.x && otherCell.y == context.RegionCell.y)
                {
                    cell.Add(index);
                    if (gridX < minX) minX = gridX;
                    if (gridZ < minZ) minZ = gridZ;
                    if (gridX > maxX) maxX = gridX;
                    if (gridZ > maxZ) maxZ = gridZ;
                }
            }
        }

        int modifiedSizeX = 1 + maxX - minX;
        int modifiedSizeZ = 1 + maxZ - minZ;
        int offsetX = minX - region.MinX;
        int offsetZ = minZ - region.MinZ;
        int prevSizeX = 1 + 2 * Units.REGION_RADIUS_IN_GRID;

        region.SetRegionArea(minX, minZ, maxX, maxZ);

        for (int dx = 0; dx < modifiedSizeX; dx++)
        {
            for (int dz = 0; dz < modifiedSizeZ; dz++)
            {
                if (cell.Contains((offsetX + dx) + prevSizeX * (offsetZ + dz)))
                {
                    region.Init(minX + dx, minZ + dz);
                }
            }
        }
    }
}

/// <summary>AnnotateDistanceToCellEdge</summary>
public sealed class AnnotateDistanceToCellEdge : RegionTask
{
    public static readonly AnnotateDistanceToCellEdge Instance = new();
    private AnnotateDistanceToCellEdge() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();

        for (int dx = 0; dx < region.SizeX; dx++)
        {
            for (int dz = 0; dz < region.SizeZ; dz++)
            {
                int index = dx + region.SizeX * dz;
                var point = region.At(dx + region.MinX, dz + region.MinZ);
                if (point == null || IsUnbounded(region, dx, dz))
                {
                    explored.Add(index);
                    queue.AddLast(index);
                    if (point != null) point.DistanceToEdge = -1;
                }
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int nextDistance = lastPoint == null ? 0 : lastPoint.DistanceToEdge + 1;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && point.DistanceToEdge == 0)
                    {
                        if (!explored.Contains(point.Index))
                        {
                            point.DistanceToEdge = (sbyte)nextDistance;
                            explored.Add(point.Index);
                            queue.AddLast(point.Index);
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }
    }

    private static bool IsUnbounded(Region region, int dx, int dz)
        => dx == 0 || dz == 0 || dx == region.SizeX - 1 || dz == region.SizeZ - 1;
}

/// <summary>AnnotateBoundaryTypes：洋流发散</summary>
public sealed class AnnotateBoundaryTypes : RegionTask
{
    public static readonly AnnotateBoundaryTypes Instance = new();
    private AnnotateBoundaryTypes() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        foreach (var point in region.Points())
        {
            point.Divergence = (float)context.Generator.GetDivergence(point);
        }
    }
}

/// <summary>AddContinentsAndSetOceanDepths</summary>
public sealed class AddContinentsAndSetOceanDepths : RegionTask
{
    public static readonly AddContinentsAndSetOceanDepths Instance = new();
    private AddContinentsAndSetOceanDepths() { }

    public void Apply(RegionGenerator.Context context)
    {
        foreach (var point in context.Region.Points())
        {
            double perlinNoiseContinent = context.Generator.ContinentNoise.Noise(point.X, point.Z);
            double finiteWorldMultiplier = context.Generator.ContinentFactor(point);
            double tectonicFeatures = AddRiftSeas(point);

            double continent = (perlinNoiseContinent + tectonicFeatures) * finiteWorldMultiplier;
            if (continent > 4.4)
            {
                point.SetLand();
            }
            else if (point.Divergence > 0 && point.DistanceToEdge < 2)
            {
                point.OceanDepth = 3;
            }
            else if (continent > 3.3)
            {
                point.OceanDepth = 2;
            }
            else if (continent > 3 && point.Divergence < 0)
            {
                point.OceanDepth = 5;
            }
            else if (point.DistanceToEdge < 2 && !(point.Divergence < 0 && continent > 2))
            {
                point.OceanDepth = 3;
            }
            else
            {
                point.OceanDepth = 4;
            }
        }
    }

    private static double AddRiftSeas(Region.RegionPoint point)
    {
        if (point.DistanceToEdge <= 5 && point.Divergence > 0)
        {
            return (5 - point.DistanceToEdge) * -0.12;
        }
        return 0;
    }
}

/// <summary>FloodFillSmallOceans</summary>
public sealed class FloodFillSmallOceans : RegionTask
{
    public static readonly FloodFillSmallOceans Instance = new();
    private FloodFillSmallOceans() { }

    private const int SMALL_OCEAN_FILL_THRESHOLD = 180;

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var explored = new HashSet<int>();

        foreach (var point in region.Points())
        {
            if (!point.Land && !explored.Contains(point.Index))
            {
                FloodFillSmallOcean(explored, point.Index, region);
            }
        }

        var exploredForShelf = new HashSet<int>();
        foreach (var point in region.Points())
        {
            if (!point.Land && point.OceanDepth != 2 && !exploredForShelf.Contains(point.Index))
            {
                FloodFillContinentalShelfDepression(exploredForShelf, point.Index, region);
            }
        }
    }

    private static void FloodFillSmallOcean(HashSet<int> explored, int index, Region region)
    {
        var values = new HashSet<int>();
        var queue = new LinkedList<int>();
        queue.AddLast(index);
        values.Add(index);

        bool unbounded = false;
        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point == null)
                    {
                        unbounded = true;
                        continue;
                    }
                    if (point.Land || explored.Contains(point.Index)) continue;

                    explored.Add(point.Index);
                    queue.AddLast(point.Index);
                    values.Add(point.Index);
                }
            }
        }

        if (values.Count < SMALL_OCEAN_FILL_THRESHOLD && !unbounded)
        {
            foreach (int i in values) region.AtIndex(i).SetLand();
        }
    }

    private static void FloodFillContinentalShelfDepression(HashSet<int> explored, int index, Region region)
    {
        var values = new HashSet<int>();
        var queue = new LinkedList<int>();
        queue.AddLast(index);
        values.Add(index);

        bool unbounded = false;
        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point == null)
                    {
                        unbounded = true;
                        continue;
                    }
                    if (point.OceanDepth == 2 || explored.Contains(point.Index)) continue;

                    explored.Add(point.Index);
                    queue.AddLast(point.Index);
                    values.Add(point.Index);
                }
            }
        }

        if (values.Count < SMALL_OCEAN_FILL_THRESHOLD && !unbounded)
        {
            foreach (int i in values) region.AtIndex(i).SetLand();
        }
    }
}

/// <summary>AddHotspots</summary>
public sealed class AddHotspots : RegionTask
{
    public static readonly AddHotspots Instance = new();
    private AddHotspots() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        const double threshold = 0.65;
        const double expansionThreshold = 0.15;

        var queue = new LinkedList<int>();

        foreach (var point in region.Points())
        {
            if (context.Generator.ContinentFactor(point) > 0.5f)
            {
                var cell = context.Generator.PlateRegionNoise.GetCell(point.X, point.Z);
                double edgeDist = Math.Abs(cell.f1 - cell.f2);

                double val = context.Generator.HotSpotIntensityNoise.Noise(Shift(point.X), Shift(point.Z));
                if (val > threshold && edgeDist > 0.05)
                {
                    sbyte age = (sbyte)(int)context.Generator.HotSpotAgeNoise.Noise(Shift(point.X), Shift(point.Z));
                    point.HotSpotAge = age;
                    if (age != 4) point.SetLand();
                    queue.AddLast(point.Index);
                }
            }
        }

        while (queue.Count > 0)
        {
            int index = queue.First.Value;
            queue.RemoveFirst();
            sbyte lastAge = region.AtIndex(index).HotSpotAge;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var next = region.AtOffset(index, dx, dz);
                    if (next != null && next.HotSpotAge == 0)
                    {
                        if (context.Generator.HotSpotIntensityNoise.Noise(Shift(next.X), Shift(next.Z)) > expansionThreshold)
                        {
                            queue.AddLast(next.Index);
                            next.HotSpotAge = lastAge;
                            if (lastAge != 4) next.SetLand();
                        }
                        else if (!next.Land && context.Generator.HotSpotIntensityNoise.Noise(Shift(next.X) - dx, Shift(next.Z) - dz) > expansionThreshold)
                        {
                            next.HotSpotAge = lastAge;
                            if (lastAge != 4) next.SetLand();
                        }
                    }
                }
            }
        }
    }

    private static double Shift(int point) => point + 0.5;
}

/// <summary>AnnotateDistanceToOcean</summary>
public sealed class AnnotateDistanceToOcean : RegionTask
{
    public static readonly AnnotateDistanceToOcean Instance = new();
    private AnnotateDistanceToOcean() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();

        foreach (var point in region.Points())
        {
            if (!point.Land)
            {
                point.DistanceToOcean = -1;
                queue.AddLast(point.Index);
                explored.Add(point.Index);
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int nextDistance = lastPoint.DistanceToOcean + 1;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && point.Land && point.DistanceToOcean == 0)
                    {
                        if (!lastPoint.Land && !point.Island)
                        {
                            lastPoint.SetShore();
                        }
                        if (!explored.Contains(point.Index))
                        {
                            point.DistanceToOcean = (sbyte)nextDistance;
                            queue.AddLast(point.Index);
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }
    }
}

/// <summary>AnnotateDistanceToDeepOcean</summary>
public sealed class AnnotateDistanceToDeepOcean : RegionTask
{
    public static readonly AnnotateDistanceToDeepOcean Instance = new();
    private AnnotateDistanceToDeepOcean() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();

        foreach (var point in region.Points())
        {
            if (point.OceanDepth > 2)
            {
                point.DistanceToDeepOcean = -1;
                queue.AddLast(point.Index);
                explored.Add(point.Index);
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int nextDistance = lastPoint.DistanceToDeepOcean + 1;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && point.OceanDepth <= 2 && point.DistanceToDeepOcean == 0)
                    {
                        if (!explored.Contains(point.Index))
                        {
                            point.DistanceToDeepOcean = (sbyte)nextDistance;
                            queue.AddLast(point.Index);
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }
    }
}

/// <summary>AnnotateBaseLandHeight</summary>
public sealed class AnnotateBaseLandHeight : RegionTask
{
    public static readonly AnnotateBaseLandHeight Instance = new();
    private AnnotateBaseLandHeight() { }

    private const int ISLAND_SEED_DEPTH = 3;

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();
        var islandQueue = new List<int>();
        var random = context.Random;

        foreach (var point in region.Points())
        {
            if (point.Land)
            {
                point.BaseLandHeight = point.DistanceToOcean;
                if (point.BaseLandHeight > point.DistanceToEdge)
                {
                    point.BaseLandHeight = (sbyte)(0.3f * point.BaseLandHeight + 0.7f * point.DistanceToEdge);
                }

                explored.Add(point.Index);

                if (point.Island)
                {
                    point.DistanceToLand = ISLAND_SEED_DEPTH;
                    islandQueue.Add(point.Index);
                }
                else
                {
                    point.DistanceToLand = 0;
                    queue.AddLast(point.Index);
                }
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int nextDepth = lastPoint.DistanceToLand + 1;

            if (nextDepth == ISLAND_SEED_DEPTH && islandQueue.Count > 0)
            {
                // 岛点此时一次性入队尾（Java enqueue 语义，不是 enqueueFirst）
                foreach (int q in islandQueue) queue.AddLast(q);
                islandQueue.Clear();
            }

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && !point.Land && point.DistanceToLand == 0)
                    {
                        if (!explored.Contains(point.Index))
                        {
                            if (random.NextInt(15) == 0)
                            {
                                point.DistanceToLand = lastPoint.DistanceToLand;
                                queue.AddFirst(point.Index); // enqueueFirst
                            }
                            else
                            {
                                point.DistanceToLand = (sbyte)nextDepth;
                                queue.AddLast(point.Index);
                            }
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }
    }
}

/// <summary>AnnotateDistanceToWestCoast</summary>
public sealed class AnnotateDistanceToWestCoast : RegionTask
{
    public static readonly AnnotateDistanceToWestCoast Instance = new();
    private AnnotateDistanceToWestCoast() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;

        for (int dx = 0; dx < region.SizeX; dx++)
        {
            for (int dz = 0; dz < region.SizeZ; dz++)
            {
                int index = dx + region.SizeX * dz;
                var point = region.AtIndex(index);
                if (point != null)
                {
                    if (dx == 0)
                    {
                        point.DistanceToWestCoast = 0;
                    }
                    else
                    {
                        var lastCenterPoint = region.AtIndex(index - 1);
                        if (lastCenterPoint == null)
                        {
                            point.DistanceToWestCoast = point.Land ? (sbyte)(25 + point.DistanceToOcean) : (sbyte)0;
                        }
                        else
                        {
                            int lastCenterVal = lastCenterPoint.DistanceToWestCoast;
                            if (!point.Land)
                            {
                                point.DistanceToWestCoast = (sbyte)Math.Max(lastCenterVal - 2, 0);
                            }
                            else
                            {
                                int sum = 0;
                                float scale = context.Generator.Settings.TemperatureScale;
                                float frequency = Units.GRID_WIDTH_IN_BLOCK / (2f * scale);
                                float function = Math.Abs(4f * frequency * point.Z - 4f * Mth.Floor(frequency * point.Z + 0.75f)) - 1f;
                                int start = -2 + (function > 0.1 ? 1 : 0);
                                int end = 2 - (function < -0.1 ? 1 : 0);
                                for (int dz2 = start; dz2 <= end; dz2++)
                                {
                                    var lastPoint = region.AtOffset(point.Index, -1, dz2);
                                    sum += lastPoint != null ? lastPoint.DistanceToWestCoast : lastCenterVal;
                                }
                                point.DistanceToWestCoast = (sbyte)(Mth.Ceil(sum / (1f + end - start)) + 1);
                            }
                        }
                    }
                }
            }
        }

        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();

        foreach (var point in region.Points())
        {
            if (point.Land)
            {
                explored.Add(point.Index);
                queue.AddLast(point.Index);
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int lastDistance = lastPoint.DistanceToWestCoast;
            int nextDistance = lastDistance + (lastDistance > 40 ? -1 : 1);

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && point.DistanceToWestCoast == 0)
                    {
                        if (!explored.Contains(point.Index))
                        {
                            point.DistanceToWestCoast = (sbyte)nextDistance;
                            queue.AddLast(point.Index);
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }
    }
}

/// <summary>AddMountainsAndBarrierIslands</summary>
public sealed class AddMountainsAndBarrierIslands : RegionTask
{
    public static readonly AddMountainsAndBarrierIslands Instance = new();
    private AddMountainsAndBarrierIslands() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var random = context.Random;

        // 碰撞山脉
        foreach (var point in region.Points())
        {
            if (point.Land && point.Divergence < 0 && point.DistanceToEdge < 1.5 * context.Generator.ContinentNoise.Noise(point.X, point.Z) - 5.2)
            {
                point.SetMountain();
            }
        }

        // 山脉/火山弧/屏障岛链
        for (int attempt = 0, mtnsPlaced = 0, islesPlaced = 0; attempt < 40 && !(mtnsPlaced >= 3 && islesPlaced >= 6); attempt++)
        {
            var origin = region.Random(random);
            if (origin != null)
            {
                if (mtnsPlaced < 3 && (origin.Land && origin.BaseLandHeight <= 1 || (origin.BaseLandHeight >= 4 && origin.BaseLandHeight <= 11)))
                {
                    var range = PlaceRange(region, random, origin.Index);
                    if (range.Count > 45)
                    {
                        foreach (int index in range)
                        {
                            var point = region.AtIndex(index);
                            point.SetMountain();
                            if (origin.Divergence < 0 && origin.BaseLandHeight < 8)
                            {
                                point.SetVolcanic();
                            }
                            if (origin.BaseLandHeight <= 2)
                            {
                                point.SetCoastalMountain();
                            }
                        }
                        mtnsPlaced++;
                    }
                }
                else if (islesPlaced < 6 && !origin.Land && origin.OceanDepth == 2 && origin.Divergence < 0 && origin.DistanceToDeepOcean <= 3 && origin.DistanceToLand > 2)
                {
                    var range = PlaceVolcanicArc(region, random, origin.Index);
                    if (range.Count > 45)
                    {
                        sbyte originContour = origin.DistanceToDeepOcean;
                        foreach (int index in range)
                        {
                            var point = region.AtIndex(index);
                            if (point.DistanceToDeepOcean >= originContour && point.DistanceToDeepOcean <= originContour + 1)
                            {
                                point.SetBarrierIsland();
                            }
                            point.SetVolcanic();
                            point.OceanDepth = 1;
                        }
                        islesPlaced += 2;
                    }
                }
                else if (islesPlaced < 6 && !origin.Land && origin.OceanDepth == 2 && origin.Divergence > 0 && origin.DistanceToLand > 2 && origin.DistanceToLand < 6)
                {
                    var range = PlaceBarrier(region, random, origin.Index);
                    sbyte startContour = (sbyte)Math.Max(1, (int)origin.DistanceToLand);
                    if (range.Count > 45)
                    {
                        foreach (int index in range)
                        {
                            var point = region.AtIndex(index);
                            if (point.DistanceToLand == startContour)
                            {
                                point.SetBarrierIsland();
                            }
                            point.OceanDepth = 1;
                        }
                        islesPlaced++;
                    }
                }
            }
        }
    }

    private static HashSet<int> PlaceRange(Region region, RandomSource random, int originIndex)
    {
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();
        var range = new HashSet<int>();

        queue.AddLast(originIndex);
        explored.Add(originIndex);
        range.Add(originIndex);

        int originBaseLandHeight = Math.Max(1, (int)region.AtIndex(originIndex).BaseLandHeight);
        int maxSize = 70 + random.NextInt(40);

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            if (range.Count > maxSize) break;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null &&
                        point.Land &&
                        point.BaseLandHeight >= originBaseLandHeight - 1 &&
                        point.BaseLandHeight <= originBaseLandHeight + 1 &&
                        (point.BaseLandHeight > 2 || point.DistanceToOcean < 3) &&
                        !explored.Contains(point.Index))
                    {
                        if (lastPoint.BaseLandHeight != point.BaseLandHeight)
                        {
                            queue.AddLast(point.Index);
                        }
                        else
                        {
                            queue.AddFirst(point.Index);
                        }
                        range.Add(point.Index);
                        explored.Add(point.Index);
                    }
                }
            }
        }
        return range;
    }

    private static HashSet<int> PlaceVolcanicArc(Region region, RandomSource random, int originIndex)
    {
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();
        var arc = new HashSet<int>();

        queue.AddLast(originIndex);
        explored.Add(originIndex);
        arc.Add(originIndex);

        int originDistanceToDeepOcean = Math.Max(1, (int)region.AtIndex(originIndex).DistanceToDeepOcean);
        int maxSize = 90 + random.NextInt(50);

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            if (arc.Count > maxSize) break;

            for (int dx = -2; dx <= 2; dx++)
            {
                for (int dz = -2; dz <= 2; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null &&
                        point.OceanDepth == 2 &&
                        point.Divergence < 0 &&
                        point.DistanceToDeepOcean >= originDistanceToDeepOcean - 1 &&
                        point.DistanceToDeepOcean <= originDistanceToDeepOcean + 2 &&
                        !explored.Contains(point.Index))
                    {
                        if (lastPoint.DistanceToDeepOcean != point.DistanceToDeepOcean)
                        {
                            queue.AddLast(point.Index);
                        }
                        else
                        {
                            queue.AddFirst(point.Index);
                        }
                        arc.Add(point.Index);
                        explored.Add(point.Index);
                    }
                }
            }
        }
        return arc;
    }

    private static HashSet<int> PlaceBarrier(Region region, RandomSource random, int originIndex)
    {
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();
        var barrier = new HashSet<int>();

        queue.AddLast(originIndex);
        explored.Add(originIndex);
        barrier.Add(originIndex);

        int originDistanceToLand = Math.Max(1, (int)region.AtIndex(originIndex).DistanceToLand);
        int maxSize = 70 + random.NextInt(40);

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            if (barrier.Count > maxSize) break;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null &&
                        point.OceanDepth == 2 &&
                        point.DistanceToLand >= originDistanceToLand - 1 &&
                        point.DistanceToLand <= originDistanceToLand + 1 &&
                        !explored.Contains(point.Index))
                    {
                        if (lastPoint.DistanceToLand != point.DistanceToLand)
                        {
                            queue.AddLast(point.Index);
                        }
                        else
                        {
                            queue.AddFirst(point.Index);
                        }
                        barrier.Add(point.Index);
                        explored.Add(point.Index);
                    }
                }
            }
        }
        return barrier;
    }
}

/// <summary>AddIslands</summary>
public sealed class AddIslands : RegionTask
{
    public static readonly AddIslands Instance = new();
    private AddIslands() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var random = context.Random;

        for (int attempt = 0, placed = 0; attempt < 130 && placed < 15; attempt++)
        {
            var point = region.Random(random);
            if (point == null) continue;

            if (!point.Land && !point.Shore && point.DistanceToEdge > 2 && context.Generator.ContinentFactor(point) > 0.5f)
            {
                for (int island = 0; island < 12; island++)
                {
                    point.SetLand();
                    point.SetIsland();
                    point.DistanceToOcean = 1;
                    point = region.At(
                        point.X + random.NextInt(4) - random.NextInt(4),
                        point.Z + random.NextInt(4) - random.NextInt(4));
                    if (point == null || (point.Land && !point.Island) || point.DistanceToEdge <= 2 || point.OceanDepth == 1)
                    {
                        break;
                    }
                }
                placed += 1;
            }
        }
    }
}

/// <summary>AnnotateBiomeAltitude</summary>
public sealed class AnnotateBiomeAltitude : RegionTask
{
    public static readonly AnnotateBiomeAltitude Instance = new();
    private AnnotateBiomeAltitude() { }

    public const int WIDTH = 4;

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var random = context.Random;
        var explored = new HashSet<int>();
        var queue = new LinkedList<int>();

        foreach (var point in region.Points())
        {
            if (point.Land && point.Mountain)
            {
                point.BiomeAltitude = 3 * WIDTH;
                queue.AddLast(point.Index);
                explored.Add(point.Index);
            }
        }

        while (queue.Count > 0)
        {
            int last = queue.First.Value;
            queue.RemoveFirst();
            var lastPoint = region.AtIndex(last);
            int nextAltitude = lastPoint.BiomeAltitude - 1;
            if (nextAltitude < 0) continue;

            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    var point = region.AtOffset(last, dx, dz);
                    if (point != null && point.Land && point.BiomeAltitude == 0 && !explored.Contains(point.Index))
                    {
                        if (random.NextInt(13) == 0 && lastPoint.BiomeAltitude != 3 * WIDTH)
                        {
                            point.BiomeAltitude = lastPoint.BiomeAltitude;
                            queue.AddFirst(point.Index);
                        }
                        else
                        {
                            point.BiomeAltitude = (sbyte)nextAltitude;
                            queue.AddLast(point.Index);
                        }
                        explored.Add(point.Index);
                    }
                }
            }
        }

        // 二次扫描：低 -> 中
        foreach (var point in region.Points())
        {
            if (point.Land && point.DiscreteBiomeAltitude == 0 && point.BaseLandHeight >= 4)
            {
                if (point.DiscreteBiomeAltitude == 0 && point.BaseLandHeight >= 4)
                {
                    point.BiomeAltitude = WIDTH;
                }
                if (point.DiscreteBiomeAltitude == 1 && point.BaseLandHeight >= 11)
                {
                    point.BiomeAltitude = 2 * WIDTH;
                }
            }
        }
    }
}

/// <summary>AnnotateClimate</summary>
public sealed class AnnotateClimate : RegionTask
{
    public static readonly AnnotateClimate Instance = new();
    private AnnotateClimate() { }

    public void Apply(RegionGenerator.Context context)
    {
        foreach (var point in context.Region.Points())
        {
            int x = point.X;
            int z = point.Z;

            point.Temperature = (float)context.Generator.TemperatureNoise.Noise(x, z);
            point.Rainfall = (float)context.Generator.RainfallNoise.Noise(x, z);

            float bias;
            if (point.Land)
            {
                float potentialBias = (float)Mth.ClampedMap(point.DistanceToEdge, 2f, 6f, 0f, 1f);
                float oceanProximityBias = (float)Mth.ClampedMap(point.DistanceToOcean, 2f, 6f, 0f, 1f);
                bias = Math.Min(potentialBias, oceanProximityBias);
            }
            else
            {
                bias = 0;
            }

            point.RainfallVariance = (float)Mth.ClampedMap(point.DistanceToWestCoast + (float)context.Generator.RainfallVarianceNoise.Noise(x, z), 0f, 80f, -1, 1);

            float biasTargetTemperature = Mth.Lerp(bias, 5f, point.Temperature);
            float biasTargetRainfall = Mth.Lerp(bias, Math.Min(point.Rainfall + 350f, 500f), point.Rainfall);

            float tempDelta = (float)Mth.ClampedMap((float)context.Generator.OceanicInfluenceNoise.Noise(x, z), -0.8f, 0.9f, -0.07f, 0.23f);
            float oldTemp = point.Temperature;
            point.Temperature = Mth.Lerp(tempDelta, oldTemp, biasTargetTemperature);

            float rainDelta = (float)Mth.ClampedMap(point.Temperature - oldTemp, -2f, 2f, 0f, 0.25f);
            point.Rainfall = Math.Clamp(Mth.Lerp(rainDelta, point.Rainfall, biasTargetRainfall), 0, 500);

            float edgeBiasScale = (float)Mth.ClampedMap(point.DistanceToEdge, 0, 12, 1, 0);
            point.RainfallVariance = Mth.Lerp(edgeBiasScale, point.RainfallVariance, 0);
            point.RainfallVariance = Mth.Clamp(point.RainfallVariance, -1, 1);
        }
    }
}

/// <summary>ChooseRocks</summary>
public sealed class ChooseRocks : RegionTask
{
    public static readonly ChooseRocks Instance = new();
    private ChooseRocks() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var rockArea = context.Generator.RockArea;

        foreach (var point in region.Points())
        {
            // 低 2 位 = 岩石类型，高位 = 种子（层链 uniform 输出）
            point.Rock = (rockArea.Get(point.X, point.Z) << RockType.TYPE_BITS) | FindClosestType(region, point, point.Index);
        }
    }

    private static int FindClosestType(Region region, Region.RegionPoint center, int index)
    {
        int type = center.Land ? RockType.LAND : RockType.OCEAN;
        int minDist = int.MaxValue;
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                var point = region.AtOffset(index, dx, dz);
                int dist = Math.Abs(dx) + Math.Abs(dz);
                if (point != null && dist < minDist)
                {
                    if (point.Island && dist < 4 || point.HotSpotAge > 0 || point.Volcanic)
                    {
                        type = RockType.VOLCANIC;
                        minDist = dist;
                    }
                    else if ((point.Mountain || point.CoastalMountain) && dist < 3)
                    {
                        type = RockType.UPLIFT;
                        minDist = dist;
                    }
                }
            }
        }
        return type;
    }

    private static int UNSIGNED_SHIFT(int v, int n)
    {
        // >>> 语义：C# int 无 >>>，用 uint 中转
        unchecked { return (int)((uint)v >> n); }
    }
}

/// <summary>KarstSurfaceRocks</summary>
public sealed class KarstSurfaceRocks : RegionTask
{
    public static readonly KarstSurfaceRocks Instance = new();
    private KarstSurfaceRocks() { }

    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        foreach (var point in region.Points())
        {
            var surfaceRock = context.Generator.GetSurfaceRock(point);
            point.IsSurfaceRockKarst = surfaceRock.Karst ?? false;
        }
    }
}

/// <summary>AddRiversAndLakes</summary>
public sealed class AddRiversAndLakes : RegionTask
{
    public static readonly AddRiversAndLakes Instance = new();
    private AddRiversAndLakes() { }

    public const float RIVER_LENGTH = 2.7f;
    public const int RIVER_DEPTH = 17;
    public const float RIVER_FEATHER = 0.8f;

    public void Apply(RegionGenerator.Context context)
    {
        if (Environment.GetEnvironmentVariable("TFC_NO_RIVER") != null) return; // 临时禁用
        var region = context.Region;
        var random = context.Random;

        if (Environment.GetEnvironmentVariable("TFC_RIVER_TRACE") != null)
        {
            Console.Error.WriteLine($"RTR region=({region.MinX},{region.MinZ})-({region.MaxX},{region.MaxZ}) size=({region.SizeX},{region.SizeZ})");
        }

        var riverGenerator = new RegionRiverGenerator(region);
        CreateInitialDrains(context, region, riverGenerator);

        if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
        {
            Console.Error.WriteLine($"[RIVER-DBG] After CreateInitialDrains: builder count = {riverGenerator.BuilderCount}");
        }

        var rivers = riverGenerator.Build(e => new RiverEdge(e, random));

        region.SetRivers(rivers);
        if (rivers.Count > 0)
        {
            AnnotateRiverGridScale(region, random, rivers);
        }

        if (Environment.GetEnvironmentVariable("TFC_TASK_LOG") != null)
        {
            var probe = RandomSourceDump.TryClone(random);
            Console.Error.WriteLine($"[TASK] AddRiversAndLakes done: rivers={rivers.Count} probeNextLong={probe!.NextLong()} probeNextInt13={probe.NextInt(13)}");
        }
    }

    private void CreateInitialDrains(RegionGenerator.Context context, Region region, RegionRiverGenerator riverGenerator)
    {
        foreach (var point in region.Points())
        {
            if (point.Shore)
            {
                float bestAngle = FindBestStartingAngle(region, context.Random, point.Index);
                if (!float.IsNaN(bestAngle))
                {
                    long riverSeed = context.Random.NextLong();
                    var rng = new XoroshiroRandomSource(riverSeed);
                    riverGenerator.Add(new RiverGen.Builder(rng, point.X + 0.5f, point.Z + 0.5f, bestAngle, RIVER_LENGTH, RIVER_DEPTH, RIVER_FEATHER));
                }
            }
        }
    }

    private float FindBestStartingAngle(Region region, RandomSource random, int index)
    {
        float bestDistanceMetric = float.Epsilon; // 等价 Java Float.MIN_VALUE（最小正数）！不是 float.MinValue（最负数）
        int bestDistanceCount = 0;
        float bestAngle = float.NaN;

        for (int dirX = -1; dirX <= 1; dirX++)
        {
            for (int dirZ = -1; dirZ <= 1; dirZ++)
            {
                if (dirX == 0 && dirZ == 0) continue;

                var dirPoint = region.AtOffset(index, 4 * dirX, 4 * dirZ);
                if (dirPoint != null && dirPoint.Land)
                {
                    float dirDistanceMetric = dirPoint.DistanceToOcean - Math.Abs(dirX) - Math.Abs(dirZ);
                    if (dirDistanceMetric > bestDistanceMetric || (dirDistanceMetric == bestDistanceMetric && random.NextInt(1 + bestDistanceCount) == 0))
                    {
                        if (dirDistanceMetric > bestDistanceMetric)
                        {
                            bestDistanceMetric = dirDistanceMetric;
                            bestDistanceCount = 0;
                        }
                        bestDistanceCount += 1;
                        bestAngle = (float)Math.Atan2(dirZ, dirX);
                    }
                }
            }
        }
        if (!float.IsNaN(bestAngle))
        {
            bestAngle += random.NextFloat() * 0.2f - 0.1f;
        }
        return bestAngle;
    }

    private void AnnotateRiverGridScale(Region region, RandomSource random, List<RiverEdge> rivers)
    {
        var sourceVertexToEdge = new Dictionary<RiverGen.Vertex, RiverEdge>();
        foreach (var edge in rivers)
        {
            sourceVertexToEdge[edge.Source] = edge;
        }

        foreach (var edge in rivers)
        {
            edge.LinkToDrain(sourceVertexToEdge.GetValueOrDefault(edge.Drain));
        }

        foreach (var edge in rivers)
        {
            if (!edge.SourceEdge)
            {
                int width = RiverEdge.MIN_WIDTH;
                var e = edge;
                while (e != null)
                {
                    e.Width = Math.Max(e.Width, width);
                    e = e.DrainEdge;
                    width = Math.Min(width + 2, RiverEdge.MAX_WIDTH);
                }
            }
        }

        foreach (var edge in rivers)
        {
            if (!edge.SourceEdge && random.NextInt(3) == 0)
            {
                PlaceLakeNear(region, edge, 1, 1);
                PlaceLakeNear(region, edge, -1, 1);
                PlaceLakeNear(region, edge, 1, -1);
                PlaceLakeNear(region, edge, -1, -1);
            }
            else if (edge.Width > RiverEdge.MIN_VALLEY_WIDTH)
            {
                AnnotateRiverGridScale(region, edge);
            }
        }
    }

    private void AnnotateRiverGridScale(Region region, RiverEdge edge)
    {
        int ux = (int)edge.Source.x;
        int uy = (int)edge.Source.y;
        int vx = (int)edge.Drain.x;
        int vy = (int)edge.Drain.y;
        int dx = vx - ux;
        int dy = vy - uy;
        double mag = Math.Sqrt(dx * dx + dy * dy);
        double unitX = dx / mag;
        double unitY = dy / mag;

        double i = 0;
        while (i <= mag)
        {
            int x = (int)(ux + unitX * i);
            int y = (int)(uy + unitY * i);
            SetRiver(region.At(x, y));
            SetRiver(region.At(x + 1, y));
            SetRiver(region.At(x, y + 1));
            SetRiver(region.At(x + 1, y + 1));
            i += 1;
        }
    }

    private static void SetRiver(Region.RegionPoint? point)
    {
        if (point != null && point.Land)
        {
            point.SetRiver();
            point.Rainfall += 0.09f * (500f - point.Rainfall);
        }
    }

    private void PlaceLakeNear(Region region, RiverEdge edge, int offsetX, int offsetZ)
    {
        int gridX = (int)(edge.Source.x + 0.3f * offsetX);
        int gridZ = (int)(edge.Source.y + 0.3f * offsetZ);

        var point = region.At(gridX, gridZ);
        if (point != null && point.Land && point.DistanceToOcean >= 2 && point.DistanceToEdge >= 2)
        {
            point.SetLake();
            point.Rainfall += 0.09f * (500f - point.Rainfall);
        }
    }

    private sealed class RegionRiverGenerator : RiverGen.MultiParallelBuilder
    {
        private readonly Region region;

        public RegionRiverGenerator(Region region)
        {
            this.region = region;
        }

        protected override bool IsLegal(RiverGen.Vertex prev, RiverGen.Vertex vertex)
        {
            var prevPoint = Vertex2Point(prev);
            var newPoint = Vertex2Point(vertex);
            return newPoint != null && prevPoint != null
                && newPoint.Land
                && newPoint.DistanceToOcean >= prevPoint.DistanceToOcean
                && newPoint.DistanceToOcean >= Math.Min(3, prev.distance / 2);
        }

        private Region.RegionPoint? Vertex2Point(RiverGen.Vertex vertex)
        {
            int gridX = (int)Math.Floor(vertex.x + 0.5);
            int gridZ = (int)Math.Floor(vertex.y + 0.5);
            return region.At(gridX, gridZ);
        }
    }
}

/// <summary>ChooseBiomes</summary>
public sealed class ChooseBiomes : RegionTask
{
    public static readonly ChooseBiomes Instance = new();
    private ChooseBiomes() { }

    private static readonly int[][] ALTITUDE_BIOMES = {
        new[]{TFCLayers.PLAINS, TFCLayers.PLAINS, TFCLayers.HILLS, TFCLayers.HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.LOW_CANYONS, TFCLayers.LOWLANDS, TFCLayers.LOWLANDS},
        new[]{TFCLayers.PLAINS, TFCLayers.HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.HIGHLANDS, TFCLayers.BUTTES, TFCLayers.MESAS, TFCLayers.BADLANDS, TFCLayers.PLATEAU_WIDE, TFCLayers.CANYONS, TFCLayers.CANYONS, TFCLayers.LOW_CANYONS},
        new[]{TFCLayers.HIGHLANDS, TFCLayers.HIGHLANDS, TFCLayers.HIGHLANDS, TFCLayers.HIGHLANDS, TFCLayers.ROLLING_HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.BADLANDS, TFCLayers.BADLANDS, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.PLATEAU, TFCLayers.PLATEAU, TFCLayers.PLATEAU, TFCLayers.PLATEAU_WIDE, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS},
    };
    private static readonly int[][] ICE_SHEET_ALTITUDE_BIOMES = {
        new[]{TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET_TUYAS},
        new[]{TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET_TUYAS, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET_TUYAS},
        new[]{TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET, TFCLayers.ICE_SHEET_TUYAS, TFCLayers.ICE_SHEET_TUYAS, TFCLayers.ICE_SHEET_MOUNTAINS, TFCLayers.ICE_SHEET_MOUNTAINS},
    };
    private static readonly int[][] PALEO_ICE_SHEET_ALTITUDE_BIOMES = {
        new[]{TFCLayers.PATTERNED_GROUND, TFCLayers.INVERTED_PATTERNED_GROUND, TFCLayers.KNOB_AND_KETTLE, TFCLayers.KNOB_AND_KETTLE, TFCLayers.KNOB_AND_KETTLE, TFCLayers.DRUMLINS, TFCLayers.TUYAS, TFCLayers.LOWLANDS, TFCLayers.LOWLANDS},
        new[]{TFCLayers.PATTERNED_GROUND, TFCLayers.KNOB_AND_KETTLE, TFCLayers.DRUMLINS, TFCLayers.DRUMLINS, TFCLayers.DRUMLINS, TFCLayers.DRUMLINS, TFCLayers.TUYAS, TFCLayers.TUYAS},
        new[]{TFCLayers.DRUMLINS, TFCLayers.DRUMLINS, TFCLayers.DRUMLINS, TFCLayers.BADLANDS, TFCLayers.BADLANDS, TFCLayers.PLATEAU, TFCLayers.PLATEAU, TFCLayers.PLATEAU, TFCLayers.PLATEAU_WIDE, TFCLayers.ICE_SHEET_MOUNTAINS},
    };
    private static readonly int[][] DESERT_ALTITUDE_BIOMES = {
        new[]{TFCLayers.BUTTES, TFCLayers.GRASSY_DUNES, TFCLayers.DUNE_SEA, TFCLayers.DUNE_SEA, TFCLayers.DUNE_SEA, TFCLayers.SALT_FLATS, TFCLayers.SALT_FLATS},
        new[]{TFCLayers.DUNE_SEA, TFCLayers.BUTTES, TFCLayers.BUTTES, TFCLayers.HOODOOS, TFCLayers.MESAS, TFCLayers.MESAS, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.BADLANDS, TFCLayers.PLATEAU, TFCLayers.CANYONS, TFCLayers.WHORLED_CANYONS},
        new[]{TFCLayers.HOODOOS, TFCLayers.MESAS, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.ROCKY_PLATEAU, TFCLayers.ROCKY_PLATEAU, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS, TFCLayers.WHORLED_CANYONS},
    };
    private static readonly int[][] SEMI_ARID_ALTITUDE_BIOMES = {
        new[]{TFCLayers.PLAINS, TFCLayers.HILLS, TFCLayers.HILLS, TFCLayers.GRASSY_DUNES, TFCLayers.GRASSY_DUNES, TFCLayers.GRASSY_DUNES, TFCLayers.GRASSY_DUNES, TFCLayers.LOW_CANYONS, TFCLayers.LOWLANDS, TFCLayers.LOWLANDS, TFCLayers.MUD_FLATS},
        new[]{TFCLayers.PLAINS, TFCLayers.HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.HIGHLANDS, TFCLayers.HIGHLANDS, TFCLayers.BADLANDS, TFCLayers.BADLANDS, TFCLayers.PLATEAU_WIDE, TFCLayers.CANYONS, TFCLayers.LOW_CANYONS, TFCLayers.WHORLED_CANYONS, TFCLayers.BUTTES, TFCLayers.MESAS, TFCLayers.MESAS, TFCLayers.HOODOOS},
        new[]{TFCLayers.HIGHLANDS, TFCLayers.HIGHLANDS, TFCLayers.MESAS, TFCLayers.HOODOOS, TFCLayers.ROLLING_HILLS, TFCLayers.ROLLING_HILLS, TFCLayers.BADLANDS, TFCLayers.BADLANDS, TFCLayers.PLATEAU_WIDE, TFCLayers.ROCKY_PLATEAU, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.STAIR_STEP_CANYONS, TFCLayers.OLD_MOUNTAINS, TFCLayers.OLD_MOUNTAINS, TFCLayers.WHORLED_CANYONS},
    };
    private static readonly int[] KNOB_AND_KETTLE_BIOMES = { TFCLayers.KNOB_AND_KETTLE, TFCLayers.PATTERNED_GROUND, TFCLayers.INVERTED_PATTERNED_GROUND };
    private static readonly int[] ISLAND_BIOMES = { TFCLayers.ROLLING_HILLS, TFCLayers.VOLCANIC_OCEANIC_MOUNTAINS, TFCLayers.VOLCANIC_MOUNTAIN_ISLANDS, TFCLayers.GUANO_ISLAND };
    private static readonly int[] RIFT_VALLEY_BIOMES = { TFCLayers.RIFT_VALLEY, TFCLayers.RIFT_VALLEY, TFCLayers.RIFT_VALLEY, TFCLayers.RIFT_LAKE, TFCLayers.RIFT_LAKE };
    private static readonly int[] VOLCANIC_ARC_BIOMES = { TFCLayers.VOLCANIC_OCEANIC_MOUNTAINS, TFCLayers.VOLCANIC_OCEANIC_MOUNTAINS, TFCLayers.VOLCANIC_OCEANIC_MOUNTAINS, TFCLayers.VOLCANIC_ISLAND, TFCLayers.OCEANIC_VOLCANIC_ARC };
    private static readonly int[] BARRIER_ISLAND_BIOMES = { TFCLayers.PLAINS, TFCLayers.PLAINS, TFCLayers.PLAINS, TFCLayers.SALT_MARSH, TFCLayers.TIDAL_FLATS };


    public void Apply(RegionGenerator.Context context)
    {
        var region = context.Region;
        var blobArea = context.Generator.BiomeArea;
        long rngSeed = context.Random.NextLong();
        long climateSeed = context.Random.NextLong();

        foreach (var point in region.Points())
        {
            int areaSeed = blobArea.Get(point.X, point.Z);
            if (point.Island)
            {
                point.Biome = RandomSeededFrom(rngSeed, areaSeed, ISLAND_BIOMES);
            }
            else if (point.DistanceToEdge < 3 && point.Divergence > 0 && point.Land)
            {
                point.Biome = point.DistanceToOcean > 2 ? RandomSeededFrom(rngSeed, areaSeed, RIFT_VALLEY_BIOMES) : TFCLayers.RIFT_VALLEY;
            }
            else if (point.River && point.DistanceToEdge > 4 && point.Temperature > -16f + 0.006f * point.Rainfall)
            {
                point.Biome = TFCLayers.RIVER_VALLEY;
            }
            else if (point.Mountain)
            {
                float temp = point.Temperature;
                if (point.Divergence < 0 && point.DistanceToEdge < 1.2 * context.Generator.ContinentNoise.Noise(point.X, point.Z) - 5.2)
                {
                    float maxIceSheetTemp = -16f + 0.006f * point.Rainfall;
                    if (point.DistanceToOcean < 3)
                    {
                        if (temp < maxIceSheetTemp + 2) point.Biome = TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 6) point.Biome = TFCLayers.GLACIATED_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 10) point.Biome = TFCLayers.GLACIALLY_CARVED_OCEANIC_MOUNTAINS;
                        else point.Biome = TFCLayers.OCEANIC_MOUNTAINS;
                    }
                    else
                    {
                        if (temp < maxIceSheetTemp + 4) point.Biome = TFCLayers.ICE_SHEET_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 8) point.Biome = TFCLayers.GLACIATED_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 11) point.Biome = TFCLayers.GLACIALLY_CARVED_MOUNTAINS;
                        else point.Biome = TFCLayers.COLLISIONAL_MOUNTAINS;
                    }
                }
                else if (point.CoastalMountain)
                {
                    float maxIceSheetTemp = -16f + 0.006f * point.Rainfall;
                    if (point.Volcanic)
                    {
                        if (temp < maxIceSheetTemp + 2) point.Biome = TFCLayers.ICE_SHEET_VOLCANIC_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 6) point.Biome = TFCLayers.GLACIATED_VOLCANIC_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 10) point.Biome = TFCLayers.GLACIALLY_CARVED_VOLCANIC_OCEANIC_MOUNTAINS;
                        else point.Biome = TFCLayers.VOLCANIC_OCEANIC_MOUNTAINS;
                    }
                    else
                    {
                        if (temp < maxIceSheetTemp + 2) point.Biome = TFCLayers.ICE_SHEET_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 6) point.Biome = TFCLayers.GLACIATED_OCEANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 10) point.Biome = TFCLayers.GLACIALLY_CARVED_OCEANIC_MOUNTAINS;
                        else point.Biome = TFCLayers.OCEANIC_MOUNTAINS;
                    }
                }
                else
                {
                    float maxIceSheetTemp = -14f + 0.006f * point.Rainfall;
                    if (point.Volcanic)
                    {
                        if (temp < maxIceSheetTemp + 4) point.Biome = TFCLayers.ICE_SHEET_VOLCANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 8) point.Biome = TFCLayers.GLACIATED_VOLCANIC_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 11) point.Biome = TFCLayers.GLACIALLY_CARVED_VOLCANIC_MOUNTAINS;
                        else point.Biome = TFCLayers.VOLCANIC_MOUNTAINS;
                    }
                    else
                    {
                        if (temp < maxIceSheetTemp + 4) point.Biome = TFCLayers.ICE_SHEET_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 8) point.Biome = TFCLayers.GLACIATED_MOUNTAINS;
                        else if (temp < maxIceSheetTemp + 11) point.Biome = TFCLayers.GLACIALLY_CARVED_MOUNTAINS;
                        else point.Biome = TFCLayers.MOUNTAINS;
                    }
                }
            }
            else if (point.Land)
            {
                float rain = point.Rainfall;
                float maxIceSheetTemp = -17f + 0.006f * rain;
                float temp = point.Temperature;
                if (temp < maxIceSheetTemp)
                {
                    int biome = RandomSeededFrom(rngSeed, areaSeed, ICE_SHEET_ALTITUDE_BIOMES[point.DiscreteBiomeAltitude]);
                    if (point.DistanceToOcean < 3 && TFCLayers.IsFlatIceSheet(biome))
                    {
                        biome = TFCLayers.ICE_SHEET_OCEANIC;
                    }
                    point.Biome = biome;
                }
                else if (temp < maxIceSheetTemp + 1)
                {
                    point.Biome = TFCLayers.ICE_SHEET_EDGE;
                }
                else if (temp < maxIceSheetTemp + 2.5)
                {
                    point.Biome = RandomSeededFrom(rngSeed, areaSeed, KNOB_AND_KETTLE_BIOMES);
                }
                else if (temp < maxIceSheetTemp + 6)
                {
                    point.Biome = RandomSeededFrom(rngSeed, areaSeed, PALEO_ICE_SHEET_ALTITUDE_BIOMES[point.DiscreteBiomeAltitude]);
                }
                else if (rain < 60)
                {
                    point.Biome = RandomSeededFrom(rngSeed, areaSeed, DESERT_ALTITUDE_BIOMES[point.DiscreteBiomeAltitude]);
                }
                else if (rain < 155)
                {
                    point.Biome = RandomSeededFrom(rngSeed, areaSeed, SEMI_ARID_ALTITUDE_BIOMES[point.DiscreteBiomeAltitude]);
                }
                else
                {
                    point.Biome = RandomSeededFrom(rngSeed, areaSeed, ALTITUDE_BIOMES[point.DiscreteBiomeAltitude]);
                }
            }
            else if (point.BarrierIsland)
            {
                point.Biome = point.Volcanic
                    ? RandomSeededFrom(rngSeed, areaSeed, VOLCANIC_ARC_BIOMES)
                    : RandomSeededFrom(rngSeed, areaSeed, BARRIER_ISLAND_BIOMES);
            }
            else if (point.OceanDepth == 1)
            {
                point.Biome = point.Volcanic ? TFCLayers.OCEANIC_VOLCANIC_ARC : TFCLayers.OCEAN_REEF;
            }
            else if (point.OceanDepth == 2)
            {
                point.Biome = point.Temperature > 12 && point.DistanceToLand > 4 ? TFCLayers.OCEAN_ATOLLS : TFCLayers.OCEAN;
            }
            else if (point.OceanDepth == 3)
            {
                point.Biome = TFCLayers.OCEAN_RIDGE;
            }
            else if (point.OceanDepth == 5)
            {
                point.Biome = TFCLayers.DEEP_OCEAN_TRENCH;
            }
            else
            {
                point.Biome = point.Temperature > 12 && point.DistanceToLand > 3 ? TFCLayers.DEEP_OCEAN_ATOLLS : TFCLayers.DEEP_OCEAN;
            }

            sbyte age = point.HotSpotAge;
            if (age > 0)
            {
                if (age == 4 && (point.Biome == TFCLayers.OCEAN || point.Biome == TFCLayers.DEEP_OCEAN || point.Biome == TFCLayers.OCEAN_REEF || point.Biome == TFCLayers.DEEP_OCEAN_TRENCH))
                {
                    point.Biome = TFCLayers.SUNKEN_SHIELD_VOLCANO;
                }
                else
                {
                    point.Biome = GetHotSpotBiome(point.HotSpotAge);
                }
            }

            float minRainForLowFreshWaterBiomes = 90f + Mth.FloorMod(areaSeed ^ climateSeed, 40);
            float rainfall = point.Rainfall;
            float temperature = point.Temperature;
            if (rainfall < minRainForLowFreshWaterBiomes)
            {
                if (rainfall <= 55)
                {
                    if (point.Biome == TFCLayers.LOWLANDS || point.Biome == TFCLayers.LOW_CANYONS) point.Biome = TFCLayers.SALT_FLATS;
                    else if (point.Biome == TFCLayers.HILLS || point.Biome == TFCLayers.ROLLING_HILLS || point.Biome == TFCLayers.PLATEAU) point.Biome = TFCLayers.DUNE_SEA;
                }
            }
            if (rainfall < 145 && (point.Biome == TFCLayers.PATTERNED_GROUND || point.Biome == TFCLayers.INVERTED_PATTERNED_GROUND)) point.Biome = TFCLayers.STONE_CIRCLES;

            float maxRainfallForBadlands = 420f + Mth.FloorMod(areaSeed ^ climateSeed, 40);
            if (rainfall > maxRainfallForBadlands)
            {
                if (point.Biome == TFCLayers.BADLANDS) point.Biome = TFCLayers.HIGHLANDS;
            }

            float maxIceSheetTemp2 = -14f + 0.006f * rainfall;
            if (point.Land && temperature < maxIceSheetTemp2)
            {
                int biome = point.Biome;
                if (biome == TFCLayers.ACTIVE_SHIELD_VOLCANO || biome == TFCLayers.DORMANT_SHIELD_VOLCANO || biome == TFCLayers.EXTINCT_SHIELD_VOLCANO) point.Biome = TFCLayers.ICE_SHEET_SHIELD_VOLCANO;
            }
            else if (temperature < maxIceSheetTemp2 + 4f)
            {
                int biome = point.Biome;
                if (biome == TFCLayers.ACTIVE_SHIELD_VOLCANO || biome == TFCLayers.DORMANT_SHIELD_VOLCANO || biome == TFCLayers.EXTINCT_SHIELD_VOLCANO) point.Biome = TFCLayers.GLACIATED_SHIELD_VOLCANO;
            }

            if (point.IsSurfaceRockKarst)
            {
                if (rainfall > 375)
                {
                    if (rainfall > 425 && rainfall + 10 * temperature > 500)
                    {
                        point.Biome = GetTowerKarstBiome(point.Biome);
                    }
                    else if (temperature > 9)
                    {
                        point.Biome = GetShilinBiome(point.Biome);
                    }
                    else if (temperature < 0)
                    {
                        point.Biome = GetBurrenBiome(point.Biome);
                    }
                    else
                    {
                        point.Biome = GetDolineBiome(point.Biome);
                    }
                }
                else if (rainfall > 250)
                {
                    point.Biome = temperature > 5 ? GetCenoteBiome(point.Biome) : GetDolineBiome(point.Biome);
                }
            }

            if (point.DistanceToOcean <= 2 && point.Biome == TFCLayers.LOWLANDS && point.Rainfall > 220 && point.Temperature > 18)
            {
                point.Biome = TFCLayers.SALT_MARSH;
            }

            if (Environment.GetEnvironmentVariable("TFC_CB_DEBUG") != null && point.X == 0 && point.Z == 0)
            {
                Console.WriteLine($"CB({point.X},{point.Z}) rng={rngSeed} area={areaSeed}"
                    + $" island={point.Island} dEdge={point.DistanceToEdge} divr={point.Divergence}"
                    + $" land={point.Land} river={point.River} lake={point.Lake}"
                    + $" mtn={point.Mountain} coastM={point.CoastalMountain} volc={point.Volcanic}"
                    + $" bIsl={point.BarrierIsland} oceanD={point.OceanDepth} dOcean={point.DistanceToOcean}"
                    + $" dLand={point.DistanceToLand} rain={point.Rainfall} temp={point.Temperature}"
                    + $" bAlt={point.BiomeAltitude} dAlt={point.DiscreteBiomeAltitude} karst={point.IsSurfaceRockKarst}"
                    + $" hAge={point.HotSpotAge} -> biome={point.Biome}");
            }

            if (point.Lake && TFCLayers.HasLake(point.Biome))
            {
                point.Biome = TFCLayers.LakeFor(point.Biome);
            }
        }
    }

    private static int GetHotSpotBiome(int age)
    {
        if (age == 4) return TFCLayers.ANCIENT_SHIELD_VOLCANO;
        if (age == 3) return TFCLayers.EXTINCT_SHIELD_VOLCANO;
        if (age == 2) return TFCLayers.DORMANT_SHIELD_VOLCANO;
        if (age == 1) return TFCLayers.ACTIVE_SHIELD_VOLCANO;
        return TFCLayers.PLAINS;
    }

    private static int GetTowerKarstBiome(int biome)
    {
        if (biome == TFCLayers.SALT_MARSH) return TFCLayers.TOWER_KARST_BAY;
        else if (biome == TFCLayers.LOWLANDS) return TFCLayers.TOWER_KARST_LAKE;
        else if (biome == TFCLayers.PLAINS || biome == TFCLayers.LOW_CANYONS) return TFCLayers.TOWER_KARST_PLAINS;
        else if (biome == TFCLayers.CANYONS) return TFCLayers.TOWER_KARST_CANYONS;
        else if (biome == TFCLayers.HILLS || biome == TFCLayers.ROLLING_HILLS || biome == TFCLayers.BADLANDS) return TFCLayers.TOWER_KARST_HILLS;
        else if (biome == TFCLayers.HIGHLANDS) return TFCLayers.TOWER_KARST_HIGHLANDS;
        else if (biome == TFCLayers.PLATEAU || biome == TFCLayers.PLATEAU_WIDE) return TFCLayers.EXTREME_DOLINE_PLATEAU;
        else if (biome == TFCLayers.OLD_MOUNTAINS || biome == TFCLayers.MOUNTAINS || biome == TFCLayers.OCEANIC_MOUNTAINS) return TFCLayers.EXTREME_DOLINE_MOUNTAINS;
        else return biome;
    }

    private static int GetShilinBiome(int biome)
    {
        if (biome == TFCLayers.PLAINS) return TFCLayers.SHILIN_PLAINS;
        if (biome == TFCLayers.CANYONS) return TFCLayers.SHILIN_CANYONS;
        if (biome == TFCLayers.ROLLING_HILLS || biome == TFCLayers.BADLANDS) return TFCLayers.SHILIN_HILLS;
        if (biome == TFCLayers.PLATEAU || biome == TFCLayers.PLATEAU_WIDE) return TFCLayers.SHILIN_PLATEAU;
        if (biome == TFCLayers.HIGHLANDS) return TFCLayers.SHILIN_HIGHLANDS;
        return biome;
    }

    private static int GetBurrenBiome(int biome)
    {
        if (biome == TFCLayers.PLAINS || biome == TFCLayers.CANYONS) return TFCLayers.BURREN_PLAINS;
        if (biome == TFCLayers.BADLANDS || biome == TFCLayers.HILLS || biome == TFCLayers.ROLLING_HILLS) return TFCLayers.BURREN_BADLANDS;
        if (biome == TFCLayers.DRUMLINS) return TFCLayers.BURREN_ROCHE_MOUTONEE;
        if (biome == TFCLayers.HIGHLANDS) return TFCLayers.BURREN_BADLANDS_TALL;
        if (biome == TFCLayers.PLATEAU || biome == TFCLayers.PLATEAU_WIDE) return TFCLayers.BURREN_PLATEAU;
        return biome;
    }

    private static int GetDolineBiome(int biome)
    {
        if (biome == TFCLayers.CANYONS) return TFCLayers.DOLINE_CANYONS;
        else if (biome == TFCLayers.PLAINS || biome == TFCLayers.LOW_CANYONS) return TFCLayers.DOLINE_PLAINS;
        else if (biome == TFCLayers.HILLS) return TFCLayers.DOLINE_HILLS;
        else if (biome == TFCLayers.ROLLING_HILLS) return TFCLayers.DOLINE_ROLLING_HILLS;
        else if (biome == TFCLayers.HIGHLANDS) return TFCLayers.DOLINE_HIGHLANDS;
        else if (biome == TFCLayers.PLATEAU || biome == TFCLayers.PLATEAU_WIDE) return TFCLayers.DOLINE_PLATEAU;
        return biome;
    }

    private static int GetCenoteBiome(int biome)
    {
        if (biome == TFCLayers.CANYONS) return TFCLayers.CENOTE_CANYONS;
        else if (biome == TFCLayers.PLAINS || biome == TFCLayers.LOW_CANYONS) return TFCLayers.CENOTE_PLAINS;
        else if (biome == TFCLayers.HILLS) return TFCLayers.CENOTE_HILLS;
        else if (biome == TFCLayers.ROLLING_HILLS) return TFCLayers.CENOTE_ROLLING_HILLS;
        else if (biome == TFCLayers.HIGHLANDS) return TFCLayers.CENOTE_HIGHLANDS;
        else if (biome == TFCLayers.PLATEAU || biome == TFCLayers.PLATEAU_WIDE) return TFCLayers.CENOTE_PLATEAU;
        return biome;
    }

    private static int RandomSeededFrom(long rngSeed, int areaSeed, int[] choices)
        => choices[Mth.FloorMod(rngSeed ^ areaSeed, choices.Length)];
}