using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 后台导出完整抖音广告、体力及推荐流通用接入包。
/// </summary>
public static class ExportDouyinAdEnergyPackage
{
    private const string PackageName = "DouyinAdEnergySystem_v1.0.15_20261023";

    private static readonly string[] ExportRoots =
    {
        "Assets/DouYinIconDemo",
        "Assets/Font/DouyinSansBold SDF.asset",
        "Assets/Font/DouyinSansBold.otf",
        "Assets/Plugins/ByteGame",
        "Assets/UniTask"
    };

    [MenuItem("抖音/导出完整广告体力推荐流包")]
    public static void Export()
    {
        try
        {
            ValidateDirectPlayScenes();
            foreach (string root in ExportRoots)
            {
                if (AssetDatabase.LoadMainAssetAtPath(root) == null && !AssetDatabase.IsValidFolder(root))
                    throw new FileNotFoundException("导出路径不存在", root);
            }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputDirectory = Path.Combine(projectRoot, "BuildPackages");
            Directory.CreateDirectory(outputDirectory);
            string packagePath = Path.Combine(outputDirectory, PackageName + ".unitypackage");
            string manifestPath = Path.Combine(outputDirectory, PackageName + "_manifest.txt");

            AssetDatabase.ExportPackage(ExportRoots, packagePath, ExportPackageOptions.Recurse);
            File.WriteAllLines(manifestPath, CollectManifestPaths());
            Debug.Log($"[导出完成] {packagePath}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            throw;
        }
    }

    private static void ValidateDirectPlayScenes()
    {
        // 未知宿主前缀仅用于测试，不表示平台已开放该宿主。
        string[] accepted = { "3041", "023041", "103041", "993041" };
        string[] rejected = { null, "", "023040", "103042", "30410", "1001" };
        foreach (string scene in accepted)
            if (!FeedRecommendationManager.IsDirectPlayScene(scene))
                throw new InvalidOperationException("直玩场景应被识别：" + scene);
        foreach (string scene in rejected)
            if (FeedRecommendationManager.IsDirectPlayScene(scene))
                throw new InvalidOperationException("普通场景不应被识别为直玩：" + scene);
        Debug.Log("[导出检查] 多宿主直玩场景识别：10 项通过。");
    }

    private static string[] CollectManifestPaths()
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in ExportRoots)
        {
            if (AssetDatabase.IsValidFolder(root))
            {
                paths.Add(root);
                foreach (string guid in AssetDatabase.FindAssets(string.Empty, new[] { root }))
                    paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            else
            {
                paths.Add(root);
            }
        }

        return paths.Where(path => !string.IsNullOrEmpty(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
    }
}
