using System.IO.Compression;

namespace TfcViewer;

/// <summary>零依赖 PNG 编码器（8bit RGB 非隔行）</summary>
public static class PngWriter
{
    public static void Write(string path, int width, int height, Func<int, int, int> getPixel)
    {
        using var fs = File.Create(path);
        var bw = new BinaryWriter(fs);

        // 签名
        bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        // IHDR
        using (var ihdr = new MemoryStream())
        {
            var w = new BinaryWriter(ihdr);
            w.Write(IntBE(width));
            w.Write(IntBE(height));
            w.Write((byte)8);   // bit depth
            w.Write((byte)2);   // color type RGB
            w.Write((byte)0);   // compression
            w.Write((byte)0);   // filter
            w.Write((byte)0);   // interlace
            WriteChunk(bw, "IHDR", ihdr.ToArray());
        }

        // IDAT：每行 filter=0 + RGB
        var raw = new byte[height * (1 + width * 3)];
        int stride = 1 + width * 3;
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * stride;
            raw[rowStart] = 0;
            for (int x = 0; x < width; x++)
            {
                int c = getPixel(x, y);
                int o = rowStart + 1 + x * 3;
                raw[o] = (byte)(c >> 16);
                raw[o + 1] = (byte)(c >> 8);
                raw[o + 2] = (byte)c;
            }
        }

        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.Fastest, leaveOpen: true))
            {
                z.Write(raw);
            }
            WriteChunk(bw, "IDAT", ms.ToArray());
        }

        WriteChunk(bw, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        bw.Write(IntBE(data.Length));
        bw.Write(System.Text.Encoding.ASCII.GetBytes(type));
        bw.Write(data);
        uint crc = Crc32(type, data);
        bw.Write((uint)crc);
    }

    private static uint[] crcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            }
            table[n] = c;
        }
        return table;
    }

    private static uint Crc32(string type, byte[] data)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte b in System.Text.Encoding.ASCII.GetBytes(type))
            c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        foreach (byte b in data)
            c = crcTable[(c ^ b) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }

    private static byte[] IntBE(int v) => new byte[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v };
}