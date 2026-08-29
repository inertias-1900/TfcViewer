using TfcViewer.Core;
using TfcViewer.Noise;
using TfcViewer.World;
using TfcViewer.World.Region;

namespace TfcViewer;

public static class DivTestMain
{
    public static void Run()
    {
        var settings = Defaults.DefaultSettings();
        var s = Seed.Of(-1162099057L);
        var rg = new RegionGenerator(settings, s);
        foreach (var p in new[] { new[] { -32, -32 }, new[] { -9, -32 }, new[] { -10, -6 } })
        {
            int x = p[0], z = p[1];
            var cell = rg.CellNoise.GetCell(x, z);
            double vx = Helpers.HashDouble(cell.noise, 95274);
            double vy = Helpers.HashDouble(cell.noise, 894824);
            var ac = rg.CellNoise.GetCell(cell.nx, cell.ny);
            double avx = Helpers.HashDouble(ac.noise, 95274);
            double avy = Helpers.HashDouble(ac.noise, 894824);
            double convX0 = x < cell.nx ? avx - vx : -(avx - vx);
            double convY0 = z < cell.ny ? avy - vy : -(avy - vy);
            Console.WriteLine($"({x},{z}) cell=({cell.x:F6},{cell.y:F6}) nx,ny=({cell.nx:F2},{cell.ny:F2}) noiseBits={BitConverter.DoubleToInt64Bits(cell.noise):X} vx={vx:F10} vy={vy:F10} avx={avx:F10} avy={avy:F10} conv=({convX0:F8},{convY0:F8}) sum={convX0 + convY0:F8}");
        }
    }
}