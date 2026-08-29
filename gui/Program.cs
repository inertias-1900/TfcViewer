using System.Globalization;

namespace TfcViewerGui;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        long seed = -1162099057L;
        if (args.Length > 0)
        {
            seed = ParseSeed(args[0]);
        }
        Application.Run(new MainForm(seed));
    }

    /// <summary>与游戏 WorldOptions.parseSeed 一致：数字直取，字符串取 Java hashCode</summary>
    internal static long ParseSeed(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return v;
        int h = 0;
        foreach (char c in text) h = unchecked(31 * h + c);
        return h;
    }
}