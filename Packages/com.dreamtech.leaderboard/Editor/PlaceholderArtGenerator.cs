using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DreamTech.Leaderboard.EditorTools
{
    /// <summary>
    /// Sinh bộ sprite grayscale "chunky" (tint bằng Image.color) + material chữ viền đậm, xuất ra PNG asset.
    ///
    /// <para>Khác bản tham khảo: LUÔN ghi đè file và áp lại import settings mỗi lần chạy, đóng dấu phiên bản vào userData.
    /// Bản cũ bỏ qua file đã có nên sprite sinh từ thuật toán cũ nằm lại im lặng; và TextureImportProcessor của MLGameKit
    /// (không lọc đường dẫn) ép ASTC 6x6 lên PNG mới — quá thô cho gradient mềm 96px — nên phải đặt lại override sau import.</para>
    /// </summary>
    public static class PlaceholderArtGenerator
    {
        public const string GeneratorVersion = "dreamtech-leaderboard-art:v3";
        public const int ChunkyLip = 10;
        private const int MaximumTextureSize = 512;

        public sealed class ArtSet
        {
            public Sprite Chunky;
            public Sprite Rounded;
            public Sprite Circle;
            public Sprite SoftShadow;
            public Sprite Shine;
            public Sprite Rays;
            public Sprite Star;
            public Sprite Arrow;
            public Material TextOutline;

            public bool IsComplete => Chunky && Rounded && Circle && SoftShadow && Shine && Rays && Star && Arrow && TextOutline;
        }

        private const string RegenerateMenuPath = "Tools/DreamTech/Leaderboard/Regenerate Placeholder Art";

        [MenuItem(RegenerateMenuPath)]
        private static void RegenerateFromMenu()
        {
            Regenerate(LeaderboardEditorPaths.ArtFolder);
        }

        [MenuItem(RegenerateMenuPath, true)]
        private static bool CanRegenerateFromMenu()
        {
            return LeaderboardEditorPaths.IsWritable(LeaderboardEditorPaths.ArtFolder);
        }

        public static ArtSet Load(string folder)
        {
            return new ArtSet
            {
                Chunky = LoadSprite(folder, "LeaderboardChunky.png"),
                Rounded = LoadSprite(folder, "LeaderboardRounded.png"),
                Circle = LoadSprite(folder, "LeaderboardCircle.png"),
                SoftShadow = LoadSprite(folder, "LeaderboardSoftShadow.png"),
                Shine = LoadSprite(folder, "LeaderboardShine.png"),
                Rays = LoadSprite(folder, "LeaderboardRays.png"),
                Star = LoadSprite(folder, "LeaderboardStar.png"),
                Arrow = LoadSprite(folder, "LeaderboardArrow.png"),
                TextOutline = AssetDatabase.LoadAssetAtPath<Material>(folder + "/LeaderboardTextOutline.mat"),
            };
        }

        public static ArtSet Regenerate(string folder)
        {
            if (!LeaderboardEditorPaths.IsWritable(folder)) throw new InvalidOperationException("Thư mục chỉ đọc: " + folder);
            LeaderboardEditorPaths.EnsureFolder(folder);
            var art = new ArtSet
            {
                Chunky = WriteSprite(folder, "LeaderboardChunky.png", Chunky(96, 30), new Vector4(30, 30 + ChunkyLip, 30, 30)),
                Rounded = WriteSprite(folder, "LeaderboardRounded.png", Rounded(96, 30), new Vector4(30, 30, 30, 30)),
                Circle = WriteSprite(folder, "LeaderboardCircle.png", Rounded(128, 64), Vector4.zero),
                SoftShadow = WriteSprite(folder, "LeaderboardSoftShadow.png", SoftShadow(128, 30, 22), new Vector4(56, 56, 56, 56)),
                Shine = WriteSprite(folder, "LeaderboardShine.png", ShineStripe(), Vector4.zero),
                Rays = WriteSprite(folder, "LeaderboardRays.png", SunRays(), Vector4.zero),
                Star = WriteSprite(folder, "LeaderboardStar.png", Star(96), Vector4.zero),
                Arrow = WriteSprite(folder, "LeaderboardArrow.png", Arrow(64), Vector4.zero),
                TextOutline = WriteTextOutlineMaterial(folder + "/LeaderboardTextOutline.mat"),
            };
            AssetDatabase.SaveAssets();
            return art;
        }

        /// <summary>Kiểm sprite trong thư mục có được sinh bởi phiên bản generator hiện tại không.</summary>
        public static bool IsCurrentVersion(string folder, out string staleAsset)
        {
            foreach (string fileName in new[] { "LeaderboardChunky.png", "LeaderboardRounded.png", "LeaderboardCircle.png", "LeaderboardSoftShadow.png",
                                                "LeaderboardShine.png", "LeaderboardRays.png", "LeaderboardStar.png", "LeaderboardArrow.png" })
            {
                var importer = AssetImporter.GetAtPath(folder + "/" + fileName) as TextureImporter;
                if (importer == null || importer.userData != GeneratorVersion)
                {
                    staleAsset = folder + "/" + fileName;
                    return false;
                }
            }
            staleAsset = null;
            return true;
        }

        // ---------------------------------------------------------------- Ghi asset

        private static Sprite LoadSprite(string folder, string fileName)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(folder + "/" + fileName);
        }

        private static Sprite WriteSprite(string folder, string fileName, Texture2D texture, Vector4 border)
        {
            string path = folder + "/" + fileName;
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.spriteBorder = border;
            importer.spritePixelsPerUnit = 100f;
            importer.sRGBTexture = true;
            importer.maxTextureSize = MaximumTextureSize;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            foreach (string platform in new[] { "iPhone", "Android" })
            {
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = platform,
                    overridden = true,
                    maxTextureSize = MaximumTextureSize,
                    format = TextureImporterFormat.RGBA32,
                    textureCompression = TextureImporterCompression.Uncompressed,
                });
            }
            importer.userData = GeneratorVersion;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        /// <summary>Material chữ viền đậm + bóng đổ (TMP SDF: OUTLINE_ON + UNDERLAY_ON), tạo từ material mặc định của font TMP.</summary>
        private static Material WriteTextOutlineMaterial(string path)
        {
            TMP_FontAsset font = TMP_Settings.defaultFontAsset;
            if (font == null || font.material == null) throw new InvalidOperationException("Chưa có TMP Essentials (TMP_Settings.defaultFontAsset trống).");

            var material = new Material(font.material) { name = Path.GetFileNameWithoutExtension(path) };
            material.EnableKeyword("OUTLINE_ON");
            material.SetFloat("_OutlineWidth", 0.2f);
            material.SetColor("_OutlineColor", new Color(0.07f, 0.13f, 0.33f, 1f));
            material.SetFloat("_FaceDilate", 0.12f);
            material.EnableKeyword("UNDERLAY_ON");
            material.SetColor("_UnderlayColor", new Color(0.03f, 0.07f, 0.22f, 0.75f));
            material.SetFloat("_UnderlayOffsetX", 0f);
            material.SetFloat("_UnderlayOffsetY", -0.6f);
            material.SetFloat("_UnderlayDilate", 0.15f);
            material.SetFloat("_UnderlaySoftness", 0.05f);

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
            {
                existing.shader = material.shader;
                existing.CopyPropertiesFromMaterial(material);
                existing.shaderKeywords = material.shaderKeywords;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(material);
                return existing;
            }
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        // ---------------------------------------------------------------- Thuật toán (giữ nguyên bản tham khảo v3)

        private static float RoundedDistance(float pointX, float pointY, float centerX, float centerY, float halfWidth, float halfHeight, float radius)
        {
            float offsetX = Mathf.Abs(pointX - centerX) - (halfWidth - radius);
            float offsetY = Mathf.Abs(pointY - centerY) - (halfHeight - radius);
            float outside = new Vector2(Mathf.Max(offsetX, 0f), Mathf.Max(offsetY, 0f)).magnitude;
            float inside = Mathf.Min(Mathf.Max(offsetX, offsetY), 0f);
            return outside + inside - radius;
        }

        private static byte ToByte(float value)
        {
            return (byte)(Mathf.Clamp01(value) * 255f);
        }

        private static Texture2D Finish(int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        internal static Texture2D Rounded(int size, int radius)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = RoundedDistance(x + 0.5f, y + 0.5f, half, half, half, half, radius);
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(0.5f - distance));
                }
            return Finish(size, size, pixels);
        }

        /// <summary>Khối chunky: mặt trên sáng dần + dải highlight mép trên, gờ đáy tối.</summary>
        internal static Texture2D Chunky(int size, int radius)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float faceCenterY = half + ChunkyLip * 0.5f;
            float faceHalfHeight = half - ChunkyLip * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float pointX = x + 0.5f;
                    float pointY = y + 0.5f;
                    float outerDistance = RoundedDistance(pointX, pointY, half, half, half, half, radius);
                    float faceDistance = RoundedDistance(pointX, pointY, half, faceCenterY, half, faceHalfHeight, radius);
                    float alpha = Mathf.Clamp01(0.5f - outerDistance);
                    float faceMask = Mathf.Clamp01(0.5f - faceDistance);
                    float heightFactor = Mathf.Clamp01((pointY - ChunkyLip) / (size - ChunkyLip));
                    float face = Mathf.Lerp(0.86f, 0.97f, heightFactor);
                    float rim = Mathf.Clamp01(1f - (-faceDistance) / 5f) * Mathf.Clamp01((heightFactor - 0.55f) * 3f);
                    face = Mathf.Lerp(face, 1f, rim * 0.9f);
                    const float lipValue = 0.62f;
                    float value = Mathf.Lerp(lipValue, face, faceMask);
                    pixels[y * size + x] = new Color32(ToByte(value), ToByte(value), ToByte(value), ToByte(alpha));
                }
            return Finish(size, size, pixels);
        }

        internal static Texture2D SoftShadow(int size, int radius, int blur)
        {
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            float inner = half - blur;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = RoundedDistance(x + 0.5f, y + 0.5f, half, half, inner, inner, Mathf.Min(radius, inner));
                    float falloff = Mathf.Clamp01(1f - (distance + blur * 0.15f) / blur);
                    float alpha = falloff * falloff * (3f - 2f * falloff);
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(alpha));
                }
            return Finish(size, size, pixels);
        }

        internal static Texture2D Star(int size)
        {
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float horizontal = (x + 0.5f) / size * 2f - 1f;
                    float vertical = (y + 0.5f) / size * 2f - 1f;
                    float absoluteHorizontal = Mathf.Abs(horizontal);
                    float absoluteVertical = Mathf.Abs(vertical);
                    float radius = Mathf.Sqrt(horizontal * horizontal + vertical * vertical);
                    float cross = Mathf.Exp(-absoluteHorizontal * 14f) * Mathf.Clamp01(1f - absoluteVertical) +
                                  Mathf.Exp(-absoluteVertical * 14f) * Mathf.Clamp01(1f - absoluteHorizontal);
                    float core = Mathf.Exp(-radius * radius * 10f);
                    float alpha = Mathf.Clamp01(cross * 1.2f + core) * Mathf.Clamp01((1f - radius) * 4f);
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(alpha));
                }
            return Finish(size, size, pixels);
        }

        internal static Texture2D Arrow(int size)
        {
            var pixels = new Color32[size * size];
            float padding = size * 0.12f;
            float top = size - padding;
            float bottom = padding + size * 0.08f;
            float center = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float pointY = y + 0.5f;
                    float pointX = x + 0.5f;
                    float fromTop = Mathf.Clamp01((top - pointY) / (top - bottom));
                    float halfWidth = fromTop * (size * 0.5f - padding);
                    float horizontalDistance = halfWidth - Mathf.Abs(pointX - center);
                    float verticalDistance = Mathf.Min(top - pointY, pointY - bottom);
                    float alpha = Mathf.Clamp01(Mathf.Min(horizontalDistance, verticalDistance) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(alpha));
                }
            return Finish(size, size, pixels);
        }

        internal static Texture2D SunRays()
        {
            const int size = 256;
            const int rayCount = 12;
            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float offsetX = x + 0.5f - half;
                    float offsetY = y + 0.5f - half;
                    float radius = Mathf.Sqrt(offsetX * offsetX + offsetY * offsetY) / half;
                    float angle = Mathf.Atan2(offsetY, offsetX);
                    float ray = Mathf.Pow(0.5f + 0.5f * Mathf.Cos(angle * rayCount), 4f);
                    float falloff = Mathf.Clamp01(1f - radius);
                    float core = Mathf.Clamp01(1f - radius * 2.5f);
                    float alpha = Mathf.Clamp01((ray * 0.7f + 0.1f) * falloff * falloff + core * 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, ToByte(alpha));
                }
            return Finish(size, size, pixels);
        }

        internal static Texture2D ShineStripe()
        {
            const int width = 64;
            const int height = 4;
            var pixels = new Color32[width * height];
            for (int x = 0; x < width; x++)
            {
                float intensity = Mathf.Sin(Mathf.PI * (x + 0.5f) / width);
                for (int y = 0; y < height; y++) pixels[y * width + x] = new Color32(255, 255, 255, ToByte(intensity * intensity));
            }
            return Finish(width, height, pixels);
        }
    }
}
