using TfcViewer.Core;
using TfcViewer.Noise;
using TfcViewer.World;
using TfcViewer.World.Layer;
using TfcViewer.World.Region;

namespace TfcViewer;

public static class LayerTest3Main
{
    public static void Run()
    {
        var settings = Defaults.DefaultSettings();
        var s = Seed.Of(-1162099057L);
        var rg = new RegionGenerator(settings, s);
        var regionLayer = new RegionLayer(rg).Apply(s.Next());
        var main0 = RegionBiomeLayer.Apply(regionLayer);
        var main1 = RegionEdgeBiomeLayer.Apply(s.Next(), main0);
        var main2 = ZoomLayer.ApplyNormal(s.Next(), main1);
        var main3 = ShoreAndRiverLayer.Apply(s.Next(), main2);
        var main4 = MoreShoresLayer.Apply(s.Next(), main3);
        var main5 = IceSheetEdgeLayer.Apply(s.Next(), main4);
        var main6 = ZoomLayer.ApplyNormal(s.Next(), main5);
        var main7 = ZoomLayer.ApplyNormal(s.Next(), main6);
        var main8 = ZoomLayer.ApplyNormal(s.Next(), main7);
        var main9 = ZoomLayer.ApplyNormal(s.Next(), main8);
        var main10 = SmoothLayer.Apply(s.Next(), main9);

        int[][] pts = { new[] { -1024, -1024 }, new[] { -352, -896 }, new[] { -320, -256 }, new[] { -160, -192 } };
                foreach (var p in pts)
                {
                    Console.WriteLine($"({p[0]},{p[1]}) rbl={main0().Get(p[0], p[1])} edge={main1().Get(p[0], p[1])} zoom1={main2().Get(p[0], p[1])} shore={main3().Get(p[0], p[1])} more={main4().Get(p[0], p[1])} ice={main5().Get(p[0], p[1])} z2={main6().Get(p[0], p[1])} z3={main7().Get(p[0], p[1])} z4={main8().Get(p[0], p[1])} z5={main9().Get(p[0], p[1])} smooth={main10().Get(p[0], p[1])}");
                }
    }
}