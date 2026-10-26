using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>仅在编辑阶段创建并接线测试广告预制体；运行时不创建对象。</summary>
public static class AdTestLifecyclePrefabBuilder
{
    private const string DirectoryPath = "Assets/DouYinIconDemo/Prefabs/AdTest";
    private const string SystemPath = "Assets/DouYinIconDemo/Prefabs/Systems/DouyinAdSystem.prefab";
    public const string RewardPath = DirectoryPath + "/激励广告测试.prefab";
    public const string InterstitialPath = DirectoryPath + "/插屏广告测试.prefab";

    [MenuItem("抖音/测试广告/创建或更新生命周期预制体")]
    public static void Generate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("请先退出运行模式再生成测试广告。");
        Directory.CreateDirectory(DirectoryPath);
        AssetDatabase.Refresh();
        TMP_FontAsset font = LoadTestFont();
        GameObject reward = BuildPanel(true, font);
        GameObject interstitial = BuildPanel(false, font);
        GameObject system = PrefabUtility.LoadPrefabContents(SystemPath);
        try
        {
            AdTestSimulator simulator = system.GetComponent<AdTestSimulator>();
            if (simulator == null) simulator = system.AddComponent<AdTestSimulator>();
            AdTestPanel rewardPanel = FindOrPlace(system, reward);
            AdTestPanel interstitialPanel = FindOrPlace(system, interstitial);
            Set(simulator, "rewardPanel", rewardPanel);
            Set(simulator, "interstitialPanel", interstitialPanel);
            Set(system.GetComponent<AdManagerImpl>(), "testSimulator", simulator);
            PrefabUtility.SaveAsPrefabAsset(system, SystemPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(system); }
        AssetDatabase.SaveAssets();
        // 修改系统预制体会更新场景内既有实例；不替换场景、不保存其他用户改动。
        Debug.Log("[AdTest] 已生成激励/插屏预制体，并嵌入 DouyinAdSystem；一键看完按钮已序列化接线。");
    }

    private static TMP_FontAsset LoadTestFont()
    {
        string path = DirectoryPath + "/测试广告字体 SDF.asset";
        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        if (font == null)
        {
            Font source = AssetDatabase.LoadAssetAtPath<Font>("Assets/Font/DouyinSansBold.otf");
            if (source == null) throw new InvalidOperationException("缺少项目抖音字体源文件。");
            font = TMP_FontAsset.CreateFontAsset(source, 64, 6,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA, 1024, 1024,
                AtlasPopulationMode.Dynamic, true);
            font.name = "测试广告字体 SDF";
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (Texture2D texture in font.atlasTextures) AssetDatabase.AddObjectToAsset(texture, font);
        }
        // 单独预烘焙本功能的全部汉字，避免修改其他界面共用的字体和运行时补字。
        font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        string glyphs = "0123456789 .·：，、（）/ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz"
            + "激励视频测试广告插屏模拟播放中剩余秒提前关闭不会获得奖励已完整观看后领取"
            + "本地生命周期不请求真实结束恢复游戏与音频一键看完失败并继续发放加载成功就绪展示";
        if (!font.TryAddCharacters(glyphs, out string missing) && !string.IsNullOrEmpty(missing))
            throw new InvalidOperationException("测试广告字体缺字：" + missing);
        font.atlasPopulationMode = AtlasPopulationMode.Static;
        EditorUtility.SetDirty(font);
        EditorUtility.SetDirty(font.material);
        foreach (Texture2D texture in font.atlasTextures) EditorUtility.SetDirty(texture);
        return font;
    }

    private static AdTestPanel FindOrPlace(GameObject system, GameObject prefab)
    {
        Transform existing = system.transform.Find(prefab.name);
        if (existing != null) return existing.GetComponent<AdTestPanel>();
        return ((GameObject)PrefabUtility.InstantiatePrefab(prefab, system.transform)).GetComponent<AdTestPanel>();
    }

