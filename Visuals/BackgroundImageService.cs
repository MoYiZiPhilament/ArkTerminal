using System.IO;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArkTerminal.Core.Configuration;
using ArkTerminal.Core.Data;

namespace ArkTerminal.Visuals;

public sealed record BackgroundImportResult(bool Success, string ResourcePath, string? Error);
public sealed record BackgroundLoadResult(BitmapSource? Image, string? Error);

public sealed class BackgroundImageService(IAppDataPaths paths)
{
    private const int MaxProcessingPixels = 4_000_000;
    private static readonly Guid PngFormat = new("1B7CFAF4-713F-473C-BBCD-6137425FAEAF");
    private static readonly Guid JpegFormat = new("19E4A5AA-5662-4FC5-A0C0-1758028E1057");
    private static readonly Guid BmpFormat = new("0AF1D87E-FCFE-4188-BDEB-A7906471CBE3");

    public async Task<BackgroundImportResult> ImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        try
        {
            var extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            var expectedFormat = extension switch
            {
                ".png" => PngFormat,
                ".jpg" or ".jpeg" => JpegFormat,
                ".bmp" => BmpFormat,
                _ => Guid.Empty
            };
            if (expectedFormat == Guid.Empty)
            {
                return Failure("仅支持 PNG、JPEG、BMP 背景图。");
            }
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Length <= 0 || info.Length > VisualSettingsLimits.MaxBackgroundFileBytes)
            {
                return Failure($"图片不存在、为空或超过 {VisualSettingsLimits.MaxBackgroundFileBytes / 1024 / 1024} MiB 上限。");
            }
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string hash;
                using (var stream = OpenRead(sourcePath))
                {
                    var decoder = Decode(stream);
                    var frame = decoder.Frames[0];
                    ValidateFrame(frame, expectedFormat);
                    stream.Position = 0;
                    hash = Convert.ToHexString(SHA256.HashData(stream))[..20].ToLowerInvariant();
                }
                var fileName = $"background-{hash}{extension}";
                var destination = Path.Combine(paths.BackgroundsDirectory, fileName);
                if (!File.Exists(destination))
                {
                    var temporary = Path.Combine(paths.BackgroundsDirectory, $".{Guid.NewGuid():N}.tmp");
                    try
                    {
                        File.Copy(sourcePath, temporary, false);
                        File.Move(temporary, destination);
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                }
                return new BackgroundImportResult(true, $"Backgrounds/{fileName}", null);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsImageOrIoError(exception))
        {
            return Failure($"背景图导入失败：{exception.Message}");
        }
    }

    public async Task<BackgroundLoadResult> LoadProcessedAsync(
        string resourcePath,
        double brightness,
        double saturation,
        double blurRadius,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = ResolvePath(resourcePath);
            if (fullPath is null)
            {
                return new(null, "背景路径不在应用 Backgrounds 目录内。");
            }
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(fullPath);
                if (!info.Exists || info.Length <= 0 || info.Length > VisualSettingsLimits.MaxBackgroundFileBytes)
                {
                    return new BackgroundLoadResult(null, "背景副本不存在、为空或超过大小上限。");
                }
                using var stream = OpenRead(fullPath);
                var decoder = Decode(stream);
                var frame = decoder.Frames[0];
                ValidateFrame(frame, null);
                var scale = Math.Min(1, Math.Sqrt((double)MaxProcessingPixels / ((long)frame.PixelWidth * frame.PixelHeight)));
                BitmapSource source = frame;
                if (scale < 1)
                {
                    source = new TransformedBitmap(frame, new ScaleTransform(scale, scale));
                }
                var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
                converted.Freeze();
                var width = Math.Max(1, converted.PixelWidth);
                var height = Math.Max(1, converted.PixelHeight);
                var stride = width * 4;
                var pixels = new byte[stride * height];
                converted.CopyPixels(pixels, stride, 0);
                AdjustColors(pixels, brightness, saturation);
                var radius = (int)Math.Ceiling(Math.Clamp(blurRadius * width / 1920d, 0, 48));
                if (radius > 0)
                {
                    pixels = BoxBlur(pixels, width, height, radius);
                }
                var image = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                image.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), pixels, stride, 0);
                image.Freeze();
                return new BackgroundLoadResult(image, null);
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (IsImageOrIoError(exception))
        {
            return new(null, $"背景图加载失败：{exception.Message}");
        }
    }

    public string? ResolvePath(string resourcePath)
    {
        if (string.IsNullOrEmpty(resourcePath))
        {
            return null;
        }
        try
        {
            var root = Path.GetFullPath(paths.BackgroundsDirectory) + Path.DirectorySeparatorChar;
            var relative = resourcePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            relative = relative.StartsWith("Backgrounds\\", StringComparison.OrdinalIgnoreCase) ? relative[12..] : relative;
            var full = Path.GetFullPath(Path.Combine(root, relative));
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && !full.Equals(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    public Task DeleteOwnedAsync(string resourcePath, IEnumerable<string?> retainedPaths, CancellationToken cancellationToken)
    {
        if (retainedPaths.Where(path => !string.IsNullOrEmpty(path)).Contains(resourcePath, StringComparer.OrdinalIgnoreCase))
        {
            return Task.CompletedTask;
        }
        return Task.Run(() =>
        {
            var fullPath = ResolvePath(resourcePath);
            if (fullPath is not null && File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }, cancellationToken);
    }

    private static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);

    private static BitmapDecoder Decode(Stream stream) => BitmapDecoder.Create(
        stream,
        BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreImageCache,
        BitmapCacheOption.OnLoad);

    private static void ValidateFrame(BitmapFrame frame, Guid? expectedFormat)
    {
        if ((long)frame.PixelWidth * frame.PixelHeight <= 0 || (long)frame.PixelWidth * frame.PixelHeight > VisualSettingsLimits.MaxBackgroundPixels)
        {
            throw new InvalidDataException($"解码像素超过 {VisualSettingsLimits.MaxBackgroundPixels:N0} 上限。");
        }
        var format = frame.Decoder.CodecInfo.ContainerFormat;
        if (format != PngFormat && format != JpegFormat && format != BmpFormat || expectedFormat is { } expected && format != expected)
        {
            throw new InvalidDataException("图片真实解码格式与允许格式或扩展名不一致。");
        }
    }

    private static void AdjustColors(byte[] pixels, double brightness, double saturation)
    {
        if (Math.Abs(brightness - 1) < 0.001 && Math.Abs(saturation - 1) < 0.001)
        {
            return;
        }
        var lookup = new byte[256];
        for (var index = 0; index < lookup.Length; index++)
        {
            lookup[index] = (byte)Math.Clamp(Math.Round(index * brightness), 0, 255);
        }
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var blue = lookup[pixels[index]];
            var green = lookup[pixels[index + 1]];
            var red = lookup[pixels[index + 2]];
            var luminance = 0.114 * blue + 0.587 * green + 0.299 * red;
            pixels[index] = Clamp(luminance + (blue - luminance) * saturation);
            pixels[index + 1] = Clamp(luminance + (green - luminance) * saturation);
            pixels[index + 2] = Clamp(luminance + (red - luminance) * saturation);
        }
    }

    private static byte Clamp(double value) => (byte)Math.Clamp(Math.Round(value), 0, 255);

    private static byte[] BoxBlur(byte[] source, int width, int height, int radius)
    {
        var current = source;
        for (var pass = 0; pass < 3; pass++)
        {
            var target = new byte[current.Length];
            BlurHorizontal(current, target, width, height, radius);
            BlurVertical(target, current, width, height, radius);
        }
        return current;
    }

    private static void BlurHorizontal(byte[] source, byte[] target, int width, int height, int radius)
    {
        for (var y = 0; y < height; y++)
        {
            var row = y * width * 4;
            for (var channel = 0; channel < 4; channel++)
            {
                var sum = 0;
                for (var x = -radius; x <= radius; x++)
                {
                    sum += source[row + Math.Clamp(x, 0, width - 1) * 4 + channel];
                }
                for (var x = 0; x < width; x++)
                {
                    target[row + x * 4 + channel] = (byte)(sum / (radius * 2 + 1));
                    var remove = source[row + Math.Max(0, x - radius) * 4 + channel];
                    var add = source[row + Math.Min(width - 1, x + radius + 1) * 4 + channel];
                    sum += add - remove;
                }
            }
        }
    }

    private static void BlurVertical(byte[] source, byte[] target, int width, int height, int radius)
    {
        var stride = width * 4;
        for (var x = 0; x < width; x++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var column = x * 4 + channel;
                var sum = 0;
                for (var y = -radius; y <= radius; y++)
                {
                    sum += source[Math.Clamp(y, 0, height - 1) * stride + column];
                }
                for (var y = 0; y < height; y++)
                {
                    target[y * stride + column] = (byte)(sum / (radius * 2 + 1));
                    var remove = source[Math.Max(0, y - radius) * stride + column];
                    var add = source[Math.Min(height - 1, y + radius + 1) * stride + column];
                    sum += add - remove;
                }
            }
        }
    }

    private static BackgroundImportResult Failure(string error) => new(false, string.Empty, error);
    private static bool IsImageOrIoError(Exception exception) => exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException;
}
