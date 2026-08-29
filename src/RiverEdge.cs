
using TfcViewer.Core;
using TfcViewer.World.River;

using TfcViewer.Noise;
namespace TfcViewer.World.Region;

/// <summary>RiverEdge（等价 TFC 原版）</summary>
public sealed class RiverEdge
{
    public const int MIN_WIDTH = 8;
    public const int MAX_WIDTH = 24;
    public const int MIN_VALLEY_WIDTH = 20;

    private static readonly int MAX_AFFECTING_GRID_DISTANCE = 1 + Mth.Ceil(1.5f * AddRiversAndLakes.RIVER_LENGTH);

    public int Width;

    private readonly RiverGen.Vertex source, drain;
    private readonly MidpointFractal fractal;

    public readonly int MinPartX, MinPartZ, MaxPartX, MaxPartZ;

    private bool sourceEdge;
    private RiverEdge? drainEdge;

    public RiverEdge(RiverGen.Edge edge, RandomSource random)
    {
        this.source = edge.Source;
        this.drain = edge.Drain;
        this.fractal = edge.Fractal(random, 4);

        int centerGridX = (int)Math.Floor(0.5f * (edge.Source.x + edge.Drain.x) + 0.5);
        int centerGridZ = (int)Math.Floor(0.5f * (edge.Source.y + edge.Drain.y) + 0.5);

        this.MinPartX = Units.GridToPart(centerGridX - MAX_AFFECTING_GRID_DISTANCE);
        this.MinPartZ = Units.GridToPart(centerGridZ - MAX_AFFECTING_GRID_DISTANCE);
        this.MaxPartX = Units.GridToPart(centerGridX + MAX_AFFECTING_GRID_DISTANCE);
        this.MaxPartZ = Units.GridToPart(centerGridZ + MAX_AFFECTING_GRID_DISTANCE);

        this.sourceEdge = false;
        this.drainEdge = null;
    }

    public RiverGen.Vertex Source => source;
    public bool SourceEdge => sourceEdge;
    public RiverGen.Vertex Drain => drain;
    public RiverEdge? DrainEdge => drainEdge;
    public MidpointFractal Fractal => fractal;
    public int WidthSq => Width * Width;

    public double WidthSqAt(double exactGridX, double exactGridZ)
    {
        double lerpFac = RiverHelpers.ProjectAlongLine(
            source.x, source.y,
            drain.y, drain.y,
            exactGridX, exactGridZ);

        double realWidth = Helpers.Lerp(lerpFac, Width, drainEdge == null ? Width : drainEdge.Width);
        return realWidth * realWidth;
    }

    public void LinkToDrain(RiverEdge? edge)
    {
        this.drainEdge = edge;
        if (edge != null)
        {
            edge.sourceEdge = true;
        }
    }
}