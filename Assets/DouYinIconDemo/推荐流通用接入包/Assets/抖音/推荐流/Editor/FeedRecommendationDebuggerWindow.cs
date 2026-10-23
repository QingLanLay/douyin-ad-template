using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>推荐流调试：真实 GameView 镜像、横竖屏/宿主遮挡、分步生命周期和连续 ID 预览。</summary>
public sealed class FeedRecommendationDebuggerWindow : EditorWindow
{
    private const float DefaultCoveredFraction = .38f;

    [SerializeField] private FeedRecommendationConfig config;
    [SerializeField] private int selectedIndex;
    [SerializeField] private bool portrait = true;
    [SerializeField] private bool occlusion = true;
    [SerializeField] private bool guides;
    [SerializeField] private bool lightPortraitOverlay = true;
    [SerializeField] private bool sameChannel = true;
    [SerializeField] private bool loop = true;
    [SerializeField] private bool useRemote;
    [SerializeField] private bool showAfterLaunch = true;
    [SerializeField] private bool autoReady = true;
    [SerializeField] private float coveredFraction = DefaultCoveredFraction;
    [SerializeField] private bool pendingStart;
    [SerializeField] private FeedPreviewGameView gameView = new FeedPreviewGameView();
    private Vector2 controlsScroll;
    private Vector2 historyScroll;
    private double nextRepaint;

    [MenuItem("抖音/推荐流调试器")]
    private static void Open() => GetWindow<FeedRecommendationDebuggerWindow>("推荐流调试器");

    private void OnEnable()
    {
        minSize = new Vector2(920, 680);
        if (config == null) config = Resources.Load<FeedRecommendationConfig>(nameof(FeedRecommendationConfig));
        if (gameView == null) gameView = new FeedPreviewGameView();
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private void OnDisable()
    {
        EditorApplication.update -= Tick;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        gameView?.Restore();
        // 关闭工具时解除预览占用，不自动退出用户的 Play Mode。
        if (EditorApplication.isPlaying && !EditorApplication.isCompiling)
            FeedRecommendationManager.ExistingInstance?.DebugEndPreview();
    }

    private void Tick()
    {
        if (EditorApplication.timeSinceStartup < nextRepaint) return;
        nextRepaint = EditorApplication.timeSinceStartup + .1;
        Repaint();
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && pendingStart)
            EditorApplication.delayCall += () => { if (this != null && EditorApplication.isPlaying && pendingStart) { pendingStart = false; StartSelected(); } };
        if (state == PlayModeStateChange.EnteredEditMode) { pendingStart = false; gameView.Restore(); }
    }

