namespace TfcViewerGui;

/// <summary>群系中文名（读取嵌入资源 biome_zh.json：tfc:xxx → 中文）</summary>
public static class BiomeZh
{
    public static Dictionary<string, string> Load()
    {
        var map = new Dictionary<string, string>();
        using var stream = typeof(BiomeZh).Assembly.GetManifestResourceStream("TfcViewer.biome_zh.json");
        if (stream == null) return map;
        using var reader = new StreamReader(stream);
        string txt = reader.ReadToEnd();
        var m = System.Text.RegularExpressions.Regex.Match(txt, "\"([\\w:]+)\":\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");
        while (m.Success)
        {
            map[m.Groups[1].Value] = m.Groups[2].Value.Replace("\\u", "\\u");
            m = m.NextMatch();
        }
        return map;
    }

    /// <summary>显示名：中文(tfc:xxx)；无翻译则退回原始 id</summary>
    public static string DisplayName(string biomeId, Dictionary<string, string> zh)
        => zh.TryGetValue(biomeId, out var cn) ? $"{cn}({biomeId})" : biomeId;
}