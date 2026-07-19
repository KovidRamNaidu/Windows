using System;
using System.IO;
using SkiaSharp;

namespace SnapPickWin.Services
{
    public static class ImageProcessor
    {
        public static string? GenerateThumbnail(string originalPath, int maxDimension = 1200)
        {
            try
            {
                if (!File.Exists(originalPath))
                {
                    Console.WriteLine($"❌ ImageProcessor: Original file does not exist: {originalPath}");
                    return null;
                }

                // 1. Create codec from file to read metadata and size
                using var codec = SKCodec.Create(originalPath);
                if (codec == null)
                {
                    Console.WriteLine($"❌ ImageProcessor: Failed to create codec for: {originalPath}");
                    return null;
                }

                int width = codec.Info.Width;
                int height = codec.Info.Height;
                float scale = 1f;

                // 2. Calculate scaling factor to respect maxDimension
                if (width > maxDimension || height > maxDimension)
                {
                    scale = (float)maxDimension / Math.Max(width, height);
                }

                int newWidth = (int)Math.Round(width * scale);
                int newHeight = (int)Math.Round(height * scale);

                // 3. Create target image info with sRGB color space
                // Replicates the color space check: forcing conversion to standard sRGB
                var imageInfo = new SKImageInfo(
                    newWidth, 
                    newHeight, 
                    SKColorType.Rgba8888, 
                    SKAlphaType.Premul, 
                    SKColorSpace.CreateSrgb()
                );

                using var bitmap = new SKBitmap(imageInfo);
                
                // 4. Decode original image into temporary bitmap
                var decodeInfo = new SKImageInfo(width, height, codec.Info.ColorType, codec.Info.AlphaType);
                using var tempBitmap = new SKBitmap(decodeInfo);
                var result = codec.GetPixels(tempBitmap.Info, tempBitmap.GetPixels());
                
                if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput)
                {
                    Console.WriteLine($"❌ ImageProcessor: Failed to decode pixels: {result}");
                    return null;
                }

                // 5. Scale pixels to target bitmap with High quality
                tempBitmap.ScalePixels(bitmap, SKFilterQuality.High);

                // 6. Convert bitmap to SKImage
                using var image = SKImage.FromBitmap(bitmap);
                if (image == null) return null;

                // 7. Dynamic quality loop: starting at 85% down to 55% to fit under 300KB
                int maxBytes = 300 * 1024;
                int quality = 85;
                byte[] webpBytes = Array.Empty<byte>();
                bool encodeSucceeded = false;

                while (quality >= 55)
                {
                    using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
                    if (data != null)
                    {
                        webpBytes = data.ToArray();
                        if (webpBytes.Length <= maxBytes)
                        {
                            encodeSucceeded = true;
                            break; // Fits under 300KB!
                        }
                    }
                    quality -= 10; // Lower quality by 10%
                }

                // If quality loop didn't break early, use the last quality level attempt (55%)
                if (!encodeSucceeded && webpBytes.Length > 0)
                {
                    encodeSucceeded = true;
                }

                if (!encodeSucceeded || webpBytes.Length == 0)
                {
                    Console.WriteLine("❌ ImageProcessor: Failed to encode image to WebP or data is empty.");
                    return null;
                }

                // 8. Save compressed WebP bytes to temporary directory
                string tempDir = Path.GetTempPath();
                string fileName = Guid.NewGuid().ToString() + ".webp";
                string fileURL = Path.Combine(tempDir, fileName);
                
                File.WriteAllBytes(fileURL, webpBytes);
                return fileURL;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ ImageProcessor: Exception during thumbnail generation: {ex.Message}");
                return null;
            }
        }
    }
}
