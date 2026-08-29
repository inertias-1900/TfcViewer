using System.Threading;
using TfcViewer.Core;
using TfcViewer.World.Region;

namespace TfcViewer.World.River;

/// <summary>RiverHelpers（点线距离等）</summary>
public static class RiverHelpers
{
    public static double DistancePointToLineSq(double vx, double vy, double wx, double wy, double px, double py)
    {
        double t = ProjectAlongLine(vx, vy, wx, wy, px, py);
        double x0 = vx + t * (wx - vx);
        double y0 = vy + t * (wy - vy);
        return Norm2(x0 - px, y0 - py);
    }

    public static double ProjectAlongLine(double vx, double vy, double wx, double wy, double px, double py)
    {
        double l2 = Norm2(vx - wx, vy - wy);
        if (l2 == 0) return l2;
        return Mth.Clamp((((px - vx) * (wx - vx)) + ((py - vy) * (wy - vy))) / l2, 0, 1);
    }

    public static double Norm2(double x, double y) => x * x + y * y;
    public static double NormInf(double x, double y) => Math.Max(Math.Abs(x), Math.Abs(y));
    public static int Floor(double f) => f >= 0 ? (int)f : (int)f - 1;
}

/// <summary>MidpointFractal（中点位移分形）</summary>
public sealed class MidpointFractal
{
    private const double JITTER_MAX = 0.2f;
    private const double JITTER_MIN = 0.05f;
    private const int MAX_BISECTIONS = 10;

    private static readonly double[] ENCOMPASSING_RANGES = BuildRanges();

    private static double[] BuildRanges()
    {
        var ranges = new double[MAX_BISECTIONS];
        double sqrt2 = Math.Sqrt(2);
        double delta = 0, midpointDelta = 0, prevMidpoint, norm = 1;
        for (int i = 0; i < MAX_BISECTIONS; i++)
        {
            ranges[i] = Math.Max(delta, midpointDelta);
            prevMidpoint = midpointDelta;
            midpointDelta = 0.5f * (delta + midpointDelta) + sqrt2 * JITTER_MAX * norm;
            delta = Math.Max(prevMidpoint, delta);
            norm *= 0.5f + JITTER_MAX;
        }
        return ranges;
    }

    private static double[] Bisect(RandomSource random, int bisections, double[] segments)
    {
        for (int i = 0; i < bisections; i++)
        {
            var splitSegments = new double[(segments.Length << 1) - 2];
            splitSegments[0] = segments[0];
            splitSegments[1] = segments[1];

            int splitIndex = 2;
            for (int index = 0; index < segments.Length - 2; index += 2)
            {
                double sourceX = segments[index];
                double sourceY = segments[index + 1];
                double drainX = segments[index + 2];
                double drainY = segments[index + 3];

                double norm = RiverHelpers.NormInf(sourceX - drainX, sourceY - drainY);
                double bisectX = RandomJitter(random) * norm + (sourceX + drainX) * 0.5f;
                double bisectY = RandomJitter(random) * norm + (sourceY + drainY) * 0.5f;

                splitSegments[splitIndex] = bisectX;
                splitSegments[splitIndex + 1] = bisectY;
                splitSegments[splitIndex + 2] = drainX;
                splitSegments[splitIndex + 3] = drainY;
                splitIndex += 4;
            }
            segments = splitSegments;
        }
        return segments;
    }

    private static double RandomJitter(RandomSource random)
    {
        double value = 2f * random.NextDouble() - 1; // [-1, 1]
        return (JITTER_MAX - JITTER_MIN) * value + (value < 0 ? -JITTER_MIN : JITTER_MIN);
    }

    public readonly double[] Segments;
    private readonly double norm;

    public MidpointFractal(RandomSource random, int bisections, double sourceX, double sourceY, double drainX, double drainY)
    {
        Segments = Bisect(random, bisections, new[] { sourceX, sourceY, drainX, drainY });
        norm = ENCOMPASSING_RANGES[bisections] * RiverHelpers.NormInf(sourceX - drainX, sourceY - drainY);
    }

    public bool MaybeIntersect(double x, double y, double distance)
    {
        double d = RiverHelpers.DistancePointToLineSq(Segments[0], Segments[1], Segments[Segments.Length - 2], Segments[Segments.Length - 1], x, y);
        double t = distance + norm;
        return d <= t * t;
    }

