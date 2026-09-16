using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 推荐流配置的 Unity Inspector 扩展，提供服务器 JSON 生成及重复项检查功能。
/// </summary>
[CustomEditor(typeof(FeedRecommendationConfig))]
public sealed class FeedRecommendationConfigEditor : Editor
{
    // 缓存最近一次生成的 JSON，便于在 Inspector 中预览。
    private string json;
    private bool pretty;

    public override void OnInspectorGUI()
    {
        // 先绘制服务器配置工具，再显示 ScriptableObject 的默认字段。
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("服务器配置数据", EditorStyles.boldLabel);
            pretty = EditorGUILayout.Toggle("格式化预览", pretty);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("生成配置数据")) json = Config.ToRemoteJson(pretty);
                if (GUILayout.Button("生成并复制"))
                {
                    json = Config.ToRemoteJson(pretty);
                    GUIUtility.systemCopyBuffer = json;
                    Debug.Log("[推荐流配置] 已复制服务器 JSON\n" + json);
                }
            }
            if (!string.IsNullOrEmpty(json)) EditorGUILayout.TextArea(json, GUILayout.MinHeight(90));
        }

        DrawDefaultInspector();
        DrawDuplicates();
    }

    private FeedRecommendationConfig Config => (FeedRecommendationConfig)target;

    private void DrawDuplicates()
    {
        // 同一渠道下 contentId 必须唯一，否则运行时只会命中第一项。
        var keys = new HashSet<string>();
        foreach (FeedContentConfig item in Config.Contents)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.contentId)) continue;
            string key = (int)item.channel + ":" + item.contentId.Trim();
            if (!keys.Add(key)) EditorGUILayout.HelpBox("重复配置：" + key, MessageType.Error);
        }
    }
}

