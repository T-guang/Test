#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ElectricalSim.Core;
using ElectricalSim.UI;
using ElectricalSim.UI.CommonTools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace ElectricalSim.EditorTools
{
    /// <summary>
    /// 仅供 Unity Editor 维护 Demo 场景和初始演示资产的生成工具。
    /// 菜单 <c>Tools/Electrical Demo/Build Demo Scene</c> 会新建空场景、构建 EventSystem/Camera/Canvas 及其依赖对象，
    /// 然后保存为 <c>Assets/Scenes/Demo.unity</c>；当前实现还会确保若干 Assets 目录存在、创建或更新 ComponentDefinition、
    /// 三相电源图片、<c>Assets/Blueprints/BlueprintCatalog.csv</c> 与缺失的图纸占位图片，并保存和刷新 AssetDatabase。
    ///
    /// 此入口具有覆盖性和破坏性：它以 Single 模式切换到新场景，并删除固定列表中的旧 Definition 资产；执行前必须确认
    /// 当前未保存场景和目标资产差异可被替换。它不修改 Build Settings，且不是 Player 运行时数据源；生成完成只表示
    /// 写入流程结束，仍需人工检查场景引用、页面交互和回归结果。当前代码未在入口显式限制 Play Mode，运行前应人工确认
    /// 已退出 Play Mode。当前 Builder 仍会创建 LoginController 和登录页面相关对象，与当前已移除登录入口的正式产品状态可能不一致；
    /// 执行前需重新确认该生成器是否仍适用于当前 Demo 场景。文件系统或 AssetDatabase 操作失败会由 Unity/调用链报告，
    /// 后续调整前须保持既有路径、对象命名和生成顺序。
    /// </summary>
    public static class DemoSceneBuilder
    {
        private const string DataFolder = "Assets/Data";
        private const string ArtFolder = "Assets/Art";
        private const string ComponentArtFolder = "Assets/Art/Components";
        private const string BlueprintFolder = "Assets/Blueprints";
        private const string BlueprintImageFolder = "Assets/Blueprints/Images";
        private const string BlueprintCatalogPath = "Assets/Blueprints/BlueprintCatalog.csv";
        private const string ThreePhasePowerSpritePath = "Assets/Art/Components/AC_ThreePhase_Power.png";
        private const string ScenePath = "Assets/Scenes/Demo.unity";

        private sealed class BlueprintEntry
        {
            public string Path;
            public string Title;
            public string RecommendedComponents;
            public int Category;
            public int Difficulty;
            public Sprite Sprite;
        }

        [MenuItem("Tools/Electrical Demo/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            // 此菜单会创建并保存实际资产与 Demo.unity，不是可安全重复运行的只读预览。
            EnsureFolders();
            var definitions = BuildDefinitions();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Demo";

            BuildEventSystem();
            BuildCamera();
            BuildCanvas(definitions);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog("Electrical Demo", "演示场景已生成：" + ScenePath, "OK");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            if (!AssetDatabase.IsValidFolder(DataFolder)) AssetDatabase.CreateFolder("Assets", "Data");
            if (!AssetDatabase.IsValidFolder(ArtFolder)) AssetDatabase.CreateFolder("Assets", "Art");
            if (!AssetDatabase.IsValidFolder(ComponentArtFolder)) AssetDatabase.CreateFolder(ArtFolder, "Components");
            if (!AssetDatabase.IsValidFolder(BlueprintFolder)) AssetDatabase.CreateFolder("Assets", "Blueprints");
            if (!AssetDatabase.IsValidFolder(BlueprintImageFolder)) AssetDatabase.CreateFolder(BlueprintFolder, "Images");
        }

        private static List<ComponentDefinition> BuildDefinitions()
        {
            DeleteObsoleteDefinitions();
            var threePhasePowerSprite = EnsureThreePhasePowerSprite();

            var definitions = new List<ComponentDefinition>
            {
                CreateDefinition("AC_220V_Power", "\u0032\u0032\u0030\u0056\u7535\u6e90", ComponentKind.PowerSource, new Color(0.86f, 0.92f, 1f), new Color(0.12f, 0.45f, 1f), true, false,
                    new TerminalDefinition { id = "L", label = "L", role = TerminalRole.Phase, normalizedPosition = new Vector2(0.28f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "N", label = "N", role = TerminalRole.Neutral, normalizedPosition = new Vector2(0.72f, 0f), color = new Color(0.1f, 0.35f, 0.95f) }),
                CreateThreePhasePower(threePhasePowerSprite),
                CreateDefinition("Single_Control_Switch", "单开单控开关", ComponentKind.Switch, new Color(0.92f, 0.92f, 0.9f), new Color(0.1f, 0.65f, 0.25f), false, true,
                    new TerminalDefinition { id = "L", label = "L", role = TerminalRole.Input, normalizedPosition = new Vector2(0.5f, 1f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "L1", label = "L1", role = TerminalRole.Output, normalizedPosition = new Vector2(0.5f, 0f), color = new Color(0.95f, 0.12f, 0.12f) }),
                CreateDefinition("Two_Way_Switch", "单开双控开关", ComponentKind.TwoWaySwitch, new Color(0.92f, 0.92f, 0.9f), new Color(0.1f, 0.65f, 0.25f), true, true,
                    new TerminalDefinition { id = "L", label = "L", role = TerminalRole.Input, normalizedPosition = new Vector2(0.5f, 1f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "L1", label = "L1", role = TerminalRole.Output, normalizedPosition = new Vector2(0.28f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "L2", label = "L2", role = TerminalRole.Output, normalizedPosition = new Vector2(0.72f, 0f), color = new Color(0.95f, 0.12f, 0.12f) }),
                CreateDefinition("Lamp_220V", "电灯泡(220V)", ComponentKind.Lamp, new Color(1f, 0.96f, 0.82f), new Color(1f, 0.78f, 0.08f), false, false,
                    new TerminalDefinition { id = "L", label = "L", role = TerminalRole.Input, normalizedPosition = new Vector2(0.25f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "N", label = "N", role = TerminalRole.Neutral, normalizedPosition = new Vector2(0.75f, 0f), color = new Color(0.1f, 0.35f, 0.95f) }),
                CreateDefinition("Fan_220V", "电风扇(220V)", ComponentKind.Fan, new Color(0.88f, 0.96f, 1f), new Color(0.2f, 0.62f, 1f), false, false,
                    new TerminalDefinition { id = "L", label = "L", role = TerminalRole.Input, normalizedPosition = new Vector2(0.25f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "N", label = "N", role = TerminalRole.Neutral, normalizedPosition = new Vector2(0.75f, 0f), color = new Color(0.1f, 0.35f, 0.95f) }),
                CreateDefinition("Single_Phase_Meter", "单相电能表(220V)", ComponentKind.EnergyMeter, new Color(0.9f, 0.93f, 0.95f), new Color(0.15f, 0.42f, 0.92f), true, false,
                    new TerminalDefinition { id = "L_IN", label = "L进", role = TerminalRole.Phase, normalizedPosition = new Vector2(0.18f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "L_OUT", label = "L出", role = TerminalRole.Output, normalizedPosition = new Vector2(0.38f, 0f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "N_IN", label = "N进", role = TerminalRole.Neutral, normalizedPosition = new Vector2(0.62f, 0f), color = new Color(0.1f, 0.35f, 0.95f) },
                    new TerminalDefinition { id = "N_OUT", label = "N出", role = TerminalRole.Neutral, normalizedPosition = new Vector2(0.82f, 0f), color = new Color(0.1f, 0.35f, 0.95f) }),
                CreateDefinition("Breaker_1P", "空气开关1P", ComponentKind.Breaker, new Color(0.9f, 0.92f, 0.96f), new Color(0.15f, 0.42f, 0.92f), true, true,
                    new TerminalDefinition { id = "IN", label = "进", role = TerminalRole.Input, normalizedPosition = new Vector2(0.5f, 1f), color = new Color(0.95f, 0.12f, 0.12f) },
                    new TerminalDefinition { id = "OUT", label = "出", role = TerminalRole.Output, normalizedPosition = new Vector2(0.5f, 0f), color = new Color(0.95f, 0.12f, 0.12f) }),
                CreateBreaker("Breaker_2P", "空气开关2P", 2),
                CreateBreaker("Breaker_3P", "空气开关3P", 3),
                CreateBreaker("Breaker_4P", "空气开关4P", 4)
            };

            AddIndustrialDefinitions(definitions);
            AddMeasurementDefinitions(definitions);
            return definitions;
        }

        private static void DeleteObsoleteDefinitions()
        {
            // 固定旧资产清单属于生成契约；变更前须确认不会误删仍被现有模板或场景引用的 Definition。
            var obsoleteAssets = new[] { "Single_Switch", "Fuse", "Push_Button", "Contactor_Coil", "Motor_220V" };
            foreach (var assetName in obsoleteAssets)
            {
                AssetDatabase.DeleteAsset($"{DataFolder}/{assetName}.asset");
            }
        }

        private static ComponentDefinition CreateThreePhasePower(Sprite sprite)
        {
            var definition = CreateDefinition("AC_ThreePhase_Power", "\u4e09\u76f8\u4ea4\u6d41\u7535\u6e90", ComponentKind.PowerSource, new Color(1f, 0.98f, 0.88f), new Color(1f, 0.78f, 0.12f), true, false,
                T("L1", "L1", TerminalRole.Phase, 0.10f, 0.35f, Yellow()),
                T("L2", "L2", TerminalRole.Phase, 0.30f, 0.35f, Green()),
                T("L3", "L3", TerminalRole.Phase, 0.50f, 0.35f, Red()),
                T("N", "N", TerminalRole.Neutral, 0.70f, 0.35f, Blue()),
                T("PE", "PE", TerminalRole.ProtectiveEarth, 0.90f, 0.35f, Green()));
            definition.category = ComponentCategory.Industrial;
            definition.sprite = sprite;
            definition.size = new Vector2(420f, 100f);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Sprite EnsureThreePhasePowerSprite()
        {
            if (!File.Exists(ThreePhasePowerSpritePath))
            {
                GenerateThreePhasePowerPng(ThreePhasePowerSpritePath);
            }

            AssetDatabase.ImportAsset(ThreePhasePowerSpritePath, ImportAssetOptions.ForceUpdate);
            var importer = AssetImporter.GetAtPath(ThreePhasePowerSpritePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Sprite>(ThreePhasePowerSpritePath);
        }

        private static void GenerateThreePhasePowerPng(string path)
        {
            const int width = 568;
            const int height = 112;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var background = new Color32(255, 252, 231, 255);
            var border = new Color32(248, 186, 28, 255);

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, background);
                }
            }

            for (var i = 0; i < 3; i++)
            {
                for (var x = 0; x < width; x++)
                {
                    texture.SetPixel(x, i, border);
                    texture.SetPixel(x, height - 1 - i, border);
                }

                for (var y = 0; y < height; y++)
                {
                    texture.SetPixel(i, y, border);
                    texture.SetPixel(width - 1 - i, y, border);
                }
            }

            DrawTerminalSymbol(texture, 57, 38, new Color32(246, 209, 62, 255));
            DrawTerminalSymbol(texture, 171, 38, new Color32(129, 235, 168, 255));
            DrawTerminalSymbol(texture, 284, 38, new Color32(255, 92, 92, 255));
            DrawTerminalSymbol(texture, 398, 38, new Color32(83, 151, 239, 255));
            DrawProtectiveEarthSymbol(texture, 511, 38);

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void DrawTerminalSymbol(Texture2D texture, int centerX, int centerY, Color32 color)
        {
            FillCircle(texture, centerX, centerY, 22, new Color32(0, 0, 0, 255));
            FillCircle(texture, centerX, centerY, 18, color);
            FillCircle(texture, centerX, centerY, 8, new Color32(0, 0, 0, 255));
        }

        private static void DrawProtectiveEarthSymbol(Texture2D texture, int centerX, int centerY)
        {
            FillCircle(texture, centerX, centerY, 22, new Color32(0, 0, 0, 255));
            FillCircle(texture, centerX, centerY, 18, new Color32(88, 218, 130, 255));
            FillCircle(texture, centerX, centerY, 13, new Color32(250, 218, 68, 255));
            FillCircle(texture, centerX, centerY, 8, new Color32(0, 0, 0, 255));
        }

        private static void FillCircle(Texture2D texture, int centerX, int centerY, int radius, Color32 color)
        {
            var radiusSquared = radius * radius;
            for (var y = -radius; y <= radius; y++)
            {
                for (var x = -radius; x <= radius; x++)
                {
                    if (x * x + y * y > radiusSquared)
                    {
                        continue;
                    }

                    var px = centerX + x;
                    var py = centerY + y;
                    if (px >= 0 && px < texture.width && py >= 0 && py < texture.height)
                    {
                        texture.SetPixel(px, py, color);
                    }
                }
            }
        }

        private static List<BlueprintEntry> EnsureBlueprintCatalog()
        {
            EnsureBlueprintCatalogFile();
            var entries = LoadBlueprintCatalogFromCsv();

            for (var i = 0; i < entries.Count; i++)
            {
                if (!File.Exists(entries[i].Path))
                {
                    GenerateBlueprintPlaceholder(entries[i].Path, i);
                }

                AssetDatabase.ImportAsset(entries[i].Path, ImportAssetOptions.ForceUpdate);
                var importer = AssetImporter.GetAtPath(entries[i].Path) as TextureImporter;
                if (importer != null)
                {
                    importer.textureType = TextureImporterType.Sprite;
                    importer.spriteImportMode = SpriteImportMode.Single;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                entries[i].Sprite = AssetDatabase.LoadAssetAtPath<Sprite>(entries[i].Path);
            }

            return entries;
        }

        private static void EnsureBlueprintCatalogFile()
        {
            if (File.Exists(BlueprintCatalogPath))
            {
                return;
            }

            File.WriteAllText(BlueprintCatalogPath, DefaultBlueprintCatalogCsv(), new System.Text.UTF8Encoding(true));
            AssetDatabase.ImportAsset(BlueprintCatalogPath, ImportAssetOptions.ForceUpdate);
        }

        private static List<BlueprintEntry> LoadBlueprintCatalogFromCsv()
        {
            var entries = new List<BlueprintEntry>();
            var lines = File.ReadAllLines(BlueprintCatalogPath);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                {
                    continue;
                }

                var columns = line.Split(',');
                if (columns.Length < 4)
                {
                    Debug.LogWarning("Blueprint catalog row skipped: " + line);
                    continue;
                }

                var entry = BP(columns[0].Trim(), columns[1].Trim(), ParseBlueprintCategory(columns[2].Trim()), ParseBlueprintDifficulty(columns[3].Trim()));
                entry.RecommendedComponents = columns.Length >= 5 ? columns[4].Trim() : string.Empty;
                entries.Add(entry);
            }

            return entries;
        }

        private static int ParseBlueprintCategory(string value)
        {
            return value == "\u5bb6\u5ead" || value.Equals("Home", System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }

        private static int ParseBlueprintDifficulty(string value)
        {
            if (value == "\u521d\u7ea7" || value.Equals("Primary", System.StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (value == "\u9ad8\u7ea7" || value.Equals("Advanced", System.StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            return 1;
        }

        private static string DefaultBlueprintCatalogCsv()
        {
            return "FileName,DisplayName,Category,Difficulty,RecommendedComponents\n" +
                "QQ20260515-175058.png,\u7535\u52a8\u673a\u4e92\u9501\u6b63\u53cd\u8f6c,\u5de5\u4e1a,\u521d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175124.png,\u7535\u6c14\u4e92\u9501\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u521d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175135.png,\u7535\u52a8\u673a\u81ea\u9501\u63a7\u5236,\u5de5\u4e1a,\u521d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175206.png,\u7535\u52a8\u673a\u70b9\u52a8\u63a7\u5236,\u5de5\u4e1a,\u521d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "Auto_Reciprocating_Motor.png,\u81ea\u52a8\u5f80\u8fd4\u7535\u52a8\u673a\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u9650\u4f4d\u5f00\u5173SQ|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175227.png,\u7535\u52a8\u673a\u6b63\u53cd\u8f6c\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175239.png,\u53cc\u91cd\u4e92\u9501\u6b63\u53cd\u8f6c\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175250.png,\u63a5\u89e6\u5668\u8054\u9501\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175303.png,\u884c\u7a0b\u5f00\u5173\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u9650\u4f4d\u5f00\u5173SQ|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175314.png,\u987a\u5e8f\u542f\u52a8\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u65f6\u95f4\u7ee7\u7535\u5668KT|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175325.png,\u4e24\u53f0\u7535\u52a8\u673a\u987a\u5e8f\u63a7\u5236,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175335.png,\u7535\u52a8\u673a\u5ef6\u65f6\u63a7\u5236\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u65f6\u95f4\u7ee7\u7535\u5668KT|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175351.png,\u5de5\u4e1a\u63a7\u5236\u7efc\u5408\u7535\u8def,\u5de5\u4e1a,\u4e2d\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "QQ20260515-175112.png,\u661f\u4e09\u89d2\u7535\u8def,\u5de5\u4e1a,\u9ad8\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u65f6\u95f4\u7ee7\u7535\u5668KT|\u6309\u94aeSB|\u661f\u4e09\u89d2\u7535\u673a\n" +
                "Timer_Sequence_Start.png,\u65f6\u95f4\u7ee7\u7535\u5668\u63a7\u5236\u7684\u987a\u5e8f\u8d77\u52a8\u7535\u8def,\u5de5\u4e1a,\u9ad8\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u65f6\u95f4\u7ee7\u7535\u5668KT|\u6309\u94aeSB|\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n" +
                "Star_Delta_Start_5.png,\u661f\u4e09\u89d2\u964d\u538b\u542f\u52a8\u7535\u8def\u4e94,\u5de5\u4e1a,\u9ad8\u7ea7,\u4ea4\u6d41\u7535\u6e90|\u5200\u5f00\u5173QS|\u7194\u65ad\u5668FU|\u4ea4\u6d41\u63a5\u89e6\u5668KM|\u70ed\u7ee7\u7535\u5668FR|\u65f6\u95f4\u7ee7\u7535\u5668KT|\u6309\u94aeSB|\u661f\u4e09\u89d2\u7535\u673a\n" +
                "QQ20260515-175736.png,\u53cc\u5f00\u5173\u63a7\u5236\u7167\u660e\u7535\u8def,\u5bb6\u5ead,\u4e2d\u7ea7,\u0032\u0032\u0030\u0056\u7535\u6e90|\u5355\u5f00\u53cc\u63a7\u5f00\u5173|\u7535\u706f\u6ce1|\u7a7a\u6c14\u5f00\u5173\u0031\u0050\n" +
                "QQ20260515-175752.png,\u7535\u5ea6\u8868\u63a5\u7ebf\u4e0e\u7167\u660e\u7535\u8def,\u5bb6\u5ead,\u4e2d\u7ea7,\u0032\u0032\u0030\u0056\u7535\u6e90|\u5355\u76f8\u7535\u80fd\u8868|\u5355\u5f00\u5355\u63a7\u5f00\u5173|\u7535\u706f\u6ce1|\u7535\u98ce\u6247|\u7a7a\u6c14\u5f00\u5173\u0032\u0050\n";
        }

        private static BlueprintEntry BP(string fileName, string title, int category, int difficulty)
        {
            return new BlueprintEntry
            {
                Path = $"{BlueprintImageFolder}/{fileName}",
                Title = title,
                Category = category,
                Difficulty = difficulty
            };
        }

        private static void GenerateBlueprintPlaceholder(string path, int variant)
        {
            const int width = 960;
            const int height = 620;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var white = new Color32(255, 255, 255, 255);
            var black = new Color32(20, 20, 20, 255);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    texture.SetPixel(x, y, white);
                }
            }

            var offset = variant * 24;
            DrawLine(texture, 80, 490, 520 + offset, 490, black, 4);
            DrawLine(texture, 80, 450, 460 + offset, 450, black, 4);
            DrawLine(texture, 80, 410, 360 + offset, 410, black, 4);
            DrawLine(texture, 220, 490, 220, 150, black, 4);
            DrawLine(texture, 300, 450, 300, 150, black, 4);
            DrawLine(texture, 380, 410, 380, 150, black, 4);
            DrawRect(texture, 190, 250, 80, 42, black, 4);
            DrawRect(texture, 270, 250, 80, 42, black, 4);
            DrawRect(texture, 350, 250, 80, 42, black, 4);
            DrawCircleOutline(texture, 300, 95, 55, black, 4);
            DrawLine(texture, 560, 500, 840, 500, black, 4);
            DrawLine(texture, 560, 120, 840, 120, black, 4);
            for (var i = 0; i < 4; i++)
            {
                var x = 610 + i * 70;
                DrawLine(texture, x, 500, x, 120, black, 4);
                DrawRect(texture, x - 22, 190 + i * 55 % 220, 44, 32, black, 4);
            }

            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
        }

        private static void DrawLine(Texture2D texture, int x0, int y0, int x1, int y1, Color32 color, int thickness)
        {
            var dx = Mathf.Abs(x1 - x0);
            var dy = Mathf.Abs(y1 - y0);
            var sx = x0 < x1 ? 1 : -1;
            var sy = y0 < y1 ? 1 : -1;
            var err = dx - dy;

            while (true)
            {
                FillCircle(texture, x0, y0, thickness, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                var e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void DrawRect(Texture2D texture, int x, int y, int width, int height, Color32 color, int thickness)
        {
            DrawLine(texture, x, y, x + width, y, color, thickness);
            DrawLine(texture, x + width, y, x + width, y + height, color, thickness);
            DrawLine(texture, x + width, y + height, x, y + height, color, thickness);
            DrawLine(texture, x, y + height, x, y, color, thickness);
        }

        private static void DrawCircleOutline(Texture2D texture, int centerX, int centerY, int radius, Color32 color, int thickness)
        {
            for (var a = 0; a < 360; a += 2)
            {
                var rad = a * Mathf.Deg2Rad;
                var x = centerX + Mathf.RoundToInt(Mathf.Cos(rad) * radius);
                var y = centerY + Mathf.RoundToInt(Mathf.Sin(rad) * radius);
                FillCircle(texture, x, y, thickness, color);
            }
        }

        private static void AddIndustrialDefinitions(List<ComponentDefinition> definitions)
        {
            definitions.Add(CreateIndustrial("Fuse_1P", "\u7194\u65ad\u56681P(FU)", ComponentKind.Fuse, new Color(0.92f, 0.94f, 0.96f), new Color(0.92f, 0.2f, 0.16f), true, true,
                T("IN", "\u8fdb", TerminalRole.Input, 0.5f, 1f, Red()),
                T("OUT", "\u51fa", TerminalRole.Output, 0.5f, 0f, Red())));
            definitions.Add(CreateIndustrial("Fuse_3P", "\u7194\u65ad\u56683P(FU)", ComponentKind.Fuse, new Color(0.92f, 0.94f, 0.96f), new Color(0.92f, 0.2f, 0.16f), true, true,
                T("L1_IN", "L1", TerminalRole.Input, 0.2f, 1f, Yellow()), T("L1_OUT", "T1", TerminalRole.Output, 0.2f, 0f, Yellow()),
                T("L2_IN", "L2", TerminalRole.Input, 0.5f, 1f, Green()), T("L2_OUT", "T2", TerminalRole.Output, 0.5f, 0f, Green()),
                T("L3_IN", "L3", TerminalRole.Input, 0.8f, 1f, Red()), T("L3_OUT", "T3", TerminalRole.Output, 0.8f, 0f, Red())));
            definitions.Add(CreateIndustrial("Button_Compound_SB", "\u590d\u5408\u6309\u94aeSB(\u7ea2)", ComponentKind.PushButton, new Color(0.2f, 0.22f, 0.24f), new Color(1f, 0.28f, 0.24f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("Button_SelfLock_SB", "\u81ea\u9501\u5f00\u5173SB(\u7ea2)", ComponentKind.Switch, new Color(0.2f, 0.22f, 0.24f), new Color(1f, 0.28f, 0.24f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("Button_Compound_Green_SB", "\u590d\u5408\u6309\u94aeSB(\u7eff)", ComponentKind.PushButton, new Color(0.2f, 0.22f, 0.24f), new Color(0.22f, 0.72f, 0.36f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("Button_SelfLock_Green_SB", "\u81ea\u9501\u5f00\u5173SB(\u7eff)", ComponentKind.Switch, new Color(0.2f, 0.22f, 0.24f), new Color(0.22f, 0.72f, 0.36f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("Button_Start_NO", "\u542f\u52a8\u6309\u94ae(NO)", ComponentKind.PushButton, new Color(0.2f, 0.22f, 0.24f), new Color(0.22f, 0.72f, 0.36f), false, true,
                T("23", "23", TerminalRole.Input, 0f, 0.45f, Green()),
                T("24", "24", TerminalRole.Output, 1f, 0.45f, Green())));
            definitions.Add(CreateIndustrial("Button_Stop_NC", "\u505c\u6b62\u6309\u94ae(NC)", ComponentKind.PushButton, new Color(0.2f, 0.22f, 0.24f), new Color(1f, 0.28f, 0.24f), true, true,
                T("11", "11", TerminalRole.Input, 0f, 0.45f, Red()),
                T("12", "12", TerminalRole.Output, 1f, 0.45f, Red())));
            definitions.Add(CreateIndustrial("EmergencyStop_NC", "\u6025\u505c\u6309\u94ae(NC)", ComponentKind.PushButton, new Color(0.2f, 0.22f, 0.24f), new Color(1f, 0.18f, 0.14f), true, true,
                T("11", "11", TerminalRole.Input, 0f, 0.45f, Red()),
                T("12", "12", TerminalRole.Output, 1f, 0.45f, Red())));
            definitions.Add(CreateIndustrial("LimitSwitch_Compound", "\u884c\u7a0b\u5f00\u5173 SQ", ComponentKind.PushButton, new Color(0.18f, 0.2f, 0.22f), new Color(0.74f, 0.62f, 0.42f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("LimitSwitch_SelfLock", "\u9650\u4f4d\u5f00\u5173(\u81ea\u9501)", ComponentKind.Switch, new Color(0.18f, 0.2f, 0.22f), new Color(0.74f, 0.62f, 0.42f), false, true, ControlTerminals()));
            definitions.Add(CreateIndustrial("KnifeSwitch_QS", "\u5200\u5f00\u5173(QS)", ComponentKind.Breaker, new Color(0.5f, 0.53f, 0.6f), new Color(0.18f, 0.34f, 0.7f), true, true,
                ThreePhasePairTerminals()));
            definitions.Add(CreateIndustrial("Contactor_KM_220V", "\u4ea4\u6d41\u63a5\u89e6\u5668(KM)\n(220V)", ComponentKind.ContactorCoil, new Color(0.82f, 0.86f, 0.9f), new Color(0.2f, 0.45f, 1f), false, false, ContactorTerminals()));
            definitions.Add(CreateIndustrial("Contactor_KM_380V", "\u4ea4\u6d41\u63a5\u89e6\u5668(KM)\n(380V)", ComponentKind.ContactorCoil, new Color(0.82f, 0.86f, 0.9f), new Color(0.2f, 0.45f, 1f), false, false, ContactorTerminals()));
            definitions.Add(CreateIndustrial("Motor_ThreePhase_380V", "\u4e09\u76f8\u5f02\u6b65\u7535\u52a8\u673a\n(380V)", ComponentKind.Motor, new Color(0.74f, 0.88f, 1f), new Color(0.18f, 0.56f, 1f), false, false,
                T("U", "U", TerminalRole.Input, 0.88f, 0.78f, Yellow()), T("V", "V", TerminalRole.Input, 0.88f, 0.55f, Green()), T("W", "W", TerminalRole.Input, 0.88f, 0.32f, Red()), T("PE", "PE", TerminalRole.ProtectiveEarth, 1f, 0.08f, Green())));
            var starDeltaMotor = CreateIndustrial("Motor_StarDelta_380V", "\u661f\u4e09\u89d2\u7535\u673a\n(380V)", ComponentKind.Motor, new Color(0.74f, 0.88f, 1f), new Color(0.18f, 0.56f, 1f), false, false,
                T("U1", "U1", TerminalRole.Input, 0.2f, 1f, Yellow()), T("V1", "V1", TerminalRole.Input, 0.5f, 1f, Green()), T("W1", "W1", TerminalRole.Input, 0.8f, 1f, Red()),
                T("U2", "U2", TerminalRole.Output, 0.2f, 0f, Yellow()), T("V2", "V2", TerminalRole.Output, 0.5f, 0f, Green()), T("W2", "W2", TerminalRole.Output, 0.8f, 0f, Red()),
                T("PE", "PE", TerminalRole.ProtectiveEarth, 1f, 0.5f, Green()));
            starDeltaMotor.allowSameComponentJumper = true;
            definitions.Add(starDeltaMotor);
            definitions.Add(CreateIndustrial("ThermalRelay_FR_380V", "\u70ed\u7ee7\u7535\u5668(FR)\n(380V)", ComponentKind.Breaker, new Color(0.28f, 0.3f, 0.32f), new Color(0.85f, 0.18f, 0.16f), true, true, ThermalRelayTerminals()));
            definitions.Add(CreateIndustrial("SolenoidValve_24V", "\u7535\u78c1\u9600\n(24V)", ComponentKind.Indicator, new Color(0.86f, 0.9f, 0.94f), new Color(0.16f, 0.5f, 0.95f), false, false, CoilTerminals()));
            var onDelayTimer220 = CreateIndustrial("Timer_OnDelay_220V", "\u901a\u7535\u5ef6\u65f6\u65f6\u95f4\u7ee7\u7535\u5668(KT)\n(220V)", ComponentKind.ContactorCoil, new Color(0.9f, 0.94f, 0.96f), new Color(1f, 0.25f, 0.12f), false, true, TimerRelayTerminals());
            onDelayTimer220.parameters = TimerDelayParameters();
            definitions.Add(onDelayTimer220);
            var onDelayTimer380 = CreateIndustrial("Timer_OnDelay_380V", "\u901a\u7535\u5ef6\u65f6\u65f6\u95f4\u7ee7\u7535\u5668(KT)\n(380V)", ComponentKind.ContactorCoil, new Color(0.9f, 0.94f, 0.96f), new Color(1f, 0.25f, 0.12f), false, true, TimerRelayTerminals());
            onDelayTimer380.parameters = TimerDelayParameters();
            definitions.Add(onDelayTimer380);
            definitions.Add(CreateIndustrial("Timer_OffDelay_220V", "\u65ad\u7535\u5ef6\u65f6\u65f6\u95f4\u7ee7\u7535\u5668(KT)\n(220V)", ComponentKind.ContactorCoil, new Color(0.9f, 0.94f, 0.96f), new Color(1f, 0.25f, 0.12f), false, false, CoilTerminals()));
            definitions.Add(CreateIndustrial("Timer_OffDelay_380V", "\u65ad\u7535\u5ef6\u65f6\u65f6\u95f4\u7ee7\u7535\u5668(KT)\n(380V)", ComponentKind.ContactorCoil, new Color(0.9f, 0.94f, 0.96f), new Color(1f, 0.25f, 0.12f), false, false, CoilTerminals()));
            definitions.Add(CreateIndustrial("SwitchPower_220V", "\u5f00\u5173\u7535\u6e90\n(220V)", ComponentKind.PowerSource, new Color(0.42f, 0.44f, 0.45f), new Color(0.12f, 0.45f, 1f), true, false,
                T("L", "L", TerminalRole.Phase, 0.2f, 0f, Red()), T("N", "N", TerminalRole.Neutral, 0.45f, 0f, Blue()), T("24V", "+", TerminalRole.Phase, 0.75f, 0f, Red()), T("0V", "-", TerminalRole.Neutral, 0.95f, 0f, Blue())));
            definitions.Add(CreateIndustrial("PLC_Output_24V", "PLC\u8f93\u51fa\u6a21\u5757\n(24V)", ComponentKind.TerminalBlock, new Color(0.2f, 0.32f, 0.32f), new Color(0.24f, 0.75f, 0.72f), true, false,
                T("Y0", "Y0", TerminalRole.Output, 0f, 0.75f, Red()), T("COM0", "COM", TerminalRole.Input, 1f, 0.75f, Blue()),
                T("Y1", "Y1", TerminalRole.Output, 0f, 0.5f, Red()), T("COM1", "COM", TerminalRole.Input, 1f, 0.5f, Blue()),
                T("Y2", "Y2", TerminalRole.Output, 0f, 0.25f, Red()), T("COM2", "COM", TerminalRole.Input, 1f, 0.25f, Blue())));
            definitions.Add(CreateTerminalStrip("TerminalBlock_6V", "\u0036\u4f4d\u7aef\u5b50\u7ad6\u6392", 6, true));
            definitions.Add(CreateTerminalStrip("TerminalBlock_3V", "\u0033\u4f4d\u7aef\u5b50\u7ad6\u6392", 3, true));
            definitions.Add(CreateTerminalStrip("TerminalBlock_2V", "\u0032\u4f4d\u7aef\u5b50\u7ad6\u6392", 2, true));
            definitions.Add(CreateTerminalStrip("TerminalBlock_6H", "\u0036\u4f4d\u7aef\u5b50\u6a2a\u6392", 6, false));
            definitions.Add(CreateTerminalStrip("TerminalBlock_3H", "\u0033\u4f4d\u7aef\u5b50\u6a2a\u6392", 3, false));
            definitions.Add(CreateTerminalStrip("TerminalBlock_2H", "\u0032\u4f4d\u7aef\u5b50\u6a2a\u6392", 2, false));
            definitions.Add(CreateIndustrial("StepperDriver_24V", "\u6b65\u8fdb\u9a71\u52a8\u5668\n(24V)", ComponentKind.TerminalBlock, new Color(0.14f, 0.18f, 0.2f), new Color(0.1f, 0.7f, 0.95f), true, false,
                T("PUL", "PUL", TerminalRole.Input, 1f, 0.75f, Yellow()), T("DIR", "DIR", TerminalRole.Input, 1f, 0.55f, Green()), T("ENA", "ENA", TerminalRole.Input, 1f, 0.35f, Red()), T("VCC", "+", TerminalRole.Phase, 1f, 0.15f, Red()), T("GND", "-", TerminalRole.Neutral, 1f, 0f, Blue())));
            definitions.Add(CreateIndustrial("StepperMotor_24V", "\u6b65\u8fdb\u7535\u673a\n(24V)", ComponentKind.Motor, new Color(0.18f, 0.23f, 0.28f), new Color(0.22f, 0.68f, 1f), false, false,
                T("A+", "A+", TerminalRole.Input, 1f, 0.75f, Red()), T("A-", "A-", TerminalRole.Input, 1f, 0.55f, Blue()), T("B+", "B+", TerminalRole.Input, 1f, 0.35f, Green()), T("B-", "B-", TerminalRole.Input, 1f, 0.15f, Yellow())));
            definitions.Add(CreateIndicator("Indicator_Red_220V", "\u7ea2\u8272\u6307\u793a\u706f\n(220V)", new Color(0.8f, 0.18f, 0.16f), Blue()));
            definitions.Add(CreateIndicator("Indicator_Green_220V", "\u7eff\u8272\u6307\u793a\u706f\n(220V)", new Color(0.18f, 0.58f, 0.28f), Blue()));
            definitions.Add(CreateIndicator("Indicator_Yellow_220V", "\u9ec4\u8272\u6307\u793a\u706f\n(220V)", new Color(0.72f, 0.62f, 0.2f), Blue()));
            definitions.Add(CreateIndicator("Indicator_Red_380V", "\u7ea2\u8272\u6307\u793a\u706f\n(380V)", new Color(0.8f, 0.18f, 0.16f), Red()));
            definitions.Add(CreateIndicator("Indicator_Green_380V", "\u7eff\u8272\u6307\u793a\u706f\n(380V)", new Color(0.18f, 0.58f, 0.28f), Red()));
            definitions.Add(CreateIndicator("Indicator_Yellow_380V", "\u9ec4\u8272\u6307\u793a\u706f\n(380V)", new Color(0.72f, 0.62f, 0.2f), Red()));
        }

        private static void AddMeasurementDefinitions(List<ComponentDefinition> definitions)
        {
            var multimeter = CreateDefinition("Tool_Multimeter", "万用表", ComponentKind.Instrument, new Color(0.92f, 0.95f, 1f), new Color(0.12f, 0.45f, 1f), false, false);
            multimeter.category = ComponentCategory.Measurement;
            multimeter.size = new Vector2(260f, 190f);
            multimeter.parameterNote = "教学测量工具：拖到画布后，点击元件查看估算电压、电流和功率。";
            EditorUtility.SetDirty(multimeter);

            var oscilloscope = CreateDefinition("Tool_Oscilloscope", "简易示波器", ComponentKind.Instrument, new Color(0.92f, 1f, 0.95f), new Color(0.08f, 0.65f, 0.25f), false, false);
            oscilloscope.category = ComponentCategory.Measurement;
            oscilloscope.size = new Vector2(300f, 210f);
            oscilloscope.parameterNote = "教学测量工具：拖到画布后，点击元件查看简化交流波形。";
            EditorUtility.SetDirty(oscilloscope);

            definitions.Add(multimeter);
            definitions.Add(oscilloscope);
        }

        private static ComponentDefinition CreateBreaker(string assetName, string displayName, int poles)
        {
            var terminals = new List<TerminalDefinition>();
            var colors = new[] { new Color(0.95f, 0.12f, 0.12f), new Color(0.1f, 0.35f, 0.95f), new Color(0.95f, 0.78f, 0.12f), new Color(0.08f, 0.65f, 0.25f) };

            for (var i = 0; i < poles; i++)
            {
                var x = (i + 1f) / (poles + 1f);
                var isNeutral = i == 1;
                terminals.Add(new TerminalDefinition { id = $"P{i + 1}_IN", label = $"进{i + 1}", role = isNeutral ? TerminalRole.Neutral : TerminalRole.Input, normalizedPosition = new Vector2(x, 1f), color = colors[i] });
                terminals.Add(new TerminalDefinition { id = $"P{i + 1}_OUT", label = $"出{i + 1}", role = isNeutral ? TerminalRole.Neutral : TerminalRole.Output, normalizedPosition = new Vector2(x, 0f), color = colors[i] });
            }

            return CreateDefinition(assetName, displayName, ComponentKind.Breaker, new Color(0.9f, 0.92f, 0.96f), new Color(0.15f, 0.42f, 0.92f), true, true, terminals.ToArray());
        }

        private static ComponentDefinition CreateIndustrial(string assetName, string displayName, ComponentKind kind, Color body, Color accent, bool startsClosed, bool togglable, params TerminalDefinition[] terminals)
        {
            var definition = CreateDefinition(assetName, displayName, kind, body, accent, startsClosed, togglable, terminals);
            definition.category = ComponentCategory.Industrial;
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static ComponentDefinition CreateTerminalStrip(string assetName, string displayName, int count, bool vertical)
        {
            var terminals = new List<TerminalDefinition>();
            for (var i = 0; i < count; i++)
            {
                var t = count == 1 ? 0.5f : i / (count - 1f);
                if (vertical)
                {
                    var y = Mathf.Lerp(0.88f, 0.12f, t);
                    terminals.Add(T($"L{i + 1}", $"{i + 1}A", TerminalRole.Input, 0.32f, y, Red()));
                    terminals.Add(T($"R{i + 1}", $"{i + 1}B", TerminalRole.Output, 0.68f, y, Red()));
                }
                else
                {
                    var x = Mathf.Lerp(0.12f, 0.88f, t);
                    terminals.Add(T($"T{i + 1}", $"{i + 1}A", TerminalRole.Input, x, 0.68f, Red()));
                    terminals.Add(T($"B{i + 1}", $"{i + 1}B", TerminalRole.Output, x, 0.32f, Red()));
                }
            }

            return CreateIndustrial(assetName, displayName, ComponentKind.TerminalBlock, new Color(0.16f, 0.17f, 0.18f), new Color(0.55f, 0.55f, 0.55f), true, false, terminals.ToArray());
        }

        private static ComponentDefinition CreateIndicator(string assetName, string displayName, Color lampColor, Color returnColor)
        {
            return CreateIndustrial(assetName, displayName, ComponentKind.Indicator, Color.Lerp(lampColor, Color.black, 0.25f), lampColor, false, false,
                T("L", "L", TerminalRole.Input, 0f, 0.45f, Red()),
                T("N", "N", TerminalRole.Neutral, 1f, 0.45f, returnColor));
        }

        private static TerminalDefinition[] ControlTerminals()
        {
            return new[]
            {
                T("11", "11", TerminalRole.Input, 0f, 0.65f, Red()),
                T("12", "12", TerminalRole.Output, 1f, 0.65f, Red()),
                T("23", "23", TerminalRole.Input, 0f, 0.25f, Green()),
                T("24", "24", TerminalRole.Output, 1f, 0.25f, Green())
            };
        }

        private static TerminalDefinition[] CoilTerminals()
        {
            return new[]
            {
                T("A1", "A1", TerminalRole.CoilA1, 0.15f, 0f, Red()),
                T("A2", "A2", TerminalRole.CoilA2, 0.85f, 0f, Blue())
            };
        }

        private static TerminalDefinition[] TimerRelayTerminals()
        {
            return new[]
            {
                T("A1", "A1", TerminalRole.CoilA1, 0.15f, 0f, Red()),
                T("A2", "A2", TerminalRole.CoilA2, 0.85f, 0f, Blue()),
                T("15", "15", TerminalRole.Input, 0f, 0.55f, Red()),
                T("16", "16", TerminalRole.Output, 1f, 0.65f, Red()),
                T("18", "18", TerminalRole.Output, 1f, 0.35f, Green())
            };
        }

        private static List<ComponentParameter> TimerDelayParameters()
        {
            return new List<ComponentParameter>
            {
                Parameter("delaySeconds", "\u5ef6\u65f6\u65f6\u95f4", 3f, "s", 0f, 999f, true)
            };
        }

        private static ComponentParameter Parameter(
            string key,
            string displayName,
            float value,
            string unit,
            float min,
            float max,
            bool editable)
        {
            return new ComponentParameter
            {
                key = key,
                displayName = displayName,
                value = value,
                unit = unit,
                min = min,
                max = max,
                editable = editable
            };
        }

        private static TerminalDefinition[] ContactorTerminals()
        {
            return new[]
            {
                T("L1", "1/L1", TerminalRole.Input, 0.18f, 1f, Yellow()),
                T("T1", "2/T1", TerminalRole.Output, 0.18f, 0f, Yellow()),
                T("L2", "3/L2", TerminalRole.Input, 0.5f, 1f, Green()),
                T("T2", "4/T2", TerminalRole.Output, 0.5f, 0f, Green()),
                T("L3", "5/L3", TerminalRole.Input, 0.82f, 1f, Red()),
                T("T3", "6/T3", TerminalRole.Output, 0.82f, 0f, Red()),
                T("A1", "A1", TerminalRole.CoilA1, 0f, 0.68f, Red()),
                T("A2", "A2", TerminalRole.CoilA2, 1f, 0.68f, Blue()),
                T("13", "13", TerminalRole.Input, 0f, 0.42f, Green()),
                T("14", "14", TerminalRole.Output, 1f, 0.42f, Green()),
                T("21", "21", TerminalRole.Input, 0f, 0.22f, Red()),
                T("22", "22", TerminalRole.Output, 1f, 0.22f, Red())
            };
        }

        private static TerminalDefinition[] ThermalRelayTerminals()
        {
            return new[]
            {
                T("L1", "L1", TerminalRole.Input, 0.2f, 1f, Yellow()), T("T1", "T1", TerminalRole.Output, 0.2f, 0f, Yellow()),
                T("L2", "L2", TerminalRole.Input, 0.5f, 1f, Green()), T("T2", "T2", TerminalRole.Output, 0.5f, 0f, Green()),
                T("L3", "L3", TerminalRole.Input, 0.8f, 1f, Red()), T("T3", "T3", TerminalRole.Output, 0.8f, 0f, Red()),
                T("95", "95", TerminalRole.Input, 0f, 0.62f, Red()), T("96", "96", TerminalRole.Output, 1f, 0.62f, Red()),
                T("97", "97", TerminalRole.Input, 0f, 0.34f, Green()), T("98", "98", TerminalRole.Output, 1f, 0.34f, Green())
            };
        }

        private static TerminalDefinition[] ThreePhasePairTerminals()
        {
            return new[]
            {
                T("L1", "L1", TerminalRole.Input, 0.2f, 1f, Yellow()), T("T1", "T1", TerminalRole.Output, 0.2f, 0f, Yellow()),
                T("L2", "L2", TerminalRole.Input, 0.5f, 1f, Green()), T("T2", "T2", TerminalRole.Output, 0.5f, 0f, Green()),
                T("L3", "L3", TerminalRole.Input, 0.8f, 1f, Red()), T("T3", "T3", TerminalRole.Output, 0.8f, 0f, Red())
            };
        }

        private static TerminalDefinition T(string id, string label, TerminalRole role, float x, float y, Color color)
        {
            return new TerminalDefinition { id = id, label = label, role = role, normalizedPosition = new Vector2(x, y), color = color };
        }

        private static Color Red() => new Color(0.95f, 0.12f, 0.12f);
        private static Color Blue() => new Color(0.1f, 0.35f, 0.95f);
        private static Color Green() => new Color(0.08f, 0.65f, 0.25f);
        private static Color Yellow() => new Color(0.95f, 0.78f, 0.12f);

        private static ComponentDefinition CreateDefinition(string assetName, string displayName, ComponentKind kind, Color body, Color accent, bool startsClosed, bool togglable, params TerminalDefinition[] terminals)
        {
            var path = $"{DataFolder}/{assetName}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<ComponentDefinition>(path);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<ComponentDefinition>();
                AssetDatabase.CreateAsset(definition, path);
            }

            definition.displayName = displayName;
            definition.category = ComponentCategory.Household;
            definition.kind = kind;
            definition.bodyColor = body;
            definition.accentColor = accent;
            definition.sprite = null;
            definition.size = new Vector2(110f, 130f);
            definition.startsClosed = startsClosed;
            definition.togglable = togglable;
            definition.allowSameComponentJumper = false;
            definition.terminals = new List<TerminalDefinition>(terminals);
            ConfigureElectricalProfile(definition, assetName, kind);
            ConfigureSupportProfile(definition, assetName, kind);
            ConfigureEditableParameters(definition, assetName);
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static void ConfigureSupportProfile(ComponentDefinition definition, string assetName, ComponentKind kind)
        {
            definition.supportLevel = ComponentSupportLevel.RuntimeSupported;
            definition.showInPalette = true;
            definition.unsupportedReason = string.Empty;
            definition.canParticipateInRuntime = true;
            definition.canParticipateInParameterCalculation = false;

            if (kind == ComponentKind.TerminalBlock)
            {
                definition.supportLevel = ComponentSupportLevel.ConnectorOnly;
                definition.unsupportedReason = "连接器元件，仅用于接线中转，不产生主动动作。";
                return;
            }

            if (assetName == "PLC_Output_24V")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现 PLC 输出点位和 24V 控制逻辑。");
                return;
            }

            if (assetName == "SwitchPower_220V")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现 AC 220V 转 DC 24V 输出传播模型。");
                return;
            }

            if (assetName == "SolenoidValve_24V")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现 24V 电磁阀动作模型。");
                return;
            }

            if (assetName == "StepperDriver_24V")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现 PUL / DIR / ENA 脉冲驱动逻辑。");
                return;
            }

            if (assetName == "StepperMotor_24V")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现步进电机 A+/A-/B+/B- 绕组与脉冲运动模型，不能按普通三相电机判断。");
                return;
            }

            if (assetName.Contains("Timer_OffDelay"))
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现断电延时触点模型。");
                return;
            }

            if (assetName == "Tool_Oscilloscope")
            {
                MarkVisualOnlyUnsupported(definition, "当前暂未实现示波器测量逻辑。");
                return;
            }

            if (assetName.Contains("Button_SelfLock") ||
                assetName == "LimitSwitch_SelfLock")
            {
                definition.supportLevel = ComponentSupportLevel.SimpleSwitchSupported;
                return;
            }

            if (assetName == "KnifeSwitch_QS" ||
                assetName == "EmergencyStop_NC" ||
                assetName == "Breaker_1P" ||
                assetName == "Breaker_4P" ||
                assetName == "Fuse_1P")
            {
                definition.supportLevel = ComponentSupportLevel.RuntimeSupported;
                return;
            }

            if (assetName == "Timer_OnDelay_220V" || assetName == "Timer_OnDelay_380V" ||
                assetName == "LimitSwitch_Compound" || assetName == "Motor_ThreePhase_380V")
            {
                definition.supportLevel = ComponentSupportLevel.DynamicRuntimeSupported;
                return;
            }

            if (assetName == "Contactor_KM_220V" || assetName == "Contactor_KM_380V")
            {
                definition.supportLevel = ComponentSupportLevel.RuntimeSupported;
                return;
            }

            if (assetName.StartsWith("Indicator_"))
            {
                definition.supportLevel = ComponentSupportLevel.RuntimeSupported;
            }
        }

        private static void ConfigureEditableParameters(ComponentDefinition definition, string assetName)
        {
            var parameters = new List<ComponentParameter>();

            if (assetName == "AC_220V_Power")
            {
                parameters.Add(Parameter("sourceVoltage", "电源电压", 220f, "V", 0f, 500f, true));
            }
            else if (assetName == "AC_ThreePhase_Power")
            {
                parameters.Add(Parameter("sourceVoltage", "相电压", 220f, "V", 0f, 500f, true));
                parameters.Add(Parameter("sourceLineVoltage", "线电压", 380f, "V", 0f, 1000f, true));
                parameters.Add(Parameter("sourcePhaseCount", "相数", 3f, string.Empty, 1f, 3f, false));
            }
            else if (assetName == "Lamp_220V")
            {
                parameters.Add(Parameter("ratedVoltage", "额定电压", 220f, "V", 0f, 500f, true));
                parameters.Add(Parameter("ratedPower", "额定功率", 60f, "W", 0f, 5000f, true));
                parameters.Add(Parameter("ratedCurrent", "额定电流", 0.27f, "A", 0f, 100f, false));
            }
            else if (assetName == "Fan_220V")
            {
                parameters.Add(Parameter("ratedVoltage", "额定电压", 220f, "V", 0f, 500f, true));
                parameters.Add(Parameter("ratedPower", "额定功率", 45f, "W", 0f, 5000f, true));
                parameters.Add(Parameter("ratedCurrent", "额定电流", 0.2f, "A", 0f, 100f, false));
            }
            else if (assetName == "Motor_ThreePhase_380V" || assetName == "Motor_StarDelta_380V")
            {
                parameters.Add(Parameter("ratedVoltage", "额定电压", 380f, "V", 0f, 1000f, true));
                parameters.Add(Parameter("ratedPower", "额定功率", 750f, "W", 0f, 100000f, true));
                parameters.Add(Parameter("ratedCurrent", "额定电流", 1.8f, "A", 0f, 500f, true));
                parameters.Add(Parameter("efficiency", "效率", 0.85f, string.Empty, 0.1f, 1f, true));
                parameters.Add(Parameter("powerFactor", "功率因数", 0.8f, string.Empty, 0.1f, 1f, true));
            }
            else if (assetName == "Contactor_KM_220V" || assetName == "Contactor_KM_380V")
            {
                var ratedVoltage = assetName.Contains("380V") ? 380f : 220f;
                var ratedPower = assetName.Contains("380V") ? 18f : 12f;
                var ratedCurrent = ratedPower / ratedVoltage;
                var maxVoltage = assetName.Contains("380V") ? 1000f : 500f;
                parameters.Add(Parameter("ratedVoltage", "线圈额定电压", ratedVoltage, "V", 0f, maxVoltage, true));
                parameters.Add(Parameter("ratedPower", "线圈额定功率", ratedPower, "W", 0f, 200f, true));
                parameters.Add(Parameter("ratedCurrent", "线圈额定电流", ratedCurrent, "A", 0f, 5f, false));
            }
            else if (assetName == "Timer_OnDelay_220V" || assetName == "Timer_OnDelay_380V")
            {
                var ratedVoltage = assetName.Contains("380V") ? 380f : 220f;
                var ratedPower = assetName.Contains("380V") ? 18f : 12f;
                var ratedCurrent = ratedPower / ratedVoltage;
                var maxVoltage = assetName.Contains("380V") ? 1000f : 500f;
                parameters.Add(Parameter("ratedVoltage", "线圈额定电压", ratedVoltage, "V", 0f, maxVoltage, true));
                parameters.Add(Parameter("ratedPower", "线圈额定功率", ratedPower, "W", 0f, 200f, true));
                parameters.Add(Parameter("ratedCurrent", "线圈额定电流", ratedCurrent, "A", 0f, 5f, false));
                parameters.Add(Parameter("delaySeconds", "延时时间", 3f, "s", 0f, 999f, true));
            }
            else if (assetName == "Breaker_1P" || assetName == "Breaker_2P" ||
                assetName == "Breaker_3P" || assetName == "Breaker_4P")
            {
                parameters.Add(Parameter("ratedCurrent", "额定电流", 16f, "A", 0f, 200f, true));
                parameters.Add(Parameter("canTrip", "可脱扣", 1f, string.Empty, 0f, 1f, false));
            }
            else if (assetName == "Fuse_1P" || assetName == "Fuse_3P")
            {
                parameters.Add(Parameter("ratedCurrent", "额定电流", assetName == "Fuse_3P" ? 10f : 6f, "A", 0f, 200f, true));
                parameters.Add(Parameter("canTrip", "可熔断", 1f, string.Empty, 0f, 1f, false));
            }
            else if (assetName == "ThermalRelay_FR_380V")
            {
                parameters.Add(Parameter("ratedCurrent", "额定电流", 4f, "A", 0f, 100f, true));
                parameters.Add(Parameter("settingCurrent", "整定电流", 1.8f, "A", 0f, 100f, true));
                parameters.Add(Parameter("canTrip", "可保护动作", 1f, string.Empty, 0f, 1f, false));
            }
            else if (assetName.StartsWith("Indicator_"))
            {
                var ratedVoltage = assetName.Contains("380V") ? 380f : 220f;
                var maxVoltage = assetName.Contains("380V") ? 1000f : 500f;
                parameters.Add(Parameter("ratedVoltage", "额定电压", ratedVoltage, "V", 0f, maxVoltage, true));
                parameters.Add(Parameter("ratedPower", "额定功率", 2f, "W", 0f, 100f, true));
                parameters.Add(Parameter("ratedCurrent", "额定电流", 2f / ratedVoltage, "A", 0f, 5f, false));
            }

            if (parameters.Count == 0)
            {
                return;
            }

            definition.parameters = parameters;
            definition.supportLevel = ComponentSupportLevel.ParameterCalculationSupported;
            definition.canParticipateInParameterCalculation = true;
        }

        private static void MarkVisualOnlyUnsupported(ComponentDefinition definition, string reason)
        {
            definition.supportLevel = ComponentSupportLevel.VisualOnly;
            definition.showInPalette = false;
            definition.unsupportedReason = reason;
            definition.canParticipateInRuntime = false;
            definition.canParticipateInParameterCalculation = false;
        }

        private static void ConfigureElectricalProfile(ComponentDefinition definition, string assetName, ComponentKind kind)
        {
            ResetElectricalProfile(definition);

            switch (kind)
            {
                case ComponentKind.PowerSource:
                    ConfigureSource(definition, assetName);
                    break;
                case ComponentKind.Lamp:
                    ConfigureLoad(definition, 220f, 60f, 250f, 0.5f, true, "教学估算：普通照明灯泡，电流约 P/U。");
                    break;
                case ComponentKind.Fan:
                    ConfigureLoad(definition, 220f, 45f, 250f, 0.6f, true, "教学估算：小型单相风扇，按额定功率估算电流。");
                    break;
                case ComponentKind.Indicator:
                    ConfigureIndicatorOrCoil(definition, assetName);
                    break;
                case ComponentKind.ContactorCoil:
                    ConfigureCoil(definition, assetName);
                    break;
                case ComponentKind.Motor:
                    ConfigureMotor(definition, assetName);
                    break;
                case ComponentKind.Breaker:
                    ConfigureProtector(definition, assetName.Contains("ThermalRelay") ? 4f : assetName.Contains("KnifeSwitch") ? 32f : 16f, false);
                    break;
                case ComponentKind.Fuse:
                    ConfigureProtector(definition, assetName.Contains("3P") ? 10f : 6f, true);
                    break;
                case ComponentKind.EnergyMeter:
                    definition.ratedVoltage = 220f;
                    definition.maxVoltage = 250f;
                    definition.ratedCurrent = 10f;
                    definition.maxCurrent = 40f;
                    definition.parameterNote = "单相电能表：当前用于导通与教学测量，不参与烧毁。";
                    break;
                case ComponentKind.Switch:
                case ComponentKind.TwoWaySwitch:
                case ComponentKind.PushButton:
                    definition.ratedVoltage = assetName.Contains("24V") ? 24f : 220f;
                    definition.ratedCurrent = 5f;
                    definition.maxCurrent = 10f;
                    definition.parameterNote = assetName.Contains("LimitSwitch") ? "ON = 被触碰，23-24 接通，11-12 断开；OFF = 未触碰，11-12 接通，23-24 断开。" : "开关/按钮：用于控制通断，后续可参与过流触点损坏判断。";
                    break;
            }
        }

        private static void ResetElectricalProfile(ComponentDefinition definition)
        {
            definition.sourceVoltage = 0f;
            definition.sourceLineVoltage = 0f;
            definition.sourcePhaseCount = 0;
            definition.ratedVoltage = 0f;
            definition.ratedPower = 0f;
            definition.ratedCurrent = 0f;
            definition.maxVoltage = 0f;
            definition.maxCurrent = 0f;
            definition.canBurnOut = false;
            definition.canTrip = false;
            definition.parameterNote = string.Empty;
        }

        private static void ConfigureSource(ComponentDefinition definition, string assetName)
        {
            if (assetName.Contains("ThreePhase"))
            {
                definition.sourceVoltage = 220f;
                definition.sourceLineVoltage = 380f;
                definition.sourcePhaseCount = 3;
                definition.parameterNote = "三相四线交流电源：相电压 220V，线电压 380V。";
                return;
            }

            if (assetName.Contains("SwitchPower"))
            {
                definition.sourceVoltage = 24f;
                definition.sourcePhaseCount = 1;
                definition.ratedVoltage = 220f;
                definition.ratedPower = 60f;
                definition.ratedCurrent = 0.27f;
                definition.maxVoltage = 250f;
                definition.maxCurrent = 1f;
                definition.canBurnOut = true;
                definition.parameterNote = "开关电源：输入 220V，简化输出 24V。";
                return;
            }

            definition.sourceVoltage = 220f;
            definition.sourcePhaseCount = 1;
            definition.parameterNote = "单相 220V 教学电源。";
        }

        private static void ConfigureLoad(ComponentDefinition definition, float voltage, float power, float maxVoltage, float maxCurrent, bool burnable, string note)
        {
            definition.ratedVoltage = voltage;
            definition.ratedPower = power;
            definition.ratedCurrent = voltage > 0f ? Mathf.Round(power / voltage * 100f) / 100f : 0f;
            definition.maxVoltage = maxVoltage;
            definition.maxCurrent = maxCurrent;
            definition.canBurnOut = burnable;
            definition.parameterNote = note;
        }

        private static void ConfigureIndicatorOrCoil(ComponentDefinition definition, string assetName)
        {
            if (assetName.Contains("SolenoidValve_24V"))
            {
                ConfigureLoad(definition, 24f, 12f, 30f, 0.8f, true, "24V 电磁阀：接入高电压时后续会判定烧毁。");
                return;
            }

            if (assetName.Contains("380V"))
            {
                ConfigureLoad(definition, 380f, 2f, 430f, 0.05f, true, "380V 指示灯：用于工业回路状态指示。");
                return;
            }

            ConfigureLoad(definition, 220f, 2f, 250f, 0.05f, true, "220V 指示灯：用于控制回路状态指示。");
        }

        private static void ConfigureCoil(ComponentDefinition definition, string assetName)
        {
            if (assetName.Contains("380V"))
            {
                ConfigureLoad(definition, 380f, 18f, 430f, 0.12f, true, "380V 线圈/继电器：电压匹配时吸合。");
                return;
            }

            ConfigureLoad(definition, 220f, 12f, 250f, 0.1f, true, "220V 线圈/继电器：电压匹配时吸合。");
        }

        private static void ConfigureMotor(ComponentDefinition definition, string assetName)
        {
            if (assetName.Contains("Stepper"))
            {
                ConfigureLoad(definition, 24f, 24f, 30f, 1.5f, true, "24V 步进电机：当前按低压执行元件处理。");
                return;
            }

            ConfigureLoad(definition, 380f, 750f, 430f, 3f, true, "三相异步电动机：教学估算 0.75kW，电流取近似值。");
            definition.ratedCurrent = 1.8f;
        }

        private static void ConfigureProtector(ComponentDefinition definition, float ratedCurrent, bool fuse)
        {
            definition.ratedVoltage = 380f;
            definition.ratedCurrent = ratedCurrent;
            definition.maxCurrent = ratedCurrent;
            definition.canTrip = true;
            definition.parameterNote = fuse ? "熔断器：后续过流时熔断并断路。" : "断路器/保护器：后续过流时跳闸并断路。";
        }

        private static void BuildEventSystem()
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static void BuildCamera()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cameraObject.GetComponent<Camera>().backgroundColor = new Color(0.96f, 0.98f, 1f);
        }

        private static void BuildCanvas(List<ComponentDefinition> definitions)
        {
            var canvasObject = new GameObject("AppCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(TopNavigationController), typeof(DemoUIController), typeof(BlueprintController), typeof(SaveLoadService), typeof(DemoRuntimeBootstrap), typeof(LoginController));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            var appRoot = CreateRect("MainAppRoot", canvasObject.transform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var navBar = CreatePanel("NavBar", appRoot, Color.white, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 82f));
            var logo = CreateText("Logo", navBar, "电工数字学生仿真系统", 26, TextAnchor.MiddleLeft);
            logo.resizeTextForBestFit = true;
            logo.resizeTextMinSize = 18;
            logo.resizeTextMaxSize = 26;
            SetRect(logo.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(390f, 54f));

            var simulationRoot = CreateRect("SimulationPage", appRoot, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var blueprintRoot = CreatePanel("BlueprintPage", appRoot, new Color(0.94f, 0.96f, 0.98f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -41f), new Vector2(0f, -82f));
            var encyclopediaRoot = CreatePanel("EncyclopediaPage", appRoot, new Color(0.96f, 0.98f, 1f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -41f), new Vector2(0f, -82f));
            var toolsRoot = CreatePanel("ToolsPage", appRoot, new Color(0.96f, 0.98f, 1f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -41f), new Vector2(0f, -82f));
            var emptyRoot = CreatePanel("EmptyPage", appRoot, new Color(0.96f, 0.98f, 1f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -41f), new Vector2(0f, -82f));
            var emptyTitle = CreateText("EmptyPageTitle", emptyRoot, "", 34, TextAnchor.MiddleCenter);
            SetRect(emptyTitle.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            simulationRoot.gameObject.SetActive(true);
            blueprintRoot.gameObject.SetActive(false);
            encyclopediaRoot.gameObject.SetActive(false);
            toolsRoot.gameObject.SetActive(false);
            emptyRoot.gameObject.SetActive(false);

            var topBar = CreatePanel("TopBar", simulationRoot, new Color(0.98f, 0.99f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -82f), new Vector2(0f, 84f));
            var palette = CreatePanel("Palette", simulationRoot, Color.white, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(0f, -83f), new Vector2(380f, -166f));
            var workspacePanel = CreatePanel("Workspace", simulationRoot, new Color(0.96f, 0.98f, 1f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(190f, -83f), new Vector2(-380f, -166f));
            workspacePanel.gameObject.AddComponent<RectMask2D>();

            var canvasContent = CreateRect("CanvasContent", workspacePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14000f, 9000f));
            var gridLayer = CreateRect("GridLayer", canvasContent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            gridLayer.gameObject.AddComponent<WorkspaceGrid>().raycastTarget = false;
            var wireLayer = CreateRect("WireLayer", canvasContent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var componentLayer = CreateRect("ComponentLayer", canvasContent, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            var logPanel = CreatePanel("ActionLogPanel", simulationRoot, Color.white, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 16f), new Vector2(348f, 180f));
            var logOutline = logPanel.gameObject.AddComponent<Outline>();
            logOutline.effectColor = new Color(0.90f, 0.91f, 0.92f, 1f);
            logOutline.effectDistance = new Vector2(1f, -1f);
            var actionLogScroll = logPanel.gameObject.AddComponent<ScrollRect>();
            actionLogScroll.horizontal = false;
            actionLogScroll.vertical = true;
            actionLogScroll.movementType = ScrollRect.MovementType.Clamped;
            actionLogScroll.scrollSensitivity = 28f;

            var logTitle = CreateText("ActionLogTitle", logPanel, "操作记录", 20, TextAnchor.MiddleLeft);
            logTitle.color = new Color(0.05f, 0.08f, 0.14f);
            logTitle.raycastTarget = false;
            SetRect(logTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -6f), new Vector2(-24f, 28f));

            var status = CreateText("CurrentStatus", logPanel, "家庭电路组件：拖拽元件到画布，点击端子接线。", 17, TextAnchor.MiddleLeft);
            status.color = new Color(0.12f, 0.32f, 0.64f);
            status.raycastTarget = false;
            status.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetRect(status.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(-24f, 32f));

            var logViewport = CreatePanel("ActionLogViewport", logPanel, Color.white, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-24f, -84f));
            logViewport.offsetMin = new Vector2(12f, 10f);
            logViewport.offsetMax = new Vector2(-12f, -74f);
            logViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;

            var actionLog = CreateText("ActionLogText", logViewport, "等待操作...", 15, TextAnchor.UpperLeft);
            actionLog.color = new Color(0.23f, 0.29f, 0.38f);
            actionLog.raycastTarget = false;
            actionLog.horizontalOverflow = HorizontalWrapMode.Wrap;
            actionLog.verticalOverflow = VerticalWrapMode.Overflow;
            var actionLogFitter = actionLog.gameObject.AddComponent<ContentSizeFitter>();
            actionLogFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            actionLogFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            SetRect(actionLog.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-12f, 0f));
            actionLogScroll.viewport = logViewport;
            actionLogScroll.content = actionLog.rectTransform;

            var workspace = workspacePanel.gameObject.AddComponent<WorkspaceController>();
            var wireManager = workspacePanel.gameObject.AddComponent<WireManager>();
            SetPrivate(workspace, "workspaceRect", workspacePanel);
            SetPrivate(workspace, "canvasContent", canvasContent);
            SetPrivate(workspace, "componentLayer", componentLayer);
            SetPrivate(workspace, "wireLayer", wireLayer);
            SetPrivate(workspace, "wireManager", wireManager);
            SetPrivate(workspace, "statusText", status);
            SetPrivate(workspace, "actionLogText", actionLog);
            SetPrivate(workspace, "actionLogScrollRect", actionLogScroll);

            var saveLoad = canvasObject.GetComponent<SaveLoadService>();
            saveLoad.Initialize(workspace, definitions);

            var blueprintCatalog = EnsureBlueprintCatalog();
            BuildNavigation(navBar, canvasObject.GetComponent<TopNavigationController>(), simulationRoot.gameObject, blueprintRoot.gameObject, encyclopediaRoot.gameObject, toolsRoot.gameObject, emptyRoot.gameObject, emptyTitle);
            BuildToolbar(topBar, canvasObject.GetComponent<DemoUIController>(), workspace, saveLoad);
            BuildQuickToolStrip(simulationRoot, canvasObject.GetComponent<DemoUIController>());
            BuildPalette(palette, workspace, definitions);
            BuildBlueprintPage(blueprintRoot, canvasObject.GetComponent<BlueprintController>(), canvasObject.GetComponent<TopNavigationController>(), blueprintCatalog);
            BuildEncyclopediaPage(encyclopediaRoot, definitions);
            BuildToolsPage(toolsRoot);
            BuildReferencePanel(simulationRoot, canvasObject.GetComponent<BlueprintController>());
            BuildLoginPage(canvasObject.transform, canvasObject.GetComponent<LoginController>(), appRoot.gameObject, navBar);

            var bootstrap = canvasObject.GetComponent<DemoRuntimeBootstrap>();
            SetPrivate(bootstrap, "workspace", workspace);
            SetPrivate(bootstrap, "catalog", definitions);
            SetPrivate(bootstrap, "buildStarterCircuit", true);
        }

        private static void BuildNavigation(RectTransform parent, TopNavigationController controller, GameObject simulationRoot, GameObject blueprintRoot, GameObject encyclopediaRoot, GameObject toolsRoot, GameObject emptyRoot, Text emptyTitle)
        {
            var names = new[] { "模拟电路", "图纸集", "仿真广场", "元器件百科", "常用工具", "个人中心" };
            var widths = new[] { 124f, 104f, 124f, 152f, 124f, 124f };
            var buttons = new List<Button>();
            var labels = new List<Text>();
            var x = 472f;

            for (var i = 0; i < names.Length; i++)
            {
                var width = widths[i];
                var button = CreateButton("Nav_" + i, parent, names[i], new Vector2(x + width * 0.5f, 0f), Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(width, 50f), 19, out var label);
                label.resizeTextMinSize = 13;
                label.resizeTextMaxSize = 19;
                buttons.Add(button);
                labels.Add(label);
                x += width + 14f;
            }

            SetPrivate(controller, "tabButtons", buttons);
            SetPrivate(controller, "tabLabels", labels);
            SetPrivate(controller, "simulationRoot", simulationRoot);
            SetPrivate(controller, "blueprintRoot", blueprintRoot);
            SetPrivate(controller, "encyclopediaRoot", encyclopediaRoot);
            SetPrivate(controller, "toolsRoot", toolsRoot);
            SetPrivate(controller, "emptyPageRoot", emptyRoot);
            SetPrivate(controller, "emptyPageTitle", emptyTitle);
        }

        private static void BuildToolbar(RectTransform parent, DemoUIController controller, WorkspaceController workspace, SaveLoadService saveLoad)
        {
            var buttons = new List<Button>();
            buttons.Add(CreateButton("StartSimulation", parent, "开始仿真", new Vector2(118f, 0f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(136f, 48f), 18));
            buttons.Add(CreateButton("ClearWires", parent, "删除所有线", new Vector2(278f, 0f), Color.white, new Color(0.95f, 0.12f, 0.12f), new Vector2(148f, 48f), 18));
            buttons.Add(CreateButton("ClearAll", parent, "清空画布", new Vector2(440f, 0f), Color.white, new Color(0.95f, 0.35f, 0.12f), new Vector2(136f, 48f), 18));
            buttons.Add(CreateButton("Save", parent, "保存图纸", Vector2.zero, Color.white, new Color(0.05f, 0.1f, 0.2f), new Vector2(138f, 48f), 18));
            buttons.Add(CreateButton("Load", parent, "导入图纸", Vector2.zero, Color.white, new Color(0.05f, 0.1f, 0.2f), new Vector2(138f, 48f), 18));
            SetRect(buttons[3].GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-318f, 0f), new Vector2(138f, 48f));
            SetRect(buttons[4].GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-158f, 0f), new Vector2(138f, 48f));

            var dropdownObject = new GameObject("WireStyleDropdown", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            dropdownObject.transform.SetParent(parent, false);
            SetRect(dropdownObject.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(622f, 0f), new Vector2(176f, 48f));
            dropdownObject.GetComponent<Image>().color = Color.white;
            var dropdown = dropdownObject.GetComponent<Dropdown>();
            dropdown.options = new List<Dropdown.OptionData> { new Dropdown.OptionData("自定义直角"), new Dropdown.OptionData("直线") };

            var colorButtons = new List<Button>();
            var colors = new[] { new Color(0.95f, 0.15f, 0.12f), new Color(0.1f, 0.45f, 0.95f), new Color(0.08f, 0.65f, 0.25f), new Color(0.95f, 0.78f, 0.12f) };
            for (var i = 0; i < colors.Length; i++)
            {
                colorButtons.Add(CreateButton("WireColor" + i, parent, "", new Vector2(814f + i * 48f, 0f), colors[i], Color.white, new Vector2(34f, 34f), 18));
            }

            SetPrivate(controller, "workspace", workspace);
            SetPrivate(controller, "saveLoadService", saveLoad);
            SetPrivate(controller, "startButton", buttons[0]);
            SetPrivate(controller, "clearWiresButton", buttons[1]);
            SetPrivate(controller, "clearAllButton", buttons[2]);
            SetPrivate(controller, "saveButton", buttons[3]);
            SetPrivate(controller, "loadButton", buttons[4]);
            SetPrivate(controller, "wireStyleDropdown", dropdown);
            SetPrivate(controller, "colorButtons", colorButtons);
        }

        private static void BuildLoginPage(Transform parent, LoginController controller, GameObject appRoot, RectTransform navBar)
        {
            var currentUser = CreateText("CurrentUser", navBar, "当前用户：未登录", 16, TextAnchor.MiddleRight);
            currentUser.color = new Color(0.25f, 0.32f, 0.42f);
            SetRect(currentUser.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-146f, 0f), new Vector2(260f, 42f));

            var logout = CreateButton("LogoutButton", navBar, "退出", Vector2.zero, Color.white, new Color(0.9f, 0.12f, 0.12f), new Vector2(72f, 40f), 16);
            SetRect(logout.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-56f, 0f), new Vector2(72f, 40f));

            var loginRoot = CreatePanel("LoginPage", parent, new Color(0.94f, 0.97f, 1f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var card = CreatePanel("LoginCard", loginRoot, Color.white, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 650f));

            var title = CreateText("LoginTitle", card, "电工数字学生仿真系统", 30, TextAnchor.MiddleCenter);
            title.fontStyle = FontStyle.Bold;
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -36f), new Vector2(-80f, 54f));

            var subtitle = CreateText("LoginSubtitle", card, "演示级账号登录，可注册和找回密码", 17, TextAnchor.MiddleCenter);
            subtitle.color = new Color(0.35f, 0.42f, 0.52f);
            SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(-80f, 36f));

            var message = CreateText("LoginMessage", card, "请输入账号密码登录系统。演示账号：admin / 123456", 16, TextAnchor.MiddleCenter);
            message.color = new Color(0.12f, 0.32f, 0.64f);
            message.horizontalOverflow = HorizontalWrapMode.Wrap;
            SetRect(message.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(-96f, 48f));

            var loginPanel = CreateRect("LoginForm", card, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var registerPanel = CreateRect("RegisterForm", card, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;
            var forgotPanel = CreateRect("ForgotPasswordForm", card, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).gameObject;

            var loginAccount = CreateInputField("LoginAccount", loginPanel.transform, "账号", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(390f, 48f));
            var loginPassword = CreateInputField("LoginPassword", loginPanel.transform, "密码", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -286f), new Vector2(390f, 48f));
            loginPassword.contentType = InputField.ContentType.Password;
            loginPassword.asteriskChar = '*';

            var loginButton = CreateButton("LoginButton", loginPanel.transform, "登录", new Vector2(0f, -28f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(390f, 50f), 18);
            SetRect(loginButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -362f), new Vector2(390f, 50f));
            var showRegister = CreateButton("ShowRegisterButton", loginPanel.transform, "注册账号", Vector2.zero, Color.white, new Color(0.12f, 0.45f, 1f), new Vector2(180f, 44f), 16);
            SetRect(showRegister.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-105f, -428f), new Vector2(180f, 44f));
            var showForgot = CreateButton("ShowForgotButton", loginPanel.transform, "忘记密码", Vector2.zero, Color.white, new Color(0.12f, 0.45f, 1f), new Vector2(180f, 44f), 16);
            SetRect(showForgot.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(105f, -428f), new Vector2(180f, 44f));

            var registerAccount = CreateInputField("RegisterAccount", registerPanel.transform, "新账号，至少 3 个字符", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -196f), new Vector2(390f, 46f));
            var registerPassword = CreateInputField("RegisterPassword", registerPanel.transform, "密码，至少 6 位", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -254f), new Vector2(390f, 46f));
            registerPassword.contentType = InputField.ContentType.Password;
            registerPassword.asteriskChar = '*';
            var registerConfirm = CreateInputField("RegisterConfirm", registerPanel.transform, "再次输入密码", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -312f), new Vector2(390f, 46f));
            registerConfirm.contentType = InputField.ContentType.Password;
            registerConfirm.asteriskChar = '*';
            var registerAnswer = CreateInputField("RegisterAnswer", registerPanel.transform, "安全答案，用于找回密码", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -370f), new Vector2(390f, 46f));
            var registerButton = CreateButton("RegisterButton", registerPanel.transform, "完成注册", Vector2.zero, new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(184f, 48f), 17);
            SetRect(registerButton.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-104f, -438f), new Vector2(184f, 48f));
            var registerBack = CreateButton("RegisterBackButton", registerPanel.transform, "返回登录", Vector2.zero, Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(184f, 48f), 17);
            SetRect(registerBack.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(104f, -438f), new Vector2(184f, 48f));

            var forgotAccount = CreateInputField("ForgotAccount", forgotPanel.transform, "账号", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(390f, 48f));
            var forgotAnswer = CreateInputField("ForgotAnswer", forgotPanel.transform, "安全答案", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -286f), new Vector2(390f, 48f));
            var forgotNewPassword = CreateInputField("ForgotNewPassword", forgotPanel.transform, "新密码，至少 6 位", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -352f), new Vector2(390f, 48f));
            forgotNewPassword.contentType = InputField.ContentType.Password;
            forgotNewPassword.asteriskChar = '*';
            var resetPassword = CreateButton("ResetPasswordButton", forgotPanel.transform, "重置密码", Vector2.zero, new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(184f, 48f), 17);
            SetRect(resetPassword.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-104f, -430f), new Vector2(184f, 48f));
            var forgotBack = CreateButton("ForgotBackButton", forgotPanel.transform, "返回登录", Vector2.zero, Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(184f, 48f), 17);
            SetRect(forgotBack.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(104f, -430f), new Vector2(184f, 48f));

            registerPanel.SetActive(false);
            forgotPanel.SetActive(false);

            SetPrivate(controller, "loginRoot", loginRoot.gameObject);
            SetPrivate(controller, "appRoots", new List<GameObject> { appRoot });
            SetPrivate(controller, "loginPanel", loginPanel);
            SetPrivate(controller, "registerPanel", registerPanel);
            SetPrivate(controller, "forgotPanel", forgotPanel);
            SetPrivate(controller, "loginAccountInput", loginAccount);
            SetPrivate(controller, "loginPasswordInput", loginPassword);
            SetPrivate(controller, "registerAccountInput", registerAccount);
            SetPrivate(controller, "registerPasswordInput", registerPassword);
            SetPrivate(controller, "registerConfirmInput", registerConfirm);
            SetPrivate(controller, "registerAnswerInput", registerAnswer);
            SetPrivate(controller, "forgotAccountInput", forgotAccount);
            SetPrivate(controller, "forgotAnswerInput", forgotAnswer);
            SetPrivate(controller, "forgotNewPasswordInput", forgotNewPassword);
            SetPrivate(controller, "messageText", message);
            SetPrivate(controller, "currentUserText", currentUser);
            SetPrivate(controller, "loginButton", loginButton);
            SetPrivate(controller, "showRegisterButton", showRegister);
            SetPrivate(controller, "showForgotButton", showForgot);
            SetPrivate(controller, "registerButton", registerButton);
            SetPrivate(controller, "registerBackButton", registerBack);
            SetPrivate(controller, "resetPasswordButton", resetPassword);
            SetPrivate(controller, "forgotBackButton", forgotBack);
            SetPrivate(controller, "logoutButton", logout);
        }

        private static void BuildToolsPage(RectTransform parent)
        {
            if (parent.GetComponent<CommonToolsPageController>() == null)
            {
                parent.gameObject.AddComponent<CommonToolsPageController>();
            }
        }

        private static void BuildQuickToolStrip(RectTransform parent, DemoUIController controller)
        {
            var strip = CreatePanel("CanvasQuickTools", parent, new Color(1f, 1f, 1f, 0.92f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, -18f), new Vector2(96f, 382f));
            var undo = CreateButton("QuickUndo", strip, "撤销", new Vector2(48f, 142f), Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(72f, 46f), 16);
            var redo = CreateButton("QuickRedo", strip, "重做", new Vector2(48f, 86f), Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(72f, 46f), 16);
            var del = CreateButton("QuickDelete", strip, "删除", new Vector2(48f, 30f), Color.white, new Color(0.95f, 0.12f, 0.12f), new Vector2(72f, 46f), 16);
            var clearWires = CreateButton("QuickClearWires", strip, "清线", new Vector2(48f, -26f), Color.white, new Color(0.95f, 0.35f, 0.12f), new Vector2(72f, 46f), 16);
            var clearAll = CreateButton("QuickClearAll", strip, "清空", new Vector2(48f, -82f), Color.white, new Color(0.95f, 0.35f, 0.12f), new Vector2(72f, 46f), 16);
            var lockButton = CreateButton("QuickLock", strip, "锁定", new Vector2(48f, -156f), new Color(0.94f, 0.96f, 0.98f), new Color(0.08f, 0.42f, 0.2f), new Vector2(72f, 50f), 16);

            SetPrivate(controller, "undoButton", undo);
            SetPrivate(controller, "redoButton", redo);
            SetPrivate(controller, "quickDeleteButton", del);
            SetPrivate(controller, "quickClearWiresButton", clearWires);
            SetPrivate(controller, "quickClearAllButton", clearAll);
            SetPrivate(controller, "lockButton", lockButton);
        }

        private static void BuildEncyclopediaPage(RectTransform parent, List<ComponentDefinition> definitions)
        {
            var controller = parent.gameObject.AddComponent<EncyclopediaController>();
            var title = CreateText("EncyclopediaTitle", parent, "元器件百科", 32, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -30f), new Vector2(320f, 54f));

            var subtitle = CreateText("EncyclopediaSubtitle", parent, "按 PC 端资料库方式整理现有家庭与工业电路元件，方便学生查找用途和端点。", 18, TextAnchor.MiddleLeft);
            subtitle.color = new Color(0.35f, 0.42f, 0.52f);
            SetRect(subtitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(360f, -34f), new Vector2(-520f, 48f));

            var sidebar = CreatePanel("EncyclopediaSidebar", parent, Color.white, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(28f, -92f), new Vector2(260f, -136f));
            var sidebarTitle = CreateText("SidebarTitle", sidebar, "分类", 22, TextAnchor.MiddleLeft);
            SetRect(sidebarTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(22f, -18f), new Vector2(-44f, 40f));

            var categoryNames = new[] { "开关按钮", "保护设备", "仪表电源", "用电设备", "控制设备", "端子与模块" };
            var categoryButtons = new List<Button>();
            var categoryLabels = new List<Text>();
            for (var i = 0; i < categoryNames.Length; i++)
            {
                var item = CreatePanel("SidebarCategory_" + i, sidebar, i == 0 ? new Color(0.89f, 0.94f, 1f) : new Color(0.96f, 0.98f, 1f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -72f - i * 58f), new Vector2(-36f, 44f));
                var button = item.gameObject.AddComponent<Button>();
                var label = CreateText("Text", item, categoryNames[i], 18, TextAnchor.MiddleLeft);
                label.color = i == 0 ? new Color(0.06f, 0.38f, 0.95f) : new Color(0.18f, 0.24f, 0.32f);
                SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(16f, 0f), new Vector2(-32f, 0f));
                categoryButtons.Add(button);
                categoryLabels.Add(label);
            }

            var viewport = CreatePanel("EncyclopediaViewport", parent, new Color(1f, 1f, 1f, 0.01f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(304f, -104f), new Vector2(-338f, -154f));
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = CreateRect("EncyclopediaContent", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1700f));
            var scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var y = -4f;
            var categorySections = new List<RectTransform>();
            foreach (var groupName in categoryNames)
            {
                var groupDefinitions = new List<ComponentDefinition>();
                foreach (var definition in definitions)
                {
                    if (definition != null && GetEncyclopediaGroup(definition) == groupName)
                    {
                        groupDefinitions.Add(definition);
                    }
                }

                if (groupDefinitions.Count == 0)
                {
                    continue;
                }

                var section = BuildEncyclopediaSection(content, groupName, groupDefinitions, y, out y);
                categorySections.Add(section);
            }

            content.sizeDelta = new Vector2(0f, Mathf.Max(1000f, Mathf.Abs(y) + 40f));
            SetPrivate(controller, "categoryButtons", categoryButtons);
            SetPrivate(controller, "categoryLabels", categoryLabels);
            SetPrivate(controller, "categorySections", categorySections);
            SetPrivate(controller, "content", content);
        }

        private static RectTransform BuildEncyclopediaSection(RectTransform content, string title, List<ComponentDefinition> definitions, float y, out float nextY)
        {
            const float cardWidth = 368f;
            const float cardHeight = 196f;
            const float gapX = 24f;
            const float gapY = 22f;
            var columns = 3;
            var rows = Mathf.CeilToInt(definitions.Count / (float)columns);
            var sectionHeight = 58f + rows * (cardHeight + gapY) + 24f;
            var section = CreateRect("EncyclopediaSection_" + title, content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(0f, sectionHeight));

            var sectionTitle = CreateText("SectionTitle", section, title, 24, TextAnchor.MiddleLeft);
            SetRect(sectionTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(12f, -2f), new Vector2(-24f, 42f));

            for (var i = 0; i < definitions.Count; i++)
            {
                var row = i / columns;
                var col = i % columns;
                var card = CreatePanel("EncyclopediaCard_" + title + "_" + i, section, Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f + col * (cardWidth + gapX), -58f - row * (cardHeight + gapY)), new Vector2(cardWidth, cardHeight));
                BuildEncyclopediaCard(card, definitions[i]);
            }

            nextY = y - sectionHeight;
            return section;
        }

        private static void BuildEncyclopediaCard(RectTransform card, ComponentDefinition definition)
        {
            var preview = CreatePanel("Preview", card, definition.sprite != null ? Color.white : definition.bodyColor, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(104f, 116f));
            var previewImage = preview.GetComponent<Image>();
            if (definition.sprite != null)
            {
                previewImage.sprite = definition.sprite;
                previewImage.preserveAspect = true;
            }

            var name = CreateText("Name", card, definition.displayName.Replace("\n", " "), 19, TextAnchor.MiddleLeft);
            name.fontStyle = FontStyle.Bold;
            SetRect(name.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(136f, -20f), new Vector2(-154f, 46f));

            var meta = CreateText("Meta", card, GetCategoryLabel(definition.category) + " / " + GetKindLabel(definition.kind), 15, TextAnchor.MiddleLeft);
            meta.color = new Color(0.35f, 0.42f, 0.52f);
            SetRect(meta.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(136f, -66f), new Vector2(-154f, 28f));

            var rating = CreateText("Rating", card, BuildRatingSummary(definition), 14, TextAnchor.UpperLeft);
            rating.color = new Color(0.1f, 0.35f, 0.62f);
            SetRect(rating.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(136f, 52f), new Vector2(-154f, 48f));

            var terminalText = CreateText("Terminals", card, "端点：" + BuildTerminalSummary(definition), 15, TextAnchor.UpperLeft);
            terminalText.color = new Color(0.18f, 0.24f, 0.32f);
            SetRect(terminalText.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(136f, 12f), new Vector2(-154f, 38f));
        }

        private static string BuildRatingSummary(ComponentDefinition definition)
        {
            if (definition.kind == ComponentKind.Instrument)
            {
                return "测量工具：拖到画布后使用";
            }

            if (definition.kind == ComponentKind.PowerSource)
            {
                if (definition.sourcePhaseCount >= 3)
                {
                    return $"输出：相电压 {definition.sourceVoltage:0}V / 线电压 {definition.sourceLineVoltage:0}V";
                }

                return definition.sourceVoltage > 0f ? $"输出：{definition.sourceVoltage:0}V" : "输出：待配置";
            }

            var parts = new List<string>();
            if (definition.ratedVoltage > 0f)
            {
                parts.Add($"Ue {definition.ratedVoltage:0}V");
            }

            if (definition.ratedPower > 0f)
            {
                parts.Add($"P {definition.ratedPower:0.#}W");
            }

            if (definition.ratedCurrent > 0f)
            {
                parts.Add($"Ie {definition.ratedCurrent:0.##}A");
            }

            if (definition.canTrip)
            {
                parts.Add("可跳闸/熔断");
            }
            else if (definition.canBurnOut)
            {
                parts.Add("可烧毁");
            }

            return parts.Count > 0 ? string.Join(" / ", parts) : "参数：暂不参与测量";
        }

        private static string BuildTerminalSummary(ComponentDefinition definition)
        {
            if (definition.terminals == null || definition.terminals.Count == 0)
            {
                return "无";
            }

            var parts = new List<string>();
            foreach (var terminal in definition.terminals)
            {
                parts.Add(terminal.label);
                if (parts.Count >= 6)
                {
                    break;
                }
            }

            var summary = string.Join("、", parts);
            if (definition.terminals.Count > parts.Count)
            {
                summary += " 等";
            }

            return summary;
        }

        private static string GetEncyclopediaGroup(ComponentDefinition definition)
        {
            switch (definition.kind)
            {
                case ComponentKind.Switch:
                case ComponentKind.TwoWaySwitch:
                case ComponentKind.PushButton:
                    return "开关按钮";
                case ComponentKind.Fuse:
                case ComponentKind.Breaker:
                    return "保护设备";
                case ComponentKind.PowerSource:
                case ComponentKind.EnergyMeter:
                    return "仪表电源";
                case ComponentKind.Lamp:
                case ComponentKind.Fan:
                case ComponentKind.Motor:
                case ComponentKind.Indicator:
                    return "用电设备";
                case ComponentKind.ContactorCoil:
                    return "控制设备";
                default:
                    return "端子与模块";
            }
        }

        private static string GetCategoryLabel(ComponentCategory category)
        {
            switch (category)
            {
                case ComponentCategory.Household:
                    return "家庭电路";
                case ComponentCategory.Industrial:
                    return "工业电路";
                case ComponentCategory.Measurement:
                    return "测量工具";
                default:
                    return "元件";
            }
        }

        private static string GetKindLabel(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.PowerSource:
                    return "电源";
                case ComponentKind.Switch:
                    return "开关";
                case ComponentKind.TwoWaySwitch:
                    return "双控开关";
                case ComponentKind.PushButton:
                    return "按钮";
                case ComponentKind.Fuse:
                    return "熔断器";
                case ComponentKind.Breaker:
                    return "断路器";
                case ComponentKind.EnergyMeter:
                    return "仪表";
                case ComponentKind.Lamp:
                    return "灯具";
                case ComponentKind.Fan:
                    return "风扇";
                case ComponentKind.Motor:
                    return "电机";
                case ComponentKind.ContactorCoil:
                    return "线圈/继电器";
                case ComponentKind.Indicator:
                    return "指示/执行";
                case ComponentKind.TerminalBlock:
                    return "端子/模块";
                case ComponentKind.Instrument:
                    return "测量工具";
                default:
                    return "元件";
            }
        }

        private static string GetBlueprintCategoryLabel(int category)
        {
            return category == 0 ? "工业" : "家庭";
        }

        private static string GetDifficultyLabel(int difficulty)
        {
            switch (difficulty)
            {
                case 0:
                    return "初级";
                case 1:
                    return "中级";
                case 2:
                    return "高级";
                default:
                    return "练习";
            }
        }

        private static Color GetDifficultyColor(int difficulty)
        {
            switch (difficulty)
            {
                case 0:
                    return new Color(0.10f, 0.62f, 0.32f);
                case 1:
                    return new Color(0.12f, 0.45f, 1f);
                case 2:
                    return new Color(0.86f, 0.36f, 0.10f);
                default:
                    return new Color(0.35f, 0.42f, 0.52f);
            }
        }

        private static void BuildBlueprintPage(RectTransform parent, BlueprintController controller, TopNavigationController navigation, List<BlueprintEntry> catalog)
        {
            var industrialCount = catalog.FindAll(item => item.Category == 0).Count;
            var homeCount = catalog.FindAll(item => item.Category == 1).Count;
            var primaryCount = catalog.FindAll(item => item.Category == 0 && item.Difficulty == 0).Count;
            var middleCount = catalog.FindAll(item => item.Category == 0 && item.Difficulty == 1).Count;
            var advancedCount = catalog.FindAll(item => item.Category == 0 && item.Difficulty == 2).Count;

            var categoryButtons = new List<Button>();
            var difficultyButtons = new List<Button>();
            var industrialFilter = CreateButton("IndustrialBlueprintFilter", parent, $"\u5de5\u4e1a\u7535\u8def\u56fe\u7eb8({industrialCount})", new Vector2(126f, -48f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(190f, 46f), 18);
            SetRect(industrialFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -24f), new Vector2(190f, 46f));
            var homeFilter = CreateButton("HomeBlueprintFilter", parent, $"\u5bb6\u5ead\u7535\u8def\u56fe\u7eb8({homeCount})", new Vector2(240f, -48f), new Color(0.94f, 0.96f, 0.98f), new Color(0.26f, 0.34f, 0.45f), new Vector2(190f, 46f), 18);
            SetRect(homeFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(234f, -24f), new Vector2(190f, 46f));
            categoryButtons.Add(industrialFilter);
            categoryButtons.Add(homeFilter);

            var allFilter = CreateButton("AllBlueprintFilter", parent, $"\u5168\u90e8\u56fe\u7eb8({industrialCount})", new Vector2(118f, -116f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(150f, 46f), 18);
            SetRect(allFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(34f, -94f), new Vector2(150f, 46f));
            var primaryFilter = CreateButton("PrimaryBlueprintFilter", parent, $"\u521d\u7ea7\u56fe\u7eb8({primaryCount})", new Vector2(200f, -116f), new Color(0.94f, 0.96f, 0.98f), new Color(0.26f, 0.34f, 0.45f), new Vector2(132f, 46f), 18);
            SetRect(primaryFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(196f, -94f), new Vector2(132f, 46f));
            var middleFilter = CreateButton("MiddleBlueprintFilter", parent, $"\u4e2d\u7ea7\u56fe\u7eb8({middleCount})", new Vector2(340f, -116f), new Color(0.94f, 0.96f, 0.98f), new Color(0.26f, 0.34f, 0.45f), new Vector2(132f, 46f), 18);
            SetRect(middleFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(340f, -94f), new Vector2(132f, 46f));
            var advancedFilter = CreateButton("AdvancedBlueprintFilter", parent, $"\u9ad8\u7ea7\u56fe\u7eb8({advancedCount})", new Vector2(480f, -116f), new Color(0.94f, 0.96f, 0.98f), new Color(0.26f, 0.34f, 0.45f), new Vector2(132f, 46f), 18);
            SetRect(advancedFilter.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(484f, -94f), new Vector2(132f, 46f));
            difficultyButtons.Add(allFilter);
            difficultyButtons.Add(primaryFilter);
            difficultyButtons.Add(middleFilter);
            difficultyButtons.Add(advancedFilter);

            var searchInput = CreateInputField("BlueprintSearch", parent, "输入图纸名称", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-34f, -46f), new Vector2(360f, 48f));

            var viewport = CreatePanel("BlueprintViewport", parent, new Color(1f, 1f, 1f, 0.01f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(0f, -152f));
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            var content = CreateRect("BlueprintContent", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1400f));
            var scroll = parent.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 34f;

            var buttons = new List<Button>();
            var cards = new List<GameObject>();
            var names = new List<string>();
            var sprites = new List<Sprite>();
            var recommendations = new List<string>();
            var categories = new List<int>();
            var difficulties = new List<int>();
            for (var i = 0; i < catalog.Count; i++)
            {
                var row = i / 4;
                var col = i % 4;
                var card = CreatePanel("BlueprintCard_" + i, content, Color.white, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(32f + col * 470f, -26f - row * 334f), new Vector2(440f, 300f));
                var cardButton = card.gameObject.AddComponent<Button>();
                buttons.Add(cardButton);
                cards.Add(card.gameObject);
                names.Add(catalog[i].Title);
                sprites.Add(catalog[i].Sprite);
                recommendations.Add(catalog[i].RecommendedComponents);
                categories.Add(catalog[i].Category);
                difficulties.Add(catalog[i].Difficulty);

                var preview = CreatePanel("BlueprintCardImage_" + i, card, Color.white, new Vector2(0f, 0.32f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(-24f, -24f));
                var previewImage = preview.GetComponent<Image>();
                previewImage.sprite = catalog[i].Sprite;
                previewImage.preserveAspect = true;

                var footer = CreatePanel("BlueprintCardFooter_" + i, card, new Color(0.98f, 0.99f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0.34f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
                footer.GetComponent<Image>().raycastTarget = false;
                var caption = CreateText("Caption", footer, catalog[i].Title, 19, TextAnchor.MiddleLeft);
                caption.fontStyle = FontStyle.Bold;
                SetRect(caption.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(16f, -10f), new Vector2(-32f, 38f));

                var categoryBadge = CreatePanel("CategoryBadge", footer, catalog[i].Category == 0 ? new Color(0.90f, 0.94f, 1f) : new Color(0.90f, 0.98f, 0.94f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 18f), new Vector2(74f, 28f));
                categoryBadge.GetComponent<Image>().raycastTarget = false;
                var categoryLabel = CreateText("Text", categoryBadge, GetBlueprintCategoryLabel(catalog[i].Category), 14, TextAnchor.MiddleCenter);
                categoryLabel.color = catalog[i].Category == 0 ? new Color(0.06f, 0.38f, 0.95f) : new Color(0.08f, 0.48f, 0.22f);
                SetRect(categoryLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

                var difficultyBadge = CreatePanel("DifficultyBadge", footer, GetDifficultyColor(catalog[i].Difficulty), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(98f, 18f), new Vector2(74f, 28f));
                difficultyBadge.GetComponent<Image>().raycastTarget = false;
                var difficultyLabel = CreateText("Text", difficultyBadge, GetDifficultyLabel(catalog[i].Difficulty), 14, TextAnchor.MiddleCenter);
                difficultyLabel.color = Color.white;
                SetRect(difficultyLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

                var practice = CreatePanel("PracticeBadge", footer, new Color(0.12f, 0.45f, 1f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 18f), new Vector2(104f, 32f));
                practice.GetComponent<Image>().raycastTarget = false;
                var practiceLabel = CreateText("Text", practice, "进入练习", 15, TextAnchor.MiddleCenter);
                practiceLabel.color = Color.white;
                SetRect(practiceLabel.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            }

            var modal = CreatePanel("BlueprintPreviewModal", parent, new Color(1f, 1f, 1f, 0.98f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            modal.gameObject.SetActive(false);
            var modalTitle = CreateText("PreviewTitle", modal, "", 30, TextAnchor.MiddleLeft);
            SetRect(modalTitle.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(30f, -24f), new Vector2(-80f, 48f));
            var close = CreateButton("PreviewClose", modal, "X", new Vector2(1840f, -30f), Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(54f, 42f), 22);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -18f), new Vector2(54f, 42f));
            var imageRect = CreatePanel("PreviewImage", modal, Color.white, new Vector2(0.08f, 0.16f), new Vector2(0.92f, 0.88f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var previewModalImage = imageRect.GetComponent<Image>();
            previewModalImage.preserveAspect = true;
            var cancel = CreateButton("PreviewCancel", modal, "\u53d6\u6d88", new Vector2(1520f, 44f), Color.white, new Color(0.05f, 0.08f, 0.14f), new Vector2(120f, 52f), 18);
            SetRect(cancel.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-210f, 36f), new Vector2(120f, 52f));
            var enter = CreateButton("EnterConfig", modal, "\u8fdb\u5165\u7535\u8def\u914d\u7f6e", new Vector2(1690f, 44f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(180f, 52f), 18);
            SetRect(enter.GetComponent<RectTransform>(), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-28f, 36f), new Vector2(180f, 52f));

            SetPrivate(controller, "navigation", navigation);
            SetPrivate(controller, "blueprintButtons", buttons);
            SetPrivate(controller, "blueprintCards", cards);
            SetPrivate(controller, "blueprintNames", names);
            SetPrivate(controller, "blueprintSprites", sprites);
            SetPrivate(controller, "blueprintRecommendations", recommendations);
            SetPrivate(controller, "blueprintCategories", categories);
            SetPrivate(controller, "blueprintDifficulties", difficulties);
            SetPrivate(controller, "categoryButtons", categoryButtons);
            SetPrivate(controller, "difficultyButtons", difficultyButtons);
            SetPrivate(controller, "searchInput", searchInput);
            SetPrivate(controller, "cardContent", content);
            SetPrivate(controller, "previewModal", modal.gameObject);
            SetPrivate(controller, "previewTitle", modalTitle);
            SetPrivate(controller, "previewImage", previewModalImage);
            SetPrivate(controller, "previewCloseButton", close);
            SetPrivate(controller, "previewCancelButton", cancel);
            SetPrivate(controller, "configureButton", enter);
        }

        private static void BuildReferencePanel(RectTransform simulationRoot, BlueprintController controller)
        {
            var panel = CreatePanel("BlueprintReferencePanel", simulationRoot, Color.white, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, -40f), new Vector2(560f, 560f));
            panel.gameObject.SetActive(false);
            var dragZoom = panel.gameObject.AddComponent<BlueprintReferencePanel>();
            var border = panel.GetComponent<Image>();
            border.color = Color.white;

            var title = CreateText("ReferenceTitle", panel, "", 22, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(22f, -30f), new Vector2(-252f, 46f));
            var zoomOut = CreateButton("ReferenceZoomOut", panel, "-", new Vector2(424f, -34f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(38f, 38f), 24);
            SetRect(zoomOut.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-164f, -18f), new Vector2(38f, 38f));
            var reset = CreateButton("ReferenceReset", panel, "1:1", new Vector2(472f, -34f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(48f, 38f), 18);
            SetRect(reset.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-116f, -18f), new Vector2(48f, 38f));
            var zoomIn = CreateButton("ReferenceZoomIn", panel, "+", new Vector2(520f, -34f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(38f, 38f), 24);
            SetRect(zoomIn.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-68f, -18f), new Vector2(38f, 38f));
            var close = CreateButton("ReferenceClose", panel, "X", new Vector2(520f, -34f), new Color(0.12f, 0.45f, 1f), Color.white, new Vector2(38f, 38f), 18);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-20f, -18f), new Vector2(38f, 38f));

            var imageRect = CreatePanel("ReferenceImage", panel, Color.white, new Vector2(0.04f, 0.12f), new Vector2(0.96f, 0.86f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            var image = imageRect.GetComponent<Image>();
            image.preserveAspect = true;

            var recommendations = CreateText("ReferenceRecommendations", panel, "", 16, TextAnchor.MiddleLeft);
            recommendations.horizontalOverflow = HorizontalWrapMode.Wrap;
            recommendations.verticalOverflow = VerticalWrapMode.Truncate;
            recommendations.color = new Color(0.24f, 0.30f, 0.38f);
            SetRect(recommendations.rectTransform, new Vector2(0.04f, 0.02f), new Vector2(0.96f, 0.11f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            SetPrivate(dragZoom, "panelRect", panel);
            SetPrivate(dragZoom, "zoomInButton", zoomIn);
            SetPrivate(dragZoom, "zoomOutButton", zoomOut);
            SetPrivate(dragZoom, "resetButton", reset);
            SetPrivate(controller, "referencePanel", panel.gameObject);
            SetPrivate(controller, "referenceTitle", title);
            SetPrivate(controller, "referenceImage", image);
            SetPrivate(controller, "referenceRecommendations", recommendations);
            SetPrivate(controller, "referenceCloseButton", close);
        }

        private static void BuildPalette(RectTransform palette, WorkspaceController workspace, List<ComponentDefinition> definitions)
        {
            var controller = palette.gameObject.AddComponent<PaletteController>();
            var title = CreateText("PaletteTitle", palette, "家庭电路组件", 24, TextAnchor.MiddleLeft);
            SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -24f), new Vector2(260f, 44f));
            title.text = "\u7535\u5de5\u63a7\u4ef6\u6c60";

            var searchInput = CreateInputField("PaletteSearch", palette, "搜索元件", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(-40f, 38f));

            var viewport = CreatePanel("PaletteViewport", palette, new Color(1f, 1f, 1f, 0.01f), Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(0f, -76f), new Vector2(0f, -152f));
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = CreateRect("PaletteContent", viewport, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(0f, 1600f));
            var scroll = palette.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 28f;

            var y = -10f;
            var sectionTitles = new List<RectTransform>();
            var itemRects = new List<RectTransform>();
            var itemNames = new List<string>();
            var itemCategories = new List<int>();
            y = BuildPaletteSection(content, workspace, definitions, ComponentCategory.Household, "\u5bb6\u5ead\u7535\u8def\u7ec4\u4ef6", y, sectionTitles, itemRects, itemNames, itemCategories);
            y = BuildPaletteSection(content, workspace, definitions, ComponentCategory.Industrial, "\u5de5\u4e1a\u7535\u8def\u7ec4\u4ef6", y - 8f, sectionTitles, itemRects, itemNames, itemCategories);
            y = BuildPaletteSection(content, workspace, definitions, ComponentCategory.Measurement, "测量工具", y - 8f, sectionTitles, itemRects, itemNames, itemCategories);
            content.sizeDelta = new Vector2(0f, Mathf.Max(900f, Mathf.Abs(y) + 24f));

            SetPrivate(controller, "workspace", workspace);
            SetPrivate(controller, "searchInput", searchInput);
            SetPrivate(controller, "content", content);
            SetPrivate(controller, "sectionTitles", sectionTitles);
            SetPrivate(controller, "itemRects", itemRects);
            SetPrivate(controller, "itemNames", itemNames);
            SetPrivate(controller, "itemCategories", itemCategories);
        }

        private static float BuildPaletteSection(RectTransform content, WorkspaceController workspace, List<ComponentDefinition> definitions, ComponentCategory category, string title, float y, List<RectTransform> sectionTitles, List<RectTransform> itemRects, List<string> itemNames, List<int> itemCategories)
        {
            var sectionTitle = CreateText("Section_" + category, content, title, 22, TextAnchor.MiddleLeft);
            SetRect(sectionTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, y), new Vector2(320f, 40f));
            sectionTitles.Add(sectionTitle.rectTransform);
            y -= 58f;

            var index = 0;
            foreach (var definition in definitions)
            {
                if (definition == null || definition.category != category || !definition.showInPalette)
                {
                    continue;
                }

                var row = index / 3;
                var col = index % 3;
                var item = CreatePaletteItem(content, workspace, definition, new Vector2(18f + col * 134f, y - row * 112f));
                itemRects.Add(item);
                itemNames.Add(definition.displayName);
                itemCategories.Add((int)category);
                index++;
            }

            var rows = Mathf.CeilToInt(index / 3f);
            return y - rows * 112f - 28f;
        }

        private static RectTransform CreatePaletteItem(RectTransform content, WorkspaceController workspace, ComponentDefinition definition, Vector2 position)
        {
            var item = CreatePanel("Palette_" + definition.name, content, new Color(0.94f, 0.96f, 0.98f), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), position, new Vector2(122f, 92f));
            item.gameObject.AddComponent<Button>();
            var label = CreateText("Label", item, definition.displayName, 14, TextAnchor.MiddleCenter);
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 10;
            label.resizeTextMaxSize = 14;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            SetRect(label.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -8f));
            var paletteItem = item.gameObject.AddComponent<PaletteItem>();
            paletteItem.Initialize(definition, workspace);
            return item;
        }

        private static RectTransform CreatePanel(string name, Transform parent, Color color, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            SetRect(rect, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            go.GetComponent<Image>().color = color;
            return rect;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            SetRect(rect, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            return rect;
        }

        private static Text CreateText(string name, Transform parent, string text, int size, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<Text>();
            label.text = text;
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = size;
            label.alignment = anchor;
            label.color = new Color(0.05f, 0.08f, 0.14f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(label.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(260f, 46f));
            return label;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 position, Color background, Color textColor, Vector2? size = null, int fontSize = 18)
        {
            return CreateButton(name, parent, label, position, background, textColor, size, fontSize, out _);
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 position, Color background, Color textColor, Vector2? size, int fontSize, out Text labelText)
        {
            var buttonRect = CreatePanel(name, parent, background, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), position, size ?? new Vector2(130f, 46f));
            var button = buttonRect.gameObject.AddComponent<Button>();
            labelText = null;
            if (!string.IsNullOrEmpty(label))
            {
                labelText = CreateText("Text", buttonRect, label, fontSize, TextAnchor.MiddleCenter);
                labelText.color = textColor;
                labelText.resizeTextForBestFit = true;
                labelText.resizeTextMinSize = 11;
                labelText.resizeTextMaxSize = fontSize;
                labelText.horizontalOverflow = HorizontalWrapMode.Wrap;
                labelText.verticalOverflow = VerticalWrapMode.Truncate;
                SetRect(labelText.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(-16f, -4f));
            }
            return button;
        }

        private static InputField CreateInputField(string name, Transform parent, string placeholder, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            var rect = CreatePanel(name, parent, Color.white, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
            var input = rect.gameObject.AddComponent<InputField>();

            var text = CreateText("Text", rect, "", 17, TextAnchor.MiddleLeft);
            text.color = new Color(0.05f, 0.08f, 0.14f);
            text.supportRichText = false;
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(-36f, 0f));

            var hint = CreateText("Placeholder", rect, placeholder, 17, TextAnchor.MiddleLeft);
            hint.color = new Color(0.48f, 0.56f, 0.68f);
            SetRect(hint.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), new Vector2(18f, 0f), new Vector2(-36f, 0f));

            input.textComponent = text;
            input.placeholder = hint;
            input.targetGraphic = rect.GetComponent<Image>();
            input.caretColor = new Color(0.12f, 0.45f, 1f);
            input.selectionColor = new Color(0.12f, 0.45f, 1f, 0.28f);
            return input;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void SetPrivate(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            field?.SetValue(target, value);
            EditorUtility.SetDirty((Object)target);
        }
    }
}
#endif


