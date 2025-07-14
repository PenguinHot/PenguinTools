using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using PenguinTools.Common;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Image = SixLabors.ImageSharp.Image;

namespace PenguinMedia.Graphic;

public static class ImageUtils
{
    private static void ResizeIfNeeded(this Image<Rgba32> img, int targetWidth, int targetHeight)
    {
        if (img.Width == targetWidth && img.Height == targetHeight) return;
        img.Mutate(x => x.Resize(targetWidth, targetHeight, KnownResamplers.Lanczos3));
    }

    private static void EncodeDdsToStream(this Image<Rgba32> img, Stream stream, CompressionFormat format)
    {
        var encoder = new BcEncoder
        {
            OutputOptions =
            {
                GenerateMipMaps = false,
                Quality = CompressionQuality.BestQuality,
                Format = format,
                FileFormat = OutputFileFormat.Dds
            }
        };

        encoder.EncodeToStream(img, stream);
    }

    private static byte[] ConvertBackground(string srcPath)
    {
        using var bgImg = Image.Load<Rgba32>(srcPath);
        bgImg.ResizeIfNeeded(1920, 1080);
        using var ms = new MemoryStream(1036928);
        bgImg.EncodeDdsToStream(ms, CompressionFormat.Bc1);
        return ms.ToArray();
    }

    private static byte[] ConvertEffect(string[] srcPaths)
    {
        const int tileSize = 256;
        const int canvasSize = tileSize * 2;
        using var canvas = new Image<Rgba32>(canvasSize, canvasSize);

        for (var i = 0; i < 4; i++)
        {
            var path = i < srcPaths.Length ? srcPaths[i] : null;
            if (string.IsNullOrWhiteSpace(path)) continue;

            var x = i % 2 * tileSize;
            var y = i / 2 * tileSize;

            canvas.Mutate(p =>
            {
                using var img = Image.Load<Rgba32>(path);
                img.ResizeIfNeeded(tileSize, tileSize);
                p.DrawImage(img, new Point(x, y), 1f);
            });
        }

        using var ms = new MemoryStream(262272);
        canvas.EncodeDdsToStream(ms, CompressionFormat.Bc3);
        return ms.ToArray();
    }
    
    public static string ProbeImage(string srcPath)
    {
        var fmt = Image.DetectFormat(srcPath);
        return $"{fmt.Name} ({fmt.DefaultMimeType})";
    }
    
    public static void ConvertJacket(string srcPath, string dstPath)
    {
        using var img = Image.Load<Rgba32>(srcPath);
        img.ResizeIfNeeded(300, 300);
        using var fs = new FileStream(dstPath, FileMode.Create, FileAccess.Write);
        img.EncodeDdsToStream(fs, CompressionFormat.Bc1);
    }

    public static void ExtractAfb(string srcPath, string dstPath)
    {
        var data = File.ReadAllBytes(srcPath);
        var chunks = ChunkUtils.LocateDdsChunks(data);
        if (chunks.IsEmpty || chunks.Length == 0) throw new MediaException(Strings.Media_Error_no_dds_chunks);
        var baseName = Path.GetFileNameWithoutExtension(srcPath);
        if (string.IsNullOrEmpty(baseName)) baseName = "chunk";
        ChunkUtils.ExtractChunks(data, dstPath, baseName, ".dds", chunks);
    }
    
    public static void ConvertStage(string bgSrcPath, string[] fxSrcPaths, string stDstPath, string nfDstPath)
    {
        var bgDds = ConvertBackground(bgSrcPath);
        byte[]? fxDds = null;
        if (fxSrcPaths.Any(p => !string.IsNullOrEmpty(p))) fxDds = ConvertEffect(fxSrcPaths);
        byte[]?[] replacements = [bgDds, fxDds ?? ResourceUtils.GetByte("fx_dummy.dds")];
        ChunkUtils.ReplaceChunks(ResourceUtils.GetByte("st_dummy.afb"), stDstPath, [(9824, 1046752), (1046752, 1309024)], replacements);
        File.WriteAllBytes(nfDstPath, ResourceUtils.GetByte("nf_dummy.afb"));
    }
}