    private IReadOnlyList<FeedContentConfig> Contents => config == null ? null : config.Contents;
    private FeedContentConfig Selected => Contents != null && selectedIndex >= 0 && selectedIndex < Contents.Count ? Contents[selectedIndex] : null;
    private FeedRecommendationManager Manager => EditorApplication.isPlaying ? FeedRecommendationManager.ExistingInstance : null;

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(390)))
            {
                controlsScroll = EditorGUILayout.BeginScrollView(controlsScroll);
                DrawControls();
                EditorGUILayout.EndScrollView();
            }
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("推荐流获客 · 抖音宿主遮挡预览", EditorStyles.boldLabel);
                Rect area = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                Texture texture = EditorApplication.isPlaying ? gameView.ReadFrame() : null;
                FeedPreviewOverlay.Draw(area, texture, portrait, occlusion, lightPortraitOverlay, guides, coveredFraction, Manager, Selected, PreviewNext);
                EditorGUILayout.HelpBox("镜像包含真实游戏 UI。游戏操作在 Game 窗口进行；这里的「立即去玩 / 下一条」直接驱动模拟生命周期。遮挡按截图近似，不是官方精确安全区。", MessageType.None);
                if (!string.IsNullOrEmpty(gameView.Error)) EditorGUILayout.HelpBox(gameView.Error, MessageType.Warning);
            }
        }
    }

    private void DrawControls()
    {
        EditorGUILayout.LabelField("1. 内容与预览方向", EditorStyles.boldLabel);
        config = (FeedRecommendationConfig)EditorGUILayout.ObjectField("配置", config, typeof(FeedRecommendationConfig), false);
        IReadOnlyList<FeedContentConfig> items = Contents;
        if (items == null || items.Count == 0)
        {
            EditorGUILayout.HelpBox("请在配置中填写至少一条有效 ContentID。SIM_ 为编辑器占位，不可替代平台真实 ID。", MessageType.Warning);
            if (GUILayout.Button("重新加载 Resources 配置")) config = Resources.Load<FeedRecommendationConfig>(nameof(FeedRecommendationConfig));
            return;
        }
        var labels = new string[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            FeedContentConfig item = items[i];
            labels[i] = item == null ? i + ": <空配置>" : i + ": " + (item.channel == FeedUserChannel.Acquisition ? "获客" : "复访") + " / " + item.contentId;
        }
        selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
        selectedIndex = EditorGUILayout.Popup("预览 ContentID", selectedIndex, labels);
        using (new EditorGUI.DisabledScope(Selected == null))
        {
            if (Selected != null)
            {
                EditorGUILayout.LabelField("方案 / 业务场景", Selected.scheme + " / " + Selected.returnSceneId);
                EditorGUILayout.SelectableLabel(Selected.payload ?? string.Empty, EditorStyles.textField, GUILayout.Height(20));
            }
        }
        EditorGUI.BeginChangeCheck();
        int orientation = EditorGUILayout.Popup("游戏方向", portrait ? 0 : 1, new[] { "竖屏 1179 × 2556", "横屏 2556 × 1179" });
        if (EditorGUI.EndChangeCheck()) { portrait = orientation == 0; gameView.Apply(portrait); }
        occlusion = EditorGUILayout.Toggle("模拟抖音遮挡", occlusion);
        using (new EditorGUI.DisabledScope(!portrait))
            lightPortraitOverlay = EditorGUILayout.Toggle("竖屏浅色遮挡", lightPortraitOverlay);
        guides = EditorGUILayout.Toggle("显示遮挡参考线", guides);
        using (new EditorGUILayout.HorizontalScope())
        {
            coveredFraction = EditorGUILayout.Slider("底部遮挡比例", coveredFraction, .25f, .55f);
            if (GUILayout.Button("恢复默认值", GUILayout.Width(90)))
            {
                coveredFraction = DefaultCoveredFraction;
                Repaint();
            }
        }
        EditorGUILayout.LabelField(portrait ? "卡片：竖屏画面 + 上下宿主遮挡" : "卡片：竖屏容器 + 横屏等比留黑；进入游戏后横屏", EditorStyles.wordWrappedMiniLabel);
        useRemote = EditorGUILayout.Toggle("按正式逻辑请求远程", useRemote);
        if (!useRemote) EditorGUILayout.LabelField("默认精确预览所选本地资产，不被服务器配置覆盖。", EditorStyles.wordWrappedMiniLabel);
        showAfterLaunch = EditorGUILayout.Toggle("启动后自动 OnShow", showAfterLaunch);
        autoReady = EditorGUILayout.Toggle("资源就绪后自动 7001", autoReady);
        using (new EditorGUI.DisabledScope(!FeedPreviewSequence.IsValid(Selected) || EditorApplication.isCompiling))
        {
            if (GUILayout.Button(EditorApplication.isPlaying ? "一键预览所选 ID" : "进入 Play 并预览所选 ID", GUILayout.Height(30))) RequestStart();
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            sameChannel = GUILayout.Toggle(sameChannel, "仅同渠道下一条");
            loop = GUILayout.Toggle(loop, "末尾循环");
        }
        int next = FeedPreviewSequence.FindNextIndex(items, selectedIndex, sameChannel, loop);
        using (new EditorGUI.DisabledScope(next < 0 || EditorApplication.isCompiling))
            if (GUILayout.Button("一键预览下一 ID", GUILayout.Height(28))) PreviewNext();
        if (next < 0) EditorGUILayout.LabelField("没有下一条有效且不同的 ID（空项/重复 ID 会跳过）。", EditorStyles.wordWrappedMiniLabel);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("2. 分步生命周期", EditorStyles.boldLabel);
        FeedRecommendationManager manager = Manager;
        using (new EditorGUI.DisabledScope(manager == null || !manager.IsEditorPreview))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("资源就绪 / 上报 7001")) manager.DebugSimulateSceneReady();
                if (GUILayout.Button("结束预览")) { manager.DebugEndPreview(); gameView.Restore(); }
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("卡片 OnShow")) manager.DebugSimulateShow();
                if (GUILayout.Button("卡片 OnHide")) manager.DebugSimulateHide();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("FeedEnter · 完整游戏")) manager.DebugSimulateEnter();
                if (GUILayout.Button("FeedExit · 退出")) manager.DebugSimulateExit();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("完整游戏切后台")) manager.DebugSimulateHide();
                if (GUILayout.Button("完整游戏回前台")) manager.DebugSimulateShow();
            }
            if (GUILayout.Button("退出完整游戏并返回卡片")) { manager.DebugSimulateExit(); manager.DebugSimulateShow(); }
        }
        if (manager != null)
        {
            if (Selected != null && manager.CurrentContext != null &&
                (manager.CurrentContext.Channel != Selected.channel || manager.CurrentContext.ContentId != Selected.contentId))
                EditorGUILayout.HelpBox("所选 ID 与实际上下文不同，请点击一键预览；远程兜底时以当前上下文为准。", MessageType.Warning);
            EditorGUILayout.LabelField("状态", manager.LifecycleStage.ToString());
            EditorGUILayout.LabelField("会话 / 配置解析", manager.LaunchVersion + " / " + (manager.IsResolvingConfiguration ? "进行中" : "完成"));
            EditorGUILayout.LabelField("7001 / 卡片 / 完整游戏", (manager.IsSceneReady ? "已上报" : "未上报") + " / " + manager.IsCardVisible + " / " + manager.IsInFullGame);
            if (manager.DebugReadinessPending) EditorGUILayout.HelpBox("首帧已经准备完成，当前等待你点击 7001。", MessageType.Info);
            EditorGUILayout.SelectableLabel(manager.CurrentContext == null ? "等待上下文" : manager.CurrentContext.ToString(), EditorStyles.textArea, GUILayout.Height(58));
            if (!string.IsNullOrEmpty(manager.LastConfigurationFailure)) EditorGUILayout.HelpBox(manager.LastConfigurationFailure, MessageType.Warning);
        }
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("3. 生命周期时间线（最近 120 条）", EditorStyles.boldLabel);
        if (manager != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("复制日志"))
                {
                    var lines = new List<string>();
                    foreach (FeedLifecycleRecord item in manager.LifecycleHistory) lines.Add(item.ToString());
                    EditorGUIUtility.systemCopyBuffer = string.Join("\n", lines);
                }
                if (GUILayout.Button("清空日志")) manager.DebugClearLifecycleHistory();
            }
            historyScroll = EditorGUILayout.BeginScrollView(historyScroll, GUILayout.Height(200));
            for (int i = manager.LifecycleHistory.Count - 1; i >= 0; i--)
                EditorGUILayout.LabelField(manager.LifecycleHistory[i].ToString(), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndScrollView();
        }
    }

    private void RequestStart()
    {
        if (!FeedPreviewSequence.IsValid(Selected)) return;
        gameView.Apply(portrait);
        if (!EditorApplication.isPlaying)
        {
            pendingStart = true;
            EditorApplication.isPlaying = true;
            return;
        }
        StartSelected();
    }

    private void StartSelected()
    {
        if (!EditorApplication.isPlaying || !FeedPreviewSequence.IsValid(Selected)) return;
        gameView.Apply(portrait);
        FeedRecommendationManager.Instance.DebugPreviewContent(config, Selected, useRemote, showAfterLaunch, autoReady);
        Repaint();
    }

    private void PreviewNext()
    {
        int next = FeedPreviewSequence.FindNextIndex(Contents, selectedIndex, sameChannel, loop);
        if (next < 0) return;
        selectedIndex = next;
        RequestStart();
    }
}
