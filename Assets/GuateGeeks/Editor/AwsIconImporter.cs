using UnityEditor;
using UnityEngine;

namespace GuateGeeks.AwsVr.Editor
{
    public sealed class AwsIconImporter : AssetPostprocessor
    {
        // Bumped when the rules change so existing textures are re-imported with them.
        public override uint GetVersion() => 2;
        void OnPreprocessTexture()
        {
            var importer = (TextureImporter)assetImporter;
            if (assetPath.StartsWith("Assets/GuateGeeks/Resources/Branding/") && assetPath.EndsWith("SDF.png"))
            {
                // Signed-distance fields are data, not colour: linear, opaque, filtered and full precision.
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = false; importer.alphaIsTransparency = false;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4; importer.maxTextureSize = 2048; importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
                return;
            }
            if (assetPath.StartsWith("Assets/GuateGeeks/Resources/Branding/"))
            {
                // Event branding: crisp edges at distance, transparent background, colour fringes avoided by alpha bleed.
                importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true; importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 4; importer.maxTextureSize = 2048; importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed; importer.isReadable = false;
                return;
            }
            if (!assetPath.StartsWith("Assets/GuateGeeks/Resources/AwsIcons/")) return;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.maxTextureSize = 512;
            // Fine white outlines should survive Quest compression and oblique viewing.
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = false;
        }
    }
}
