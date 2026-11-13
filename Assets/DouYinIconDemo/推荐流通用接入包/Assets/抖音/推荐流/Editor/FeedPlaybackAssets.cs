#if UNITY_EDITOR
using System;
using System.IO;
using Douyin.Feed;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TextCore.LowLevel;

/// <summary>创建并检查推荐流播放器与嵌套字幕预制体。</summary>
public static class FeedPlaybackAssets
{
    public const string Root = "Assets/DouYinIconDemo/推荐流通用接入包/";
    public const string SubtitlePath = Root + "Assets/抖音/推荐流/UI/FeedSubtitle.prefab";
    public const string ViewPath = Root + "Resources/FeedPlayback.prefab";

    [MenuItem("抖音/推荐流/准备播放器预制体")]
    public static void Prepare()
    {
        var caption = AssetDatabase.LoadAssetAtPath<GameObject>(SubtitlePath);
        if (caption == null) throw new InvalidOperationException("缺少 FeedSubtitle 预制体。");
        string fontPath = Root + "Assets/抖音/推荐流/UI/FeedSubtitleFont.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
        if (font == null)
        {
            var sourceFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/DouyinSansBold.otf");
            if (sourceFont == null) throw new InvalidOperationException("缺少 DouyinSansBold 字体文件。");
            font = TMP_FontAsset.CreateFontAsset(sourceFont, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "FeedSubtitleFont";
            AssetDatabase.CreateAsset(font, fontPath);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
        }
        const string requiredCharacters = "推荐流字幕同步播放配音与统一管理正在准备内容暂时无法显示请重试";
        if (!font.HasCharacters(requiredCharacters) && !font.TryAddCharacters(requiredCharacters, out string missing))
            throw new InvalidOperationException("字幕字体缺少字形：" + missing);
        EditorUtility.SetDirty(font);
        var material = AssetDatabase.LoadAssetAtPath<Material>(Root + "Assets/抖音/推荐流/UI/FeedSubtitle.mat");
        if (font == null || material == null) throw new InvalidOperationException("缺少字幕字体或材质。");
        material.shader = font.material.shader;
        material.SetTexture("_MainTex", font.atlasTexture);
        material.EnableKeyword("OUTLINE_ON"); material.EnableKeyword("UNDERLAY_ON");
        ShaderUtilities.UpdateShaderRatios(material);
        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        var subtitleRoot = PrefabUtility.LoadPrefabContents(SubtitlePath);
        try
        {
            var text = subtitleRoot.GetComponent<TextMeshProUGUI>();
            text.font = font; text.fontSharedMaterial = material; text.maxVisibleLines = 2;
            text.enableWordWrapping = true; text.UpdateMeshPadding();
            PrefabUtility.SaveAsPrefabAsset(subtitleRoot, SubtitlePath);
        }
        finally { PrefabUtility.UnloadPrefabContents(subtitleRoot); }
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath) == null)
        {
            var root = new GameObject("FeedPlayback", typeof(RectTransform));
            try
            {
                var view = root.AddComponent<FeedPlaybackView>();
                var voice = root.AddComponent<AudioSource>();
                voice.playOnAwake = false; voice.loop = false; voice.ignoreListenerPause = true;
                var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvasObject.transform.SetParent(root.transform, false);
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 32760;
                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                var subtitle = (GameObject)PrefabUtility.InstantiatePrefab(caption, canvasObject.transform);
                var panel = UI("Fallback", canvasObject.transform, new Vector2(900, 360), Vector2.zero);
                panel.AddComponent<Image>().color = new Color(0, 0, 0, .8f);
                var error = Label("Message", panel.transform, new Vector2(820, 160), new Vector2(0, 55), "");
                var buttonObject = UI("Retry", panel.transform, new Vector2(260, 80), new Vector2(0, -100));
                buttonObject.AddComponent<Image>().color = new Color(.15f, .45f, .75f, 1);
                var button = buttonObject.AddComponent<Button>();
                Label("Label", buttonObject.transform, new Vector2(240, 70), Vector2.zero, "重试");
                var so = new SerializedObject(view);
                so.FindProperty("subtitle").objectReferenceValue = subtitle.GetComponent<TextMeshProUGUI>();
                so.FindProperty("voiceSource").objectReferenceValue = voice;
                so.FindProperty("fallbackPanel").objectReferenceValue = panel;
                so.FindProperty("fallbackText").objectReferenceValue = error;
                so.FindProperty("retryButton").objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo();
                panel.SetActive(false);
                if (PrefabUtility.SaveAsPrefabAsset(root, ViewPath) == null) throw new InvalidOperationException("播放器预制体保存失败。");
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        var viewRoot = PrefabUtility.LoadPrefabContents(ViewPath);
        try
        {
            var serialized = new SerializedObject(viewRoot.GetComponent<FeedPlaybackView>());
            var message = (TextMeshProUGUI)serialized.FindProperty("fallbackText").objectReferenceValue;
            message.font = font; message.fontSharedMaterial = font.material;
            var retry = (Button)serialized.FindProperty("retryButton").objectReferenceValue;
            foreach (var label in retry.GetComponentsInChildren<TextMeshProUGUI>(true)) { label.font = font; label.fontSharedMaterial = font.material; }
            PrefabUtility.SaveAsPrefabAsset(viewRoot, ViewPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(viewRoot); }
        Validate();
        AssetDatabase.SaveAssets();
    }
    private static GameObject UI(string name, Transform parent, Vector2 size, Vector2 position)
    {
        var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
        rect.sizeDelta = size; rect.anchoredPosition = position;
        return go;
    }
    private static TextMeshProUGUI Label(string name, Transform parent, Vector2 size, Vector2 position, string text)
    {
        var label = UI(name, parent, size, position).AddComponent<TextMeshProUGUI>();
        label.font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Font/DouyinSansBold SDF.asset");
        label.fontSize = 36; label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white; label.raycastTarget = false; label.text = text;
        return label;
    }
    public static void Validate()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ViewPath);
        var view = root == null ? null : root.GetComponent<FeedPlaybackView>();
        if (view == null) throw new InvalidOperationException("播放器预制体没有 FeedPlaybackView。");
        var so = new SerializedObject(view);
        foreach (string name in new[] { "subtitle", "voiceSource", "fallbackPanel", "fallbackText", "retryButton" })
            if (so.FindProperty(name).objectReferenceValue == null) throw new InvalidOperationException("播放器缺少引用：" + name);
        var subtitle = (TextMeshProUGUI)so.FindProperty("subtitle").objectReferenceValue;
        var source = PrefabUtility.GetCorrespondingObjectFromSource(subtitle);
        if (source == null || AssetDatabase.GetAssetPath(source) != SubtitlePath) throw new InvalidOperationException("播放器必须使用 FeedSubtitle 的嵌套预制体实例。");
        var material = subtitle.fontSharedMaterial;
        if (subtitle.font == null || material == null || material.shader == null || material == subtitle.font.material ||
            !material.IsKeywordEnabled("OUTLINE_ON") || !material.IsKeywordEnabled("UNDERLAY_ON") ||
            material.GetFloat("_OutlineWidth") <= 0 || material.GetColor("_UnderlayColor").a <= 0 ||
            material.GetTexture("_MainTex") != subtitle.font.atlasTexture || subtitle.raycastTarget ||
            !subtitle.gameObject.activeSelf || subtitle.GetComponents<Shadow>().Length > 0)
            throw new InvalidOperationException("字幕字体、描边、阴影或显示属性无效。");
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject) > 0) throw new InvalidOperationException("播放器预制体缺少脚本。");
        Debug.Log("[推荐流资源检查] 播放器、嵌套字幕、字体、描边和阴影通过。");
    }
}
#endif