    public bool Intersect(double x, double y, double distance)
        => MaybeIntersect(x, y, distance) && IntersectIndex(x, y, distance * distance) != -1;

    public double IntersectDistance(double x, double y)
    {
        double min = double.MaxValue;
        for (int i = 0; i < Segments.Length - 2; i += 2)
        {
            double d = RiverHelpers.DistancePointToLineSq(Segments[i], Segments[i + 1], Segments[i + 2], Segments[i + 3], x, y);
            if (d < min) min = d;
        }
        return min;
    }

    private int IntersectIndex(double x, double y, double distSq)
    {
        for (int i = 0; i < Segments.Length - 2; i += 2)
        {
            double d = RiverHelpers.DistancePointToLineSq(Segments[i], Segments[i + 1], Segments[i + 2], Segments[i + 3], x, y);
            if (d < distSq) return i;
        }
        return -1;
    }
}

/// <summary>River（河流生成器）</summary>
public static class RiverGen
{
    private const double MIN_BRANCH_ANGLE = 0.4f;
    private const int MIN_BRANCH_DISTANCE = 2;
    private const int MIN_RIVER_EDGE_COUNT = 6;

    private static double Distance(Edge edge, Vertex vertex)
        => RiverHelpers.DistancePointToLineSq(edge.Source.x, edge.Source.y, edge.Drain.x, edge.Drain.y, vertex.x, vertex.y);

    private static bool Intersect(Vertex p1, Vertex q1, Vertex p2, Vertex q2)
    {
        int o1 = Orientation(p1, q1, p2);
        int o2 = Orientation(p1, q1, q2);
        int o3 = Orientation(p2, q2, p1);
        int o4 = Orientation(p2, q2, q1);

        return (o1 != o2 && o3 != o4)
            || (o1 == 0 && IntersectCollinear(p1, p2, q1))
            || (o2 == 0 && IntersectCollinear(p1, q2, q1))
            || (o3 == 0 && IntersectCollinear(p2, p1, q2))
            || (o4 == 0 && IntersectCollinear(p2, q1, q2));
    }

    private static bool IntersectCollinear(Vertex p, Vertex q, Vertex r)
        => q.x <= Math.Max(p.x, r.x) && q.x >= Math.Min(p.x, r.x) && q.y <= Math.Max(p.y, r.y) && q.y >= Math.Min(p.y, r.y);

