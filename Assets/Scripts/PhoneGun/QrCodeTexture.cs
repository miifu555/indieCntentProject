using QRCoder;
using UnityEngine;

// 埋め込んだQRCoder(QRCoder/フォルダ以下)を使い、文字列からQRコードのTexture2Dを生成する。
public static class QrCodeTexture
{
    public static Texture2D Generate(string text, int pixelsPerModule = 8, int quietZoneModules = 4)
    {
        using (var generator = new QRCodeGenerator())
        using (var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q))
        {
            var matrix = data.ModuleMatrix;
            int moduleCount = matrix.Count;
            int totalModules = moduleCount + quietZoneModules * 2;
            int size = totalModules * pixelsPerModule;

            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(0, 0, 0, 255);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = white;

            for (int y = 0; y < moduleCount; y++)
            {
                for (int x = 0; x < moduleCount; x++)
                {
                    if (!matrix[y][x]) continue;

                    // Texture2Dは左下が原点なので、行列の上下を反転させて配置する
                    int moduleX = x + quietZoneModules;
                    int moduleY = totalModules - 1 - (y + quietZoneModules);

                    int baseX = moduleX * pixelsPerModule;
                    int baseY = moduleY * pixelsPerModule;
                    for (int py = 0; py < pixelsPerModule; py++)
                    {
                        int rowStart = (baseY + py) * size + baseX;
                        for (int px = 0; px < pixelsPerModule; px++)
                        {
                            pixels[rowStart + px] = black;
                        }
                    }
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            tex.filterMode = FilterMode.Point;
            tex.SetPixels32(pixels);
            tex.Apply(false, false);
            return tex;
        }
    }
}
