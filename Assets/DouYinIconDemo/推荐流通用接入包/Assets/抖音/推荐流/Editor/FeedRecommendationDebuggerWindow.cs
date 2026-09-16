using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class FeedRecommendationDebuggerWindow : EditorWindow
{
    private FeedRecommendationConfig config;
    private int selectedIndex;

    [MenuItem("抖音/推荐流调试器")]
    private static void Open() => GetWindow<FeedRecommendationDebuggerWindow>("推荐流调试器");

    private void OnEnable() => LoadDefaultConfig();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("进入 Play Mode 后，分别模拟启动、卡片 OnShow/OnHide 和完整游戏 FeedEnter/FeedExit。", MessageType.Info);
        config = (FeedRecommendationConfig)EditorGUILayout.ObjectField("配置", config, typeof(FeedRecommendationConfig), false);
        IReadOnlyList<FeedContentConfig> contents = config == null ? null : config.Contents;
        if (contents == null || contents.Count == 0)
        {
            EditorGUILayout.HelpBox("请创建并填写 Resources/FeedRecommendationConfig.asset。", MessageType.Warning);
            if (GUILayout.Button("重新加载 Resources 配置")) LoadDefaultConfig();
            return;
        }

        string[] labels = new string[contents.Count];
        for (int i = 0; i < contents.Count; i++)
        {
            FeedContentConfig item = contents[i];
            labels[i] = item == null ? $"{i}: <空配置>" : $"{i}: {item.channel} / {item.contentId} / {item.scheme}";
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, contents.Count - 1);
        selectedIndex = EditorGUILayout.Popup("模拟内容", selectedIndex, labels);
        FeedContentConfig selected = contents[selectedIndex];
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || selected == null || string.IsNullOrWhiteSpace(selected.contentId)))
        {
            if (GUILayout.Button("1. 模拟启动（SchemePrepared）"))
                FeedRecommendationManager.Instance.DebugSimulateLaunch(selected.channel, selected.contentId, selected.returnSceneId);
            if (GUILayout.Button("2. 模拟卡片 OnShow"))
                FeedRecommendationManager.Instance.DebugSimulateShow();
            if (GUILayout.Button("3. 模拟卡片 OnHide"))
                FeedRecommendationManager.Instance.DebugSimulateHide();
            if (GUILayout.Button("4. 模拟 FeedEnter（进入完整小游戏）"))
                FeedRecommendationManager.Instance.DebugSimulateEnter();
            if (GUILayout.Button("5. 模拟 FeedExit（退出完整小游戏）"))
                FeedRecommendationManager.Instance.DebugSimulateExit();
        }

        if (!EditorApplication.isPlaying) EditorGUILayout.HelpBox("请先进入 Play Mode。", MessageType.Warning);
        FeedLaunchContext context = EditorApplication.isPlaying ? FeedRecommendationManager.Instance.CurrentContext : null;
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("当前上下文", EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(context == null ? "尚未模拟启动" : context.ToString(), EditorStyles.textArea, GUILayout.MinHeight(42));
    }

    private void LoadDefaultConfig()
    {
        config = Resources.Load<FeedRecommendationConfig>(nameof(FeedRecommendationConfig));
    }
}
