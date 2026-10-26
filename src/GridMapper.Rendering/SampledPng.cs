using System.Buffers.Binary;
using System.IO.Compression;
using SkiaSharp;

namespace GridMapper.Rendering;

// Bounded row decoder for reference-image previews, including Adam7 and 1/2/4/8/16
// bit PNG. Original bytes are kept in the project; this preview never enters export.
// PNG framing/CRC, zlib integrity and every reconstructed row are validated.
internal static class SampledPng
{
    private static readonly uint[] CrcTable = BuildCrc();
    private static uint[] BuildCrc()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++) { var c = i; for (var bit = 0; bit < 8; bit++) c = (c & 1) == 0 ? c >> 1 : 0xedb88320 ^ (c >> 1); table[i] = c; }
        return table;
    }
    private static uint Crc(ReadOnlySpan<byte> bytes)
    { var crc = uint.MaxValue; foreach (var b in bytes) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8); return ~crc; }
    private static IOException Invalid() => new("mask.invalid");

    public static SKBitmap Decode(byte[] source, int maximum, SKColorSpace space, CancellationToken token)
    {
        if (source.Length < 33 || !source.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw Invalid();
        var width = 0; var height = 0; var depth = 0; var color = 0; var interlace = 0;
        byte[] palette = [], alpha = []; var header = false; var ended = false; var hadData = false;
        using var compressed = new MemoryStream();
        for (var offset = 8; offset < source.Length;)
        {
            token.ThrowIfCancellationRequested();
            if (source.Length - offset < 12) throw Invalid();
            var length = BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset, 4));
            if (length > source.Length - offset - 12) throw Invalid();
            var count = (int)length; var name = source.AsSpan(offset + 4, 4); var chunk = source.AsSpan(offset + 8, count);
            if (Crc(source.AsSpan(offset + 4, count + 4)) != BinaryPrimitives.ReadUInt32BigEndian(source.AsSpan(offset + count + 8, 4))) throw Invalid();
            if (!header && !name.SequenceEqual("IHDR"u8)) throw Invalid();
            if (name.SequenceEqual("IHDR"u8))
            {
                if (header || count != 13) throw Invalid(); header = true;
                width = BinaryPrimitives.ReadInt32BigEndian(chunk[..4]); height = BinaryPrimitives.ReadInt32BigEndian(chunk.Slice(4, 4));
                depth = chunk[8]; color = chunk[9]; interlace = chunk[12];
                if (width is < 1 or > CanvasMaskImages.MaxDimension || height is < 1 or > CanvasMaskImages.MaxDimension
                    || chunk[10] != 0 || chunk[11] != 0 || interlace > 1) throw Invalid();
                if (!(color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => false })) throw Invalid();
            }
            else if (name.SequenceEqual("acTL"u8) || name.SequenceEqual("fcTL"u8) || name.SequenceEqual("fdAT"u8)) throw new IOException("mask.format");
            else if (name.SequenceEqual("PLTE"u8)) { if (hadData || count is < 3 or > 768 || count % 3 != 0) throw Invalid(); palette = chunk.ToArray(); }
            else if (name.SequenceEqual("tRNS"u8)) { if (hadData || count > 256) throw Invalid(); alpha = chunk.ToArray(); }
            else if (name.SequenceEqual("IDAT"u8)) { hadData = true; compressed.Write(chunk); }
            else if (name.SequenceEqual("IEND"u8)) { if (count != 0 || !hadData || offset + 12 != source.Length) throw Invalid(); ended = true; break; }
            else if ((name[0] & 32) == 0) throw Invalid(); // Unknown critical PNG chunk.
            offset += count + 12;
        }
        if (!header || !ended || color == 3 && (palette.Length == 0 || alpha.Length > palette.Length / 3)
            || color == 0 && alpha.Length is not (0 or 2) || color == 2 && alpha.Length is not (0 or 6)
            || color is 4 or 6 && alpha.Length != 0) throw Invalid();
        var factor = Math.Min(1d, (double)maximum / Math.Max(width, height));
        var outWidth = Math.Max(1, (int)Math.Round(width * factor)); var outHeight = Math.Max(1, (int)Math.Round(height * factor));
        var result = new SKBitmap(new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul, space));
        try
        {
            result.Erase(SKColors.Transparent);
            var mapX = Enumerable.Repeat(-1, width).ToArray(); var mapY = Enumerable.Repeat(-1, height).ToArray();
            for (var x = 0; x < outWidth; x++) mapX[Math.Min(width - 1, (int)((x + .5) * width / outWidth))] = x;
            for (var y = 0; y < outHeight; y++) mapY[Math.Min(height - 1, (int)((y + .5) * height / outHeight))] = y;
            var channels = color switch { 0 or 3 => 1, 2 => 3, 4 => 2, _ => 4 }; var bpp = Math.Max(1, (channels * depth + 7) / 8);
            compressed.Position = 0;
            using var zlib = new ZLibStream(compressed, CompressionMode.Decompress, leaveOpen: true);
            var passes = interlace == 0 ? new[] { (0, 0, 1, 1) } : new[] { (0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2) };
            foreach (var (startX, startY, stepX, stepY) in passes)
            {
                var pw = Math.Max(0, (width - startX + stepX - 1) / stepX); var ph = Math.Max(0, (height - startY + stepY - 1) / stepY);
                if (pw == 0 || ph == 0) continue;
                var rowBytes = (pw * channels * depth + 7) / 8;
                var row = new byte[rowBytes]; var previous = new byte[rowBytes];
                for (var y = 0; y < ph; y++)
                {
                    token.ThrowIfCancellationRequested(); var filter = zlib.ReadByte(); if (filter is < 0 or > 4) throw Invalid();
                    zlib.ReadExactly(row);
                    for (var i = 0; i < rowBytes; i++)
                    {
                        var left = i >= bpp ? row[i - bpp] : 0; var up = previous[i]; var diagonal = i >= bpp ? previous[i - bpp] : 0;
                        var predict = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, _ => Paeth(left, up, diagonal) };
                        row[i] = unchecked((byte)(row[i] + predict));
                    }
                    var destY = mapY[startY + y * stepY];
                    if (destY >= 0) for (var x = 0; x < pw; x++)
                    {
                        var destX = mapX[startX + x * stepX]; if (destX < 0) continue;
                        int Sample(int channel)
                        {
                            var index = x * channels + channel;
                            return depth switch { 16 => BinaryPrimitives.ReadUInt16BigEndian(row.AsSpan(index * 2, 2)), 8 => row[index],
                                _ => (row[index * depth / 8] >> (8 - depth - index * depth % 8)) & ((1 << depth) - 1) };
                        }
                        byte Byte(int sample) => depth == 16 ? (byte)(sample >> 8) : depth == 8 ? (byte)sample : (byte)(sample * 255 / ((1 << depth) - 1));
                        var r = Sample(0); int g = r, b = r; var a = 255;
                        if (color == 3)
                        {
                            if (r >= palette.Length / 3) throw Invalid();
                            a = r < alpha.Length ? alpha[r] : 255; b = palette[r * 3 + 2]; g = palette[r * 3 + 1]; r = palette[r * 3];
                            result.SetPixel(destX, destY, new((byte)r, (byte)g, (byte)b, (byte)a)); continue;
                        }
                        if (color is 2 or 6) { g = Sample(1); b = Sample(2); }
                        if (color is 4 or 6) a = Byte(Sample(channels - 1));
                        else if (color == 0 && alpha.Length == 2 && r == BinaryPrimitives.ReadUInt16BigEndian(alpha)) a = 0;
                        else if (color == 2 && alpha.Length == 6 && r == BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(0, 2))
                            && g == BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(2, 2)) && b == BinaryPrimitives.ReadUInt16BigEndian(alpha.AsSpan(4, 2))) a = 0;
                        result.SetPixel(destX, destY, new(Byte(r), Byte(g), Byte(b), (byte)a));
                    }
                    (row, previous) = (previous, row);
                }
            }
            if (zlib.ReadByte() != -1) throw Invalid();
            return result;
        }
        catch { result.Dispose(); throw; }
    }
    private static int Paeth(int a, int b, int c)
    { var p = a + b - c; var pa = Math.Abs(p - a); var pb = Math.Abs(p - b); var pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
}
