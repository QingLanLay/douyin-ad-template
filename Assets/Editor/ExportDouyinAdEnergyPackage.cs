using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>按实际编辑器版本分别导出，禁止将同一份资源包改名冒充另一版本。</summary>
public static class ExportDouyinAdEnergyPackage
{
    private const string PackageName = "DouyinAdEnergySystem_v1.0.22_20261112";
    private static readonly string[] ExportRoots =
    {
        "Assets/DouYinIconDemo",
        "Assets/Font/DouyinSansBold SDF.asset",
        "Assets/Font/DouyinSansBold.otf",
        "Assets/Plugins/ByteGame",
        "Assets/UniTask",
        "Assets/Editor/DouyinUnity6000Compat",
        "Assets/Plugins/WebGL/DouyinUnity6FileAccess.jslib",
        "Assets/Plugins/WebGL/FeedPlaybackAudio.jslib",
        "Assets/Plugins/WebGL/DouyinUnity6ImplicitExit.jslib",
        "Assets/Editor/AdTestLifecyclePrefabBuilder.cs"
    };

    [Serializable]
    private sealed class ExportReport
    {
        public string packageName;
        public string editorVersion;
        public string targetUnity;
        public string uiPackage;
        public bool unity6000AutoCompatIncluded = true;
        public bool feedPlaybackControllerIncluded = true;
        public bool nestedFeedSubtitlePrefabVerified = true;
        public int checkedFunctionalPrefabs;
        public int checkedTMPTexts;
        public int reserializedAssets;
        public string status = "passed";
        public string boundary = "Editor import/compilation and functional prefab reference checks; not PlayMode, full WebGL/SDK build or device acceptance.";
    }

    [MenuItem("抖音/导出完整广告体力推荐流包（按当前Unity版本）")]
    public static void Export()
    {
        if (Application.unityVersion.StartsWith("2022.3.", StringComparison.Ordinal)) Export2022();
        else if (Application.unityVersion == "6000.0.46f1") Export6000();
        else throw new InvalidOperationException("未配置导出版本：" + Application.unityVersion);
    }

    [MenuItem("抖音/分别导出/2022版完整模板包")]
    public static void Export2022() { ExportVersion(false); }

    [MenuItem("抖音/分别导出/6000版完整模板包")]
    public static void Export6000() { ExportVersion(true); }

