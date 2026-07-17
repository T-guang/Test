using UnityEditor;
using UnityEngine;
using System;

public class ImportUIAssets : AssetPostprocessor
{
    private const string UiAssetsRoot = "Assets/Resources/UIAssets/";

    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith(UiAssetsRoot, StringComparison.Ordinal))
        {
            ApplyUiSpriteSettings((TextureImporter)assetImporter);
        }
    }

    [MenuItem("Tools/Force Import UI Assets As Sprite")]
    public static void ForceImport()
    {
        AssetDatabase.Refresh();
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Resources/UIAssets" });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                if (ApplyUiSpriteSettings(importer))
                {
                    importer.SaveAndReimport();
                }
            }
        }
        Debug.Log("Applied UIAssets Sprite settings where required.");
    }

    // UIAssets contains both small icons and page-scale art. Compression remains an
    // asset-level decision; this shared importer only enforces common Sprite settings.
    private static bool ApplyUiSpriteSettings(TextureImporter importer)
    {
        var changed = false;
        if (importer.textureType != TextureImporterType.Sprite)
        {
            importer.textureType = TextureImporterType.Sprite;
            changed = true;
        }
        if (importer.spriteImportMode != SpriteImportMode.Single)
        {
            importer.spriteImportMode = SpriteImportMode.Single;
            changed = true;
        }
        if (!importer.alphaIsTransparency)
        {
            importer.alphaIsTransparency = true;
            changed = true;
        }
        if (importer.mipmapEnabled)
        {
            importer.mipmapEnabled = false;
            changed = true;
        }
        if (importer.filterMode != FilterMode.Bilinear)
        {
            importer.filterMode = FilterMode.Bilinear;
            changed = true;
        }
        return changed;
    }
}
