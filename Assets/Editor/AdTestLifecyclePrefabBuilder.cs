using System;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>仅编辑阶段迁移/创建测试广告；保留既有节点、按钮事件和 Prefab GUID，运行时不生成 UI。</summary>
public static class AdTestLifecyclePrefabBuilder
{
    private const string DirectoryPath = "Assets/DouYinIconDemo/Prefabs/AdTest";
    private const string ImagePath = "Assets/DouYinIconDemo/Image/AdTest";
    private const string SystemPath = "Assets/DouYinIconDemo/Prefabs/Systems/DouyinAdSystem.prefab";
    public const string RewardPath = DirectoryPath + "/激励广告测试.prefab";
    public const string InterstitialPath = DirectoryPath + "/插屏广告测试.prefab";

    [MenuItem("抖音/测试广告/创建或更新生命周期预制体")]
    public static void Generate()
    {
        RequireEditMode();
        Directory.CreateDirectory(DirectoryPath);
        ImportImageSprites();
        if (!File.Exists(RewardPath)) BuildPanel(true);
        if (!File.Exists(InterstitialPath)) BuildPanel(false);
        UpdateExistingPrefabs();
    }

    [MenuItem("抖音/测试广告/更新图片界面与预制体接线")]
    public static void UpdateExistingPrefabs()
    {
        RequireEditMode();
        ImportImageSprites();
        foreach (string path in new[] { RewardPath, InterstitialPath })
        {
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            try { ApplyImages(contents.GetComponent<AdTestPanel>(), false); ConfigureCanvas(contents); PrefabUtility.SaveAsPrefabAsset(contents, path); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        GameObject system = PrefabUtility.LoadPrefabContents(SystemPath);
        try { RepairRoot(system, false); PrefabUtility.SaveAsPrefabAsset(system, SystemPath); }
        finally { PrefabUtility.UnloadPrefabContents(system); }
        AssetDatabase.SaveAssets();
        Debug.Log("[AdTest] 激励/插屏已按确认的参考图更新（插屏非全屏、激励立即下载快速领奖、动态图片倒计时），不依赖 TMP；未保存当前场景或更改模式开关。");
    }

    // 保留旧入口，避免已有编辑器自动化调用失效。
    public static void UpdateDouyinFontAndWiring() { UpdateExistingPrefabs(); }

    [MenuItem("抖音/测试广告/补齐当前场景已有广告节点")]
    public static void RepairLoadedScenes()
    {
        RequireEditMode();
        ImportImageSprites();
        int repaired = 0;
        foreach (AdManagerImpl impl in UnityEngine.Object.FindObjectsOfType<AdManagerImpl>(true))
        {
            if (!impl.gameObject.scene.IsValid() || !impl.gameObject.scene.isLoaded) continue;
            RepairRoot(impl.gameObject, true);
            EditorSceneManager.MarkSceneDirty(impl.gameObject.scene);
            repaired++;
        }
        Debug.Log(repaired == 0 ? "[AdTest] 当前场景没有 AdManagerImpl，请先放入完整 DouyinAdSystem。" : "[AdTest] 已补齐 " + repaired + " 个已有广告节点的图片面板，请自行保存场景；未更改 isTest/noAds 或广告位。");
    }

    private static void RequireEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先退出运行模式。");
    }

    private static void ImportImageSprites()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
        if (!Directory.Exists(ImagePath)) throw new DirectoryNotFoundException("缺少测试广告图片：" + ImagePath);
        foreach (string path in Directory.GetFiles(ImagePath, "*.png", SearchOption.AllDirectories))
        {
            string assetPath = path.Replace('\\', '/');
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("图片尚未导入：" + assetPath);
            if (importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single && importer.alphaIsTransparency && !importer.mipmapEnabled && importer.textureCompression == TextureImporterCompression.Uncompressed) continue;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }

    private static Sprite Sprite(string name)
    {
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ImagePath + "/" + name + ".png");
        if (sprite == null) throw new InvalidOperationException("缺少图片 Sprite：" + name);
        return sprite;
    }

    private static void RepairRoot(GameObject system, bool undo)
    {
        AdManagerImpl impl = system.GetComponent<AdManagerImpl>();
        if (impl == null) throw new InvalidOperationException("广告节点缺少 AdManagerImpl：" + system.name);
        AdTestSimulator simulator = system.GetComponent<AdTestSimulator>();
        if (simulator == null) simulator = undo ? Undo.AddComponent<AdTestSimulator>(system) : system.AddComponent<AdTestSimulator>();
        if (undo) { Undo.RecordObject(simulator, "测试广告图片接线"); Undo.RecordObject(impl, "测试广告图片接线"); }
        var reward = FindOrPlace(system, RewardPath, true, undo);
        var interstitial = FindOrPlace(system, InterstitialPath, false, undo);
        ApplyImages(reward, undo); ApplyImages(interstitial, undo);
        ConfigureCanvas(reward.gameObject); ConfigureCanvas(interstitial.gameObject);
        Set(simulator, "rewardPanel", reward); Set(simulator, "interstitialPanel", interstitial); Set(impl, "testSimulator", simulator);
        if (undo) { PrefabUtility.RecordPrefabInstancePropertyModifications(simulator); PrefabUtility.RecordPrefabInstancePropertyModifications(impl); }
    }