    private static GameObject BuildPanel(bool rewarded, TMP_FontAsset font)
    {
        GameObject root = new GameObject(rewarded ? "激励广告测试" : "插屏广告测试", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(AdTestPanel));
        root.layer = 5;
        try
        {
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32700;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            AdTestPanel panel = root.GetComponent<AdTestPanel>();
            RectTransform overlay = Rect("广告展示层", root.transform, Vector2.zero, Vector2.one);
            Image mask = overlay.gameObject.AddComponent<Image>();
            mask.color = new Color(0.025f, 0.04f, 0.065f, 0.97f);
            RectTransform card = Rect("广告卡片", overlay, new Vector2(.08f, .16f), new Vector2(.92f, .84f));
            card.gameObject.AddComponent<Image>().color = new Color(.08f, .11f, .17f, 1f);
            Color accent = rewarded ? new Color(.26f, .88f, .73f) : new Color(.38f, .68f, 1f);
            Text("广告标题", card, new Vector2(.06f, .87f), new Vector2(.94f, .98f),
                rewarded ? "激励视频 · 测试广告" : "插屏 · 测试广告", font, 52, accent);
            TMP_Text status = Text("播放状态", card, new Vector2(.06f, .65f), new Vector2(.94f, .85f),
                rewarded ? "模拟播放中 · 剩余 15 秒\n提前关闭不会获得奖励" : "插屏广告展示中\n关闭后继续游戏，不发放激励奖励", font, 46, Color.white);
            RectTransform track = Rect("播放进度", card, new Vector2(.09f, .57f), new Vector2(.91f, .59f));
            track.gameObject.AddComponent<Image>().color = new Color(.17f, .22f, .29f);
            Image fill = Rect("进度填充", track, Vector2.zero, Vector2.one).gameObject.AddComponent<Image>();
            fill.color = accent;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = 0;
            fill.fillAmount = rewarded ? 0f : 1f;
            fill.rectTransform.anchorMax = new Vector2(rewarded ? 0f : 1f, 1f);
            fill.raycastTarget = false;
            Text("测试提示", card, new Vector2(.07f, .43f), new Vector2(.93f, .54f),
                "本地生命周期模拟 · 不请求真实广告\n关闭后恢复游戏与音频", font, 30, new Color(.63f, .71f, .8f));
            Button finish = null;
            if (rewarded)
            {
                finish = Button("一键看完按钮", card, .29f, .40f, "一键看完", font, accent, out _);
                finish.GetComponentInChildren<TMP_Text>().color = new Color(.04f, .12f, .14f);
                UnityEventTools.AddPersistentListener(finish.onClick, panel.CompleteImmediately);
            }
            Button close = Button("关闭广告按钮", card, .15f, .26f, "关闭广告", font,
                new Color(.24f, .31f, .42f), out TMP_Text closeLabel);
            UnityEventTools.AddPersistentListener(close.onClick, panel.CloseAd);
            Button error = Button("模拟失败按钮", card, .03f, .12f, "模拟广告失败", font,
                new Color(.45f, .20f, .25f), out _);
            UnityEventTools.AddPersistentListener(error.onClick, panel.SimulateError);
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("rewarded").boolValue = rewarded;
            serialized.FindProperty("presentation").objectReferenceValue = overlay.gameObject;
            serialized.FindProperty("statusText").objectReferenceValue = status;
            serialized.FindProperty("closeText").objectReferenceValue = closeLabel;
            serialized.FindProperty("progressFill").objectReferenceValue = fill;
            serialized.FindProperty("closeButton").objectReferenceValue = close;
            serialized.FindProperty("finishButton").objectReferenceValue = finish;
            serialized.FindProperty("errorButton").objectReferenceValue = error;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            overlay.gameObject.SetActive(false);
            return PrefabUtility.SaveAsPrefabAsset(root, rewarded ? RewardPath : InterstitialPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.layer = 5;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return rect;
    }
    private static TMP_Text Text(string name, Transform parent, Vector2 min, Vector2 max,
        string value, TMP_FontAsset font, float size, Color color)
    {
        TMP_Text text = Rect(name, parent, min, max).gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = 20;
        text.fontSizeMax = size;
        text.alignment = TextAlignmentOptions.Center;
        text.color = color;
        text.text = value;
        text.raycastTarget = false;
        return text;
    }
    private static Button Button(string name, Transform parent, float bottom, float top, string label,
        TMP_FontAsset font, Color color, out TMP_Text text)
    {
        RectTransform rect = Rect(name, parent, new Vector2(.10f, bottom), new Vector2(.90f, top));
        Image image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        text = Text("文字", rect, new Vector2(.04f, .06f), new Vector2(.96f, .94f), label, font, 40, Color.white);
        return button;
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
