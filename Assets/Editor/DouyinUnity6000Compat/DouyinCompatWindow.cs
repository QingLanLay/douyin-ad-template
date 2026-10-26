#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace DouyinUnity6000Compat
{
    /// <summary>2022维护、6000自动兼容的编辑器入口，不创建游戏运行UI。</summary>
    public sealed class DouyinCompatWindow : EditorWindow
    {
        private string output, source, error;
        [MenuItem("Tools/抖音小游戏/6000自动兼容")]
        private static void OpenWindow() { GetWindow<DouyinCompatWindow>("抖音自动兼容").Show(); }
        private void OnEnable()
        { output = DouyinCompatService.OutputOverride; source = DouyinCompatService.SourceOverride; }
        private void OnInspectorUpdate() { Repaint(); }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("抖音小游戏 · 随包自动兼容", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("维护版本", "2022.3.62f1 / 2022.3.62f1c1");
            EditorGUILayout.LabelField("目标版本", DouyinCompatService.UnityVersion);
            EditorGUILayout.LabelField("当前Unity", Application.unityVersion);
            EditorGUILayout.LabelField("SDK版本", DouyinCompatService.DetectSdkVersion() + "（只记录，不锁定）");
            bool enabled = EditorGUILayout.Toggle("自动兼容", DouyinCompatService.AutomaticEnabled);
            if (enabled != DouyinCompatService.AutomaticEnabled) DouyinCompatService.AutomaticEnabled = enabled;
            if (!DouyinCompatService.IsTargetEditor)
                EditorGUILayout.HelpBox("当前为维护/非目标版本。可以修改并导出插件，不改本版本产物，也不阻止普通构建。", MessageType.Info);
            EditorGUILayout.HelpBox(DouyinCompatService.Status, MessageType.Info);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("通常留空，自动读取SDK配置。SDK布局变化时显式指定当前路径。", EditorStyles.wordWrappedLabel);
            output = EditorGUILayout.TextField("最终小游戏目录", output);
            source = EditorGUILayout.TextField("WebGL源framework", source);
            if (GUILayout.Button("保存路径设置")) Execute(() => { DouyinCompatService.OutputOverride = output; DouyinCompatService.SourceOverride = source; });
            using (new EditorGUI.DisabledScope(DouyinCompatService.IsBusy || !DouyinCompatService.IsTargetEditor))
                if (GUILayout.Button("检查当前导出（通常无需手动点击）")) Execute(DouyinCompatService.RunCurrentExport);
            if (GUILayout.Button("导出独立兼容unitypackage")) Execute(DouyinCompatService.ExportPluginPackage);
            if (GUILayout.Button("打开最近报告")) Execute(() => {
                string report = DouyinCompatService.LatestReportPath;
                if (!File.Exists(report)) throw new InvalidOperationException("尚无报告。");
                EditorUtility.RevealInFinder(report);
            });
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            EditorGUILayout.HelpBox("手动使用SDK构建或仅转换后，保持Editor开启，等待Console兼容完成。未知framework结构停止并保留原文件。SDK升级不等于所有版本自动通过；不改SDK、版本身份、上传规则或游戏资源。", MessageType.Warning);
        }
        private void Execute(Action action)
        { try { action(); error = null; } catch (Exception exception) { error = exception.Message; Debug.LogError("[抖音自动兼容] " + error); } }
    }
}
#endif