    private static AdTestPanel FindOrPlace(GameObject system, string path, bool rewarded, bool undo)
    {
        foreach (AdTestPanel panel in system.GetComponentsInChildren<AdTestPanel>(true)) if (panel.IsRewarded == rewarded) return panel;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) throw new InvalidOperationException("缺少测试预制体：" + path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, system.transform);
        if (undo) Undo.RegisterCreatedObjectUndo(instance, "放置测试广告图片预制体");
        return instance.GetComponent<AdTestPanel>();
    }

    private static void ApplyImages(AdTestPanel panel, bool undo)
    {
        if (panel == null) throw new InvalidOperationException("测试预制体缺少 AdTestPanel。");
        Transform overlay = panel.transform.Find("广告展示层");
        Transform card = overlay == null ? null : overlay.Find("广告卡片");
        if (card == null) throw new InvalidOperationException("测试广告缺少已有广告卡片，不擅自覆盖自定义布局。");
        bool rewarded = panel.IsRewarded;
        var so = new SerializedObject(panel);
        Button close = so.FindProperty("closeButton").objectReferenceValue as Button;
        Button finish = so.FindProperty("finishButton").objectReferenceValue as Button;
        Button error = so.FindProperty("errorButton").objectReferenceValue as Button;
        if (close == null || (rewarded && finish == null)) throw new InvalidOperationException("原测试广告按钮引用缺失。");
        if (undo) { Undo.RecordObject(panel, "参考图广告界面接线"); Undo.RecordObject(overlay, "调整广告遮罩"); }
        Layout((RectTransform)overlay, Vector2.zero, Vector2.one);
        Image scrim = overlay.GetComponent<Image>();
        if (scrim != null) { scrim.color = rewarded ? Color.black : new Color(0f, 0f, 0f, .32f); scrim.raycastTarget = true; }
        Layout((RectTransform)card, rewarded ? Vector2.zero : new Vector2(.14f, .18f), rewarded ? Vector2.one : new Vector2(.86f, .82f));
        Image oldCard = card.GetComponent<Image>();
        if (oldCard != null) { oldCard.color = Color.clear; oldCard.raycastTarget = false; }
        foreach (string name in new[] { "广告标题", "播放状态", "测试提示", "播放进度", "模拟失败按钮" })
        {
            Transform old = card.Find(name);
            if (old != null) old.gameObject.SetActive(false);
        }
        if (error != null) error.gameObject.SetActive(false); // 保留故障模拟接口，不在参考图上额外画按钮。
        RectTransform artwork = FindRect("参考广告画面", card, undo);
        Layout(artwork, Vector2.zero, Vector2.one);
        Image image = artwork.GetComponent<Image>();
        if (image == null) image = artwork.gameObject.AddComponent<Image>();
        image.color = Color.white; image.raycastTarget = false; image.preserveAspect = true;
        Sprite playing = Sprite("ReferenceReplica/reward-reference-playing");
        Sprite completed = Sprite("ReferenceReplica/reward-reference-completed");
        Sprite interstitial = Sprite("ReferenceReplica/interstitial-reference-card");
        image.sprite = rewarded ? playing : interstitial;
        AspectRatioFitter aspect = artwork.GetComponent<AspectRatioFitter>();
        if (aspect == null) aspect = artwork.gameObject.AddComponent<AspectRatioFitter>();
        aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        aspect.aspectRatio = image.sprite.rect.width / image.sprite.rect.height;
        PlaceButton(close, artwork, rewarded ? new Vector2(.89f, .957f) : new Vector2(.875f, .908f), rewarded ? new Vector2(.96f, 1f) : new Vector2(.995f, .995f), undo);
        Bind(close, panel, panel.CloseAd, "CloseAd");
        if (rewarded)
        {
            PlaceButton(finish, artwork, new Vector2(.055f, .030f), new Vector2(.945f, .096f), undo);
            Bind(finish, panel, panel.QuickClaim, "CompleteImmediately", "QuickClaim");
            so.FindProperty("watchSeconds").floatValue = 10f;
        }
        else
        {
            Transform existing = artwork.Find("立即下载按钮");
            Button download = existing == null ? null : existing.GetComponent<Button>();
            if (download == null)
            {
                RectTransform rect = FindRect("立即下载按钮", artwork, undo);
                download = rect.gameObject.AddComponent<Button>();
            }
            PlaceButton(download, artwork, new Vector2(.08f, .079f), new Vector2(.925f, .204f), undo);
            Bind(download, panel, panel.CloseAd, "CloseAd");
        }
        Transform count = card.Find("倒计时图片") ?? artwork.Find("倒计时图片");
        if (count == null) count = FindRect("倒计时图片", artwork, undo);
        Reparent(count, artwork, undo);
        Layout((RectTransform)count, new Vector2(.649f, .963f), new Vector2(.704f, .995f));
        count.gameObject.SetActive(rewarded);
        HorizontalLayoutGroup group = count.GetComponent<HorizontalLayoutGroup>();
        if (group == null) group = count.gameObject.AddComponent<HorizontalLayoutGroup>();
        group.childAlignment = TextAnchor.MiddleCenter; group.spacing = 1f;
        group.childControlWidth = true; group.childControlHeight = true;
        group.childForceExpandWidth = false; group.childForceExpandHeight = true;
        var digits = so.FindProperty("digitSprites"); digits.arraySize = 10;
        var views = so.FindProperty("countdownImages"); views.arraySize = 10;
        for (int i = 0; i < 10; i++)
        {
            RectTransform rect = FindRect("数字" + i, count, undo);
            Image digit = Convert(rect, "ReferenceReplica/countdown-" + (i % 10), undo);
            LayoutElement size = rect.GetComponent<LayoutElement>();
            if (size == null) size = rect.gameObject.AddComponent<LayoutElement>();
            size.minWidth = 0f; size.preferredWidth = 26f; size.flexibleWidth = 0f;
            digit.gameObject.SetActive(rewarded && i < 2);
            digit.sprite = Sprite("ReferenceReplica/countdown-" + (i == 0 ? 1 : 0));
            digits.GetArrayElementAtIndex(i).objectReferenceValue = Sprite("ReferenceReplica/countdown-" + i);
            views.GetArrayElementAtIndex(i).objectReferenceValue = digit;
        }
        so.FindProperty("statusImage").objectReferenceValue = image;
        so.FindProperty("closeImage").objectReferenceValue = null; // 叉号已在参考图中，独立透明 Button 负责点击。
        so.FindProperty("playingStatusSprite").objectReferenceValue = playing;
        so.FindProperty("completedStatusSprite").objectReferenceValue = completed;
        so.FindProperty("interstitialStatusSprite").objectReferenceValue = interstitial;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(panel);
        EditorUtility.SetDirty(panel);
    }

    private static RectTransform FindRect(string name, Transform parent, bool undo)
    {
        Transform found = parent.Find(name);
        if (found != null) return (RectTransform)found;
        RectTransform rect = Rect(name, parent, Vector2.zero, Vector2.one);
        if (undo) Undo.RegisterCreatedObjectUndo(rect.gameObject, "配置参考广告节点");
        return rect;
    }

    private static void Layout(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min; rect.anchorMax = max; rect.pivot = new Vector2(.5f, .5f);
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one; rect.localRotation = Quaternion.identity;
    }

    private static void Reparent(Transform child, Transform parent, bool undo)
    {
        if (child.parent == parent) return;
        if (undo) Undo.SetTransformParent(child, parent, "配置广告点击区域");
        else child.SetParent(parent, false);
    }

    private static void PlaceButton(Button button, RectTransform parent, Vector2 min, Vector2 max, bool undo)
    {
        Reparent(button.transform, parent, undo);
        Layout((RectTransform)button.transform, min, max);
        button.gameObject.SetActive(true);
        Image graphic = button.GetComponent<Image>();
        if (graphic == null) graphic = button.gameObject.AddComponent<Image>();
        graphic.sprite = null; graphic.color = Color.clear; graphic.raycastTarget = true;
        button.targetGraphic = graphic;
        Transform label = button.transform.Find("文字");
        if (label != null) label.gameObject.SetActive(false);
        EditorUtility.SetDirty(button);
        if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(button);
    }

    private static void Bind(Button button, AdTestPanel owner, UnityAction action, params string[] replaceMethods)
    {
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
            if (button.onClick.GetPersistentTarget(i) == owner && Array.IndexOf(replaceMethods, button.onClick.GetPersistentMethodName(i)) >= 0)
                UnityEventTools.RemovePersistentListener(button.onClick, i);
        UnityEventTools.AddPersistentListener(button.onClick, action);
    }

    private static Image Convert(Transform target, string spriteName, bool undo)
    {
        if (target == null) throw new InvalidOperationException("缺少既有测试广告文字节点：" + spriteName);
        Graphic old = target.GetComponent<Graphic>();
        Image image = old as Image;
        if (old != null && image == null) { if (undo) Undo.DestroyObjectImmediate(old); else UnityEngine.Object.DestroyImmediate(old); }
        if (image == null) image = undo ? Undo.AddComponent<Image>(target.gameObject) : target.gameObject.AddComponent<Image>();
        if (undo) Undo.RecordObject(image, "文字替换为图片");
        image.sprite = Sprite(spriteName); image.color = Color.white; image.preserveAspect = true; image.raycastTarget = false;
        EditorUtility.SetDirty(image);
        if (undo) PrefabUtility.RecordPrefabInstancePropertyModifications(image);
        return image;
    }

    private static void ConfigureCanvas(GameObject root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        if (canvas == null) return;
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.overrideSorting = true; canvas.sortingOrder = short.MaxValue;
        EditorUtility.SetDirty(canvas);
    }

    private static void BuildPanel(bool rewarded)
    {
        var root = new GameObject(rewarded ? "激励广告测试" : "插屏广告测试", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(AdTestPanel));
        try
        {
            root.layer = 5; ConfigureCanvas(root);
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080,1920); scaler.matchWidthOrHeight = .5f;
            AdTestPanel panel = root.GetComponent<AdTestPanel>();
            var overlay = Rect("广告展示层", root.transform, Vector2.zero, Vector2.one); overlay.gameObject.AddComponent<Image>().color = new Color(.025f,.04f,.065f,.97f);
            var card = Rect("广告卡片", overlay, new Vector2(.08f,.16f), new Vector2(.92f,.84f)); card.gameObject.AddComponent<Image>().color = new Color(.08f,.11f,.17f);
            Rect("广告标题", card, new Vector2(.06f,.87f), new Vector2(.94f,.98f));
            Rect("播放状态", card, new Vector2(.06f,.65f), new Vector2(.94f,.85f));
            Rect("测试提示", card, new Vector2(.07f,.43f), new Vector2(.93f,.54f));
            var track = Rect("播放进度",card,new Vector2(.09f,.57f),new Vector2(.91f,.59f)); track.gameObject.AddComponent<Image>().color = new Color(.17f,.22f,.29f);
            Image fill = Rect("进度填充",track,Vector2.zero,Vector2.one).gameObject.AddComponent<Image>(); fill.color = rewarded?new Color(.26f,.88f,.73f):new Color(.38f,.68f,1f); fill.raycastTarget=false;
            Button finish = rewarded ? Button("一键看完按钮",card,.29f,.40f,new Color(.26f,.88f,.73f)) : null;
            Button close = Button("关闭广告按钮",card,.15f,.26f,new Color(.24f,.31f,.42f));
            Button error = Button("模拟失败按钮",card,.03f,.12f,new Color(.45f,.20f,.25f));
            if(finish!=null) UnityEventTools.AddPersistentListener(finish.onClick,panel.CompleteImmediately);
            UnityEventTools.AddPersistentListener(close.onClick,panel.CloseAd); UnityEventTools.AddPersistentListener(error.onClick,panel.SimulateError);
            var so = new SerializedObject(panel); so.FindProperty("rewarded").boolValue=rewarded; so.FindProperty("presentation").objectReferenceValue=overlay.gameObject; so.FindProperty("progressFill").objectReferenceValue=fill; so.FindProperty("closeButton").objectReferenceValue=close; so.FindProperty("finishButton").objectReferenceValue=finish; so.FindProperty("errorButton").objectReferenceValue=error; so.ApplyModifiedPropertiesWithoutUndo();
            ApplyImages(panel,false); overlay.gameObject.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root,rewarded?RewardPath:InterstitialPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); rect.gameObject.layer=5; rect.SetParent(parent,false); rect.anchorMin=min; rect.anchorMax=max; rect.offsetMin=rect.offsetMax=Vector2.zero; return rect;
    }
    private static Button Button(string name, Transform parent, float bottom, float top, Color color)
    {
        var rect=Rect(name,parent,new Vector2(.10f,bottom),new Vector2(.90f,top)); Image image=rect.gameObject.AddComponent<Image>(); image.color=color; Button button=rect.gameObject.AddComponent<Button>(); button.targetGraphic=image; Rect("文字",rect,new Vector2(.04f,.06f),new Vector2(.96f,.94f)); return button;
    }
    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var so=new SerializedObject(target); so.FindProperty(field).objectReferenceValue=value; so.ApplyModifiedPropertiesWithoutUndo();
    }
}
