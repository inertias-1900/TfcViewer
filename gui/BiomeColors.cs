namespace TfcViewerGui;

/// <summary>群系 ID → 显示颜色（读取嵌入资源 biome_colors.json，同 CLI 约定）</summary>
public static class BiomeColors
{
    public static Dictionary<string, int[]> Load()
    {
        var map = new Dictionary<string, int[]>();
        using var stream = typeof(BiomeColors).Assembly.GetManifestResourceStream("TfcViewer.biome_colors.json");
        if (stream == null) return map;
        using var reader = new StreamReader(stream);
        string txt = reader.ReadToEnd();
        var m = System.Text.RegularExpressions.Regex.Match(txt, "\"([\\w:]+)\":\\s*\\[\\s*(\\d+)\\s*,\\s*(\\d+)\\s*,\\s*(\\d+)\\s*]");
        while (m.Success)
        {
            map[m.Groups[1].Value] = new[] { int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value) };
            m = m.NextMatch();
        }
        return map;
    }
}