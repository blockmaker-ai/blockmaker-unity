using System;
using UnityEngine;

namespace Blockmaker
{

    /// <summary>
    /// Generates QR code textures from string data using the built-in QRCodeEncoder.
    /// No external dependencies required.
    /// The caller owns the returned Texture2D and must call Destroy() when done.
    /// </summary>
    public static class QRTextureGenerator
    {
        private const int QUIET_ZONE = 4;

        public static Texture2D Generate(string data, int pixelSize = 256)
        {
            pixelSize = Mathf.Max(1, pixelSize);

            if (string.IsNullOrEmpty(data))
            {
                BlockmakerLog.Warning("[QRTextureGenerator] No data provided for QR generation.");
                return CreatePlaceholder(pixelSize);
            }

            try
            {
                bool[,] matrix = QRCodeEncoder.Encode(data);
                int moduleCount = matrix.GetLength(0);
                int totalModules = moduleCount + QUIET_ZONE * 2;
                int scale = Mathf.Max(1, pixelSize / totalModules);
                int texSize = totalModules * scale;

                var tex = new Texture2D(texSize, texSize, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Point
                };

                var pixels = new Color[texSize * texSize];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = Color.white;

                for (int row = 0; row < moduleCount; row++)
                for (int col = 0; col < moduleCount; col++)
                {
                    if (!matrix[row, col]) continue;
                    int px = (col + QUIET_ZONE) * scale;
                    int py = (totalModules - 1 - row - QUIET_ZONE) * scale;
                    for (int dy = 0; dy < scale; dy++)
                    for (int dx = 0; dx < scale; dx++)
                        pixels[(py + dy) * texSize + px + dx] = Color.black;
                }

                tex.SetPixels(pixels);
                tex.Apply();
                return tex;
            }
            catch (Exception e)
            {
                BlockmakerLog.Error($"[QRTextureGenerator] Failed to generate QR code: {e.Message}");
                return CreatePlaceholder(pixelSize);
            }
        }

        private static Texture2D CreatePlaceholder(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            var pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color(0.85f, 0.85f, 0.85f);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }

}