    private static int Orientation(Vertex p, Vertex q, Vertex r)
    {
        double value = (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
        if (value == 0) return 0;
        return value > 0 ? 1 : 2;
    }

    public interface Context
    {
        bool IntersectAny(Edge edge);
    }

    /// <summary>
    /// 河流顶点。使用唯一 ID 实现引用语义的 != 比较（匹配 Java record 的引用相等）。
    /// Java 中 e.source != edge.drain 是引用比较，永远为 true（不同对象）；
    /// C# record struct 默认是值比较，会导致不同顶点但值相同时误判为同一顶点，跳过相交检测。
    /// </summary>
    public readonly struct Vertex : IEquatable<Vertex>
    {
        private static int _nextId;
        public readonly double x, y, angle, length;
        public readonly int distance;
        public readonly int Id;

        public Vertex(double x, double y, double angle, double length, int distance)
        {
            this.x = x; this.y = y; this.angle = angle; this.length = length; this.distance = distance;
            Id = Interlocked.Increment(ref _nextId);
        }

        public bool Equals(Vertex other) => Id == other.Id;
        public override bool Equals(object? obj) => obj is Vertex v && Id == v.Id;
        public override int GetHashCode() => Id;
        public static bool operator ==(Vertex left, Vertex right) => left.Id == right.Id;
        public static bool operator !=(Vertex left, Vertex right) => left.Id != right.Id;
    }

    public sealed record Edge(Vertex Source, Vertex Drain)
    {
        public MidpointFractal Fractal(RandomSource random, int bisections)
            => new(random, bisections, Source.x, Source.y, Drain.x, Drain.y);
    }

    public sealed class Builder : Context
    {
        private readonly Queue<Edge> branchQueue;
        private readonly RandomSource random;
        private readonly List<Edge> edges;
        private readonly Vertex root;
        private readonly List<Edge> branch;
        private readonly int depth;
        private readonly double featherSq;

        public Builder(RandomSource random, double drainX, double drainY, double angle, double length, int depth, double feather)
        {
            this.branchQueue = new Queue<Edge>();
            this.random = random;
            this.edges = new List<Edge>();
            this.root = new Vertex(drainX, drainY, angle, length, 0);
            this.branch = new List<Edge>();
            this.depth = depth;
            this.featherSq = feather * feather;
        }

        public List<Edge> Edges => edges;
        public Queue<Edge> BranchQueue => branchQueue;

        internal bool BuildInitialBranch(Context context)
        {
            Vertex prev = root;
            int length = depth + random.NextInt(1 + (int)(depth * 0.3f));
            for (int i = 0; i < length; i++)
            {
                Vertex next = ComputeNext(prev, prev.length, prev.distance);
                var nextEdge = new Edge(next, prev);
                if (context.IntersectAny(nextEdge))
                {
                    PruneRiverIfTooShort();
                    return true;
                }
                edges.Add(nextEdge);
                prev = next;

                if (prev.distance < depth && prev.distance >= MIN_BRANCH_DISTANCE)
                {
                    branchQueue.Enqueue(nextEdge);
                }
            }
            return false;
        }

        internal bool BuildBranch(Context context)
        {
            if (branchQueue.Count == 0)
            {
                PruneRiverIfTooShort();
                return true;
            }

            var prevEdge = branchQueue.Dequeue();
            Vertex prev = prevEdge.Drain;
            int prevDist = prev.distance + random.NextInt(3);
            var branchNext = ComputeNext(prev, prev.length, prevDist);

            double deltaAngle = Math.Abs(prevEdge.Source.angle - branchNext.angle);
            if (deltaAngle < MIN_BRANCH_ANGLE || (2 * Math.PI - deltaAngle < MIN_BRANCH_ANGLE))
            {
                return false;
            }

            branch.Clear();
            var first = new Edge(branchNext, prev);
            if (context.IntersectAny(first))
            {
                return false;
            }
            branch.Add(first);

            prev = branchNext;
            for (int i = 0; i < depth - prev.distance + random.NextInt(1 + (int)(depth * 0.3f)); i++)
            {
                var next = ComputeNext(prev, prev.length, prevDist);
                var nextEdge = new Edge(next, prev);
                if (context.IntersectAny(nextEdge)) break;

                branch.Add(nextEdge);
                prev = next;
                prevDist = next.distance;

                if (prevDist < depth && prevDist >= MIN_BRANCH_DISTANCE)
                {
                    branchQueue.Enqueue(nextEdge);
                }
            }

            edges.AddRange(branch);
            return false;
        }

        private void PruneRiverIfTooShort()
        {
            if (edges.Count < MIN_RIVER_EDGE_COUNT)
            {
                edges.Clear();
                branchQueue.Clear();
            }
        }

        public bool IntersectAny(Edge edge)
        {
            foreach (var e in edges)
            {
                if (e.Source != edge.Drain && e.Drain != edge.Drain && (Distance(e, edge.Source) < featherSq || Intersect(e.Source, e.Drain, edge.Source, edge.Drain)))
                {
                    return true;
                }
            }
            return false;
        }

        private Vertex ComputeNext(Vertex prev, double length, int distance)
        {
            double nextAngle = distance == 0
                ? prev.angle
                : prev.angle + (random.NextDouble() * 0.5f + 0.2f) * (random.NextBoolean() ? 1 : -1);
            double nextLength = length * (random.NextDouble() * 0.08f + 0.92f);

            double dx = Mth.Cos((float)nextAngle) * nextLength;
            double dy = Mth.Sin((float)nextAngle) * nextLength;
            double x = prev.x + dx;
            double y = prev.y + dy;

            return new Vertex(x, y, nextAngle, nextLength, distance + 1);
        }
    }

    public class MultiParallelBuilder : Context
    {
        private readonly List<Builder> builders = new();

        public int BuilderCount => builders.Count;

        public Context Add(Builder builder)
        {
            builders.Add(builder);
            return this;
        }

        public List<TResult> Build<TResult>(Func<Edge, TResult> map)
        {
            // 二叉最小堆：精确匹配 Java PriorityQueue 语义（同优先级不 swap → 先入先出的堆内部排序）
            var working = new JavaStyleMinHeap();
            foreach (var builder in builders)
            {
                if (builder.BuildInitialBranch(this))
                {
                    working.Enqueue(builder);
                }
            }

            if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
            {
                Console.Error.WriteLine($"[RIVER-DBG] Build: total builders added to working queue = {working.Count}");
                var snapshot = working.Snapshot();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    Console.Error.WriteLine($"[RIVER-DBG]   working[{i}]: edges={snapshot[i].Edges.Count} branchQueue={snapshot[i].BranchQueue.Count} priority={Priority(snapshot[i])}");
                }
            }

            while (working.Count > 0)
            {
                int _dbgIdx = working.Count;
                var builder = working.Dequeue();
                if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
                {
                    Console.Error.WriteLine($"[RIVER-DBG] Dequeue: index={_dbgIdx} priority={Priority(builder)} edges={builder.Edges.Count} branchQueue={builder.BranchQueue.Count}");
                }
                if (!builder.BuildBranch(this))
                {
                    working.Enqueue(builder);
                }
            }

            if (Environment.GetEnvironmentVariable("TFC_DEBUG_RIVER") != null)
            {
                int withEdges = 0;
                foreach (var b in builders)
                {
                    if (b.Edges.Count > 0) withEdges++;
                }
                Console.Error.WriteLine($"[RIVER-DBG] Build complete: builders with edges = {withEdges} / {builders.Count}");
            }

            var result = new List<TResult>();
            foreach (var builder in builders)
            {
                foreach (var e in builder.Edges)
                    result.Add(map(e));
            }
            return result;
        }

        private static int Priority(Builder b) => -(b.Edges.Count) - 10 * b.BranchQueue.Count;

        /// <summary>
        /// 二叉最小堆，匹配 Java PriorityQueue 的 sift 行为：
        /// siftUp: 当 x &lt;= parent 时停止（同优先级不交换）→ 先插入的同优先级元素更靠近根
        /// siftDown: 当 x &lt;= child 时停止（同优先级不交换）
        /// 这与 C# PriorityQueue 的 FIFO 同优先级语义不同。
        /// </summary>
        private sealed class JavaStyleMinHeap
        {
            private readonly List<Builder> _heap = new();

            public int Count => _heap.Count;

            public List<Builder> Snapshot() => new(_heap);

            public void Enqueue(Builder builder)
            {
                _heap.Add(builder);
                SiftUp(_heap.Count - 1);
            }

            public Builder Dequeue()
            {
                var result = _heap[0];
                int last = _heap.Count - 1;
                _heap[0] = _heap[last];
                _heap.RemoveAt(last);
                if (_heap.Count > 0)
                    SiftDown(0);
                return result;
            }

            private void SiftUp(int index)
            {
                while (index > 0)
                {
                    int parent = (index - 1) >> 1;
                    if (Priority(_heap[index]) < Priority(_heap[parent]))
                    {
                        (_heap[index], _heap[parent]) = (_heap[parent], _heap[index]);
                        index = parent;
                    }
                    else
                        break; // 同优先级或更大：不交换，匹配 Java siftUpUsingComparator
                }
            }

            private void SiftDown(int index)
            {
                int half = _heap.Count >> 1;
                while (index < half)
                {
                    int child = (index << 1) + 1;
                    int right = child + 1;
                    if (right < _heap.Count && Priority(_heap[right]) < Priority(_heap[child]))
                        child = right;
                    if (Priority(_heap[index]) <= Priority(_heap[child]))
                        break; // 同优先级或更小：不交换，匹配 Java siftDownUsingComparator
                    (_heap[index], _heap[child]) = (_heap[child], _heap[index]);
                    index = child;
                }
            }
        }

        public virtual bool IntersectAny(Edge edge)
        {
            if (!IsLegal(edge.Drain, edge.Source))
            {
                return true;
            }
            foreach (var builder in builders)
            {
                if (builder.IntersectAny(edge))
                {
                    return true;
                }
            }
            return false;
        }

        protected virtual bool IsLegal(Vertex prev, Vertex vertex) => true;
    }
}