using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>在场景 Inspector 勾选 noAds 后立即预览平台入口显隐，支持撤销和取消勾选恢复。</summary>
[CustomEditor(typeof(AdManager))]
public sealed class AdManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        AdManager manager = (AdManager)target;
        bool previous = manager.noAds;
        DrawDefaultInspector();
        if (manager.noAds != previous && Application.isPlaying) manager.ApplyNoAdsVisibility();
        if (manager.noAds != previous && !Application.isPlaying)
        {
            List<GameObject> targets = manager.GetNoAdsPlatformObjects();
            Undo.RecordObject(manager, "切换无广告链接模式");
            foreach (GameObject item in targets) Undo.RecordObject(item, "切换抖音预制体显隐");
            manager.ApplyNoAdsVisibility();
            EditorUtility.SetDirty(manager);
            PrefabUtility.RecordPrefabInstancePropertyModifications(manager);
            foreach (GameObject item in targets)
            {
                EditorUtility.SetDirty(item);
                PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
            if (manager.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        }
        if (manager.noAds)
        {
            EditorGUILayout.HelpBox("无广告模式：激励入口直接成功，插屏直接继续；标量使用 Unity PlayerPrefs，JSON 存到 persistentDataPath/noads-local.json。保留添加桌面、侧边栏、分享、排行榜四个按钮，点击直接执行成功或完成回调。侧边栏教学面板保持隐藏。保留体力和原奖励业务。\n设置后重新运行；发布普通链接请使用 Unity WebGL Build，不使用抖音 SDK 导出器。", MessageType.Info);
        }
    }
}
