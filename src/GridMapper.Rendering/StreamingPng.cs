using System.Buffers.Binary;
using System.IO.Compression;
using GridMapper.Core;
using SkiaSharp;

namespace GridMapper.Rendering;

public static partial class SceneRenderer
{
    // A 16384-square Canvas needs over 1 GiB as one RGBA bitmap. Render and encode
    // 64 rows at a time instead. Complete source-screen rasters retain crop fidelity.
    public static void WritePng(Stream output, ScreenScene scene, IReadOnlyDictionary<string, LogoImage>? logos = null,
        CancellationToken cancellationToken = default, IProgress<double>? progress = null, TimeSpan? timeLimit = null)
    {
        ModelLimits.ValidateCanvas(scene.Width, scene.Height);
        var prepared = scene.Layers.Sum(layer => (long)(layer.UnclippedSource ?? layer).Bounds.Width * (layer.UnclippedSource ?? layer).Bounds.Height * 4);
        RenderSafety.CheckPreparedBytes(prepared);
        var work = new RenderWork(cancellationToken, timeLimit ?? TimeSpan.FromMinutes(2)); work.Check();
        using var recorder = new SKPictureRecorder();
        DrawScene(recorder.BeginRecording(new SKRect(0, 0, scene.Width, scene.Height)), scene, logos, false, work);
        using var picture = recorder.EndRecording(); work.Check();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13]; header.Clear();
        BinaryPrimitives.WriteInt32BigEndian(header, scene.Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], scene.Height);
        header[8] = 8; header[9] = 6;
        PngChunks.Chunk(output, "IHDR"u8, header); PngChunks.Chunk(output, "sRGB"u8, [0]);
        using var chunks = new PngChunks(output);
        using var colorSpace = SKColorSpace.CreateSrgb();
        using var band = new SKBitmap(new SKImageInfo(scene.Width, Math.Min(64, scene.Height), SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        if (band.GetPixels() == 0) throw new RenderLimitException("Не удалось выделить память для полосы PNG. Экспорт отменён.");
        var row = new byte[1 + scene.Width * 4];
        using (var encoded = new ZLibStream(chunks, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var top = 0; top < scene.Height; top += band.Height)
            {
                work.Check();
                using (var canvas = new SKCanvas(band))
                { canvas.Clear(SKColors.Transparent); canvas.Translate(0, -top); canvas.DrawPicture(picture); canvas.Flush(); }
                for (var y = 0; y < Math.Min(band.Height, scene.Height - top); y++)
                {
                    work.Check();
                    band.GetPixelSpan().Slice(y * band.RowBytes, scene.Width * 4).CopyTo(row.AsSpan(1));
                    for (var i = 1; i < row.Length; i += 4)
                    {
                        var alpha = row[i + 3];
                        if (alpha == 255) continue;
                        for (var c = 0; c < 3; c++) row[i + c] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, (row[i + c] * 255 + alpha / 2) / alpha);
                    }
                    encoded.Write(row);
                }
                progress?.Report((double)Math.Min(scene.Height, top + band.Height) / scene.Height);
            }
        }
        chunks.Flush(); work.Check(); PngChunks.Chunk(output, "IEND"u8, []);
    }

    private sealed class PngChunks(Stream destination) : Stream
    {
        private readonly byte[] _buffer = new byte[32768];
        private int _count;
        private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
        {
            var crc = (uint)n;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
            return crc;
        }).ToArray();
        public static void Chunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> number = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
            stream.Write(number); stream.Write(type); stream.Write(data);
            var crc = uint.MaxValue;
            foreach (var b in type) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
            foreach (var b in data) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
            BinaryPrimitives.WriteUInt32BigEndian(number, ~crc); stream.Write(number);
        }
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            while (!buffer.IsEmpty)
            {
                var count = Math.Min(_buffer.Length - _count, buffer.Length);
                buffer[..count].CopyTo(_buffer.AsSpan(_count)); _count += count; buffer = buffer[count..];
                if (_count == _buffer.Length) Flush();
            }
        }
        public override void Flush() { if (_count > 0) { Chunk(destination, "IDAT"u8, _buffer.AsSpan(0, _count)); _count = 0; } }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