    private static void ExportVersion(bool unity6000)
    {
        try
        {
            if (unity6000 ? Application.unityVersion != "6000.0.46f1" : !Application.unityVersion.StartsWith("2022.3.", StringComparison.Ordinal))
                throw new InvalidOperationException("必须使用对应目标编辑器实际导出，当前：" + Application.unityVersion);
            ValidateDirectPlayScenes();
            foreach (string root in ExportRoots)
                if (AssetDatabase.LoadMainAssetAtPath(root) == null && !AssetDatabase.IsValidFolder(root))
                    throw new FileNotFoundException("导出路径不存在", root);
            string prefix = unity6000 ? "6000版" : "2022版";
            string fullName = prefix + PackageName;
            var report = new ExportReport
            {
                packageName = fullName,
                editorVersion = Application.unityVersion,
                targetUnity = unity6000 ? "6000.0.46f1" : "2022.3.62f1（含c1）",
                uiPackage = unity6000 ? "com.unity.ugui@2.0.0（内含TMP）" : "com.unity.ugui@1.0.0 + com.unity.textmeshpro@3.0.7"
            };
            string receiptPath = "Assets/DouYinIconDemo/版本与导入说明.txt";
            string receipt = prefix + "完整抖音广告模板\n包名：" + fullName + "\n目标Unity：" + report.targetUnity + "\n实际导出编辑器：" + report.editorVersion + "\n依赖：" + report.uiPackage + "\n请从对应Unity版本导入TMP Essential Resources。包不携带TMP默认资源或ProjectSettings。\n6000自动兼容插件随包保留，在2022不运行；SDK构建或仅转换后等待Console兼容完成提示。\n不要把6000版回导到2022。两个包是同一模板的不同Unity版本，请择一导入，不要叠加导入。\n历史Image/Share.prefab是原有缺少Spine依赖的旧示例；请使用Prefabs/AtomicButtons中的现用分享按钮。\nv1.0.22：推荐流配音使用抖音播放器。原生服务能力可用时使用TTAudioManager，覆盖异步音源创建、有效显示请求、同位置恢复和字幕同步。保留嵌套字幕预制体、独立字体及7001有限重试。\n激励/插屏测试面板（含倒计时）为AI生成图片，不含TMP组件；其他体力等界面仍按上述TMP依赖导入。\n真实广告使用noAds=false、isTest=false，并填写当前游戏自己的激励/插屏广告位；Unity编辑器不伪造真实观看成功。\n插屏保留启动30秒保护、全屏广告60秒间隔。\n本包不代表抖音平台或真机已验收。\n";
            File.WriteAllText(receiptPath, receipt.Replace("\n", Environment.NewLine));
            AssetDatabase.ImportAsset(receiptPath, ImportAssetOptions.ForceSynchronousImport);
            FeedPlaybackAssets.Prepare();
            if (unity6000)
            {
                string[] serialized = CollectManifestPaths().Where(p => new[] { ".prefab", ".asset", ".mat" }.Contains(Path.GetExtension(p))).ToArray();
                AssetDatabase.ForceReserializeAssets(serialized, ForceReserializeAssetsOptions.ReserializeAssets);
                AssetDatabase.SaveAssets();
                // 重序列化后同步导入，避免导出器读取尚未更新的 SourceAssetDB 时间戳。
                foreach (string asset in serialized)
                    AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                report.reserializedAssets = serialized.Length;
            }
            FeedPlaybackAssets.Validate();
            ValidateFunctionalPrefabs(report);
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = ReadOutputDirectory() ?? Path.Combine(projectRoot, "BuildPackages");
            Directory.CreateDirectory(outputDirectory);
            string packagePath = Path.Combine(outputDirectory, fullName + ".unitypackage");
            if (File.Exists(packagePath)) throw new IOException("目标文件已存在，不覆盖旧包：" + packagePath);
            AssetDatabase.ExportPackage(ExportRoots, packagePath, ExportPackageOptions.Recurse);
            if (!File.Exists(packagePath) || new FileInfo(packagePath).Length == 0)
                throw new IOException("Unity 未生成有效资源包，不写入成功报告：" + packagePath);
            File.WriteAllLines(Path.Combine(outputDirectory, fullName + "_manifest.txt"), CollectManifestPaths());
            File.WriteAllText(packagePath + ".validation.json", JsonUtility.ToJson(report, true));
            Debug.Log("[版本导出完成] " + packagePath + " | Unity " + Application.unityVersion + " | " + JsonUtility.ToJson(report));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }

    private static string ReadOutputDirectory()
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == "-douyinPackageOutput") return Path.GetFullPath(args[i + 1]);
        return null;
    }

    private static void ValidateFunctionalPrefabs(ExportReport report)
    {
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/DouYinIconDemo/Prefabs" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("无法加载功能预制体：" + path);
            foreach (Transform transform in prefab.GetComponentsInChildren<Transform>(true))
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                    throw new InvalidOperationException("功能预制体缺失脚本：" + path + " / " + transform.name);
            foreach (TMP_Text text in prefab.GetComponentsInChildren<TMP_Text>(true))
            {
                if (text.font == null || text.fontSharedMaterial == null || text.fontSharedMaterial.shader == null)
                    throw new InvalidOperationException("TMP引用未解析：" + path + " / " + text.name);
                report.checkedTMPTexts++;
            }
            report.checkedFunctionalPrefabs++;
        }
    }

    private static void ValidateDirectPlayScenes()
    {
        string[] accepted = { "3041", "023041", "103041", "993041" };
        string[] rejected = { null, "", "023040", "103042", "30410", "1001" };
        foreach (string scene in accepted)
            if (!FeedRecommendationManager.IsDirectPlayScene(scene)) throw new InvalidOperationException("直玩场景应被识别：" + scene);
        foreach (string scene in rejected)
            if (FeedRecommendationManager.IsDirectPlayScene(scene)) throw new InvalidOperationException("普通场景不应被识别为直玩：" + scene);
        Debug.Log("[导出检查] 多宿主直玩场景识别：10 项通过。");
    }

    private static string[] CollectManifestPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in ExportRoots)
        {
            paths.Add(root);
            if (AssetDatabase.IsValidFolder(root))
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { root })) paths.Add(AssetDatabase.GUIDToAssetPath(guid));
        }
        return paths.Where(p => !string.IsNullOrEmpty(p)).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
}
