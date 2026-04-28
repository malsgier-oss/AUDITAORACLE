using ImageMagick;

/// <summary>
/// Builds a multi-resolution .ico from a PNG using ImageMagick (Lanczos-style filter, PNG layers in ICO).
/// Keeps the source bitmap data sharp at each size — no System.Drawing single-HICON downscale.
/// Usage: IconGen.exe &lt;input.png&gt; &lt;output.ico&gt;
/// </summary>
internal static class Program
{
    private static readonly uint[] Sizes = [16, 24, 32, 48, 64, 128, 256];

    public static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: IconGen <input.png> <output.ico>");
            return 1;
        }

        var inputPath = Path.GetFullPath(args[0]);
        var outputPath = Path.GetFullPath(args[1]);
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input not found: {inputPath}");
            return 2;
        }

        try
        {
            using var collection = new MagickImageCollection();
            foreach (var size in Sizes)
            {
                var layer = new MagickImage(inputPath);
                layer.Alpha(AlphaOption.Set);
                var g = new MagickGeometry(size, size) { IgnoreAspectRatio = true };
                layer.FilterType = FilterType.Lanczos;
                layer.Resize(g);
                layer.Depth = 8;
                collection.Add(layer);
            }

            collection.Write(outputPath, MagickFormat.Ico);
            Console.WriteLine($"Wrote {outputPath} ({Sizes.Length} sizes, lossless PNG-in-ICO layers).");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 4;
        }
    }
}
