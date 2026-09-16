using System;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class GenerateTreasureChestRewardPrefab
{
    private const string PrefabDirectory = "Assets/DouYinIconDemo/Prefabs/AtomicRewards";
    private const string PrefabPath = PrefabDirectory + "/TreasureChestRewardWidget.prefab";
    private const string FontPath = "Assets/Font/DouyinSansBold SDF.asset";
    private const string OrangeFramePath = "Assets/DouYinIconDemo/Image/ui/5橙框.png";
    private const string GreenFramePath = "Assets/DouYinIconDemo/Image/ui/5绿框.png";
    private const string CloseIconPath = "Assets/DouYinIconDemo/Image/ui/5图标（关闭）.png";
    private const string CoinIconPath = "Assets/DouYinIconDemo/Image/ui/5图标（金币）.png";
    private const string EnergyIconPath = "Assets/DouYinIconDemo/Image/ui/5图标（体力）.png";
    private const string ChestButtonIconPath = "Assets/DouYinIconDemo/Image/宝箱.png";

    private static readonly Color DarkBrown = Html("#38261D");
    private static readonly Color Cream = Html("#FFF8D9");
    private static readonly Color Mint = Html("#C3EED9");
    private static readonly Color Orange = Html("#FFB574");
    private static readonly Color Gold = Html("#FFD45C");

    [MenuItem("抖音/原子化UI/生成宝箱按钮与弹窗")]
    public static void Generate()
    {
        if (File.Exists(PrefabPath))
            throw new IOException("目标 Prefab 已存在，为避免覆盖已停止：" + PrefabPath);

        EnsureFolder(PrefabDirectory);
        TMP_FontAsset font = LoadRequired<TMP_FontAsset>(FontPath);
        Sprite orangeFrame = LoadRequired<Sprite>(OrangeFramePath);
        Sprite greenFrame = LoadRequired<Sprite>(GreenFramePath);
        Sprite closeIcon = LoadRequired<Sprite>(CloseIconPath);
        Sprite coinIcon = LoadRequired<Sprite>(CoinIconPath);
        Sprite energyIcon = LoadRequired<Sprite>(EnergyIconPath);
        Sprite chestButtonIcon = LoadRequired<Sprite>(ChestButtonIconPath);

        GameObject root = new GameObject(
            "TreasureChestRewardWidget",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler),
            typeof(GraphicRaycaster),
            typeof(TreasureChestRewardWidget));
        root.layer = LayerMask.NameToLayer("UI");

        try
        {
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 300;

            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(540f, 960f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            GameObject entry = CreateImageObject("Treasure Chest Button", root.transform, orangeFrame, Color.white, true);
            SetRect(entry.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-92f, -110f), new Vector2(154f, 172f));
            Button entryButton = entry.AddComponent<Button>();
            entryButton.targetGraphic = entry.GetComponent<Image>();
            entryButton.navigation = NoNavigation();

            RectTransform entryIconAnchor = CreateRectObject("Chest Icon", entry.transform);
            SetRect(entryIconAnchor, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -68f), new Vector2(136f, 124f));
            ConfigureChestButtonImage(entryIconAnchor, chestButtonIcon);

            TextMeshProUGUI countdown = CreateText("Countdown Text", entry.transform, font, "0:35s", 25f, DarkBrown, TextAlignmentOptions.Center);
            SetRect(countdown.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 28f), new Vector2(140f, 42f));

            GameObject panel = CreateRectObject("Treasure Chest Panel", root.transform).gameObject;
            Stretch(panel.GetComponent<RectTransform>());

            GameObject overlay = CreateImageObject("Dim Overlay", panel.transform, null, new Color(0f, 0f, 0f, 0.68f), true);
            Stretch(overlay.GetComponent<RectTransform>());

            GameObject card = CreateImageObject("Panel Card", panel.transform, null, Mint, true);
            SetRect(card.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(486f, 640f));
            Outline cardOutline = card.AddComponent<Outline>();
            cardOutline.effectColor = DarkBrown;
            cardOutline.effectDistance = new Vector2(4f, -4f);
            cardOutline.useGraphicAlpha = true;

            GameObject header = CreateImageObject("Title Background", card.transform, orangeFrame, Color.white, false);
            SetRect(header.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(350f, 92f));

            RectTransform titleIconAnchor = CreateRectObject("Title Chest Icon", header.transform);
            SetRect(titleIconAnchor, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(55f, 0f), new Vector2(72f, 58f));
            BuildChestIcon(titleIconAnchor, orangeFrame);

            TextMeshProUGUI title = CreateText("Title", header.transform, font, "限时宝箱", 34f, DarkBrown, TextAlignmentOptions.Center);
            SetRect(title.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(36f, 0f), new Vector2(-50f, -8f));

            GameObject close = CreateImageObject("Close Button", card.transform, closeIcon, Color.white, true);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-38f, -38f), new Vector2(58f, 58f));
            Button closeButton = close.AddComponent<Button>();
            closeButton.targetGraphic = close.GetComponent<Image>();
            closeButton.navigation = NoNavigation();

            RectTransform mainIconAnchor = CreateRectObject("Main Chest Icon", card.transform);
            SetRect(mainIconAnchor, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 92f), new Vector2(190f, 145f));
            BuildChestIcon(mainIconAnchor, orangeFrame);

            TextMeshProUGUI description = CreateText(
                "Description",
                card.transform,
                font,
                "倒计时结束后可领取宝箱奖励\n请选择一项奖励",
                25f,
                DarkBrown,
                TextAlignmentOptions.Center);
            description.enableWordWrapping = true;
            SetRect(description.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(400f, 105f));

            Button reward1Button = CreateRewardButton(
                "Reward Button 1",
                card.transform,
                orangeFrame,
                coinIcon,
                font,
                "获取奖励1",
                new Vector2(-112f, -218f));

            Button reward2Button = CreateRewardButton(
                "Reward Button 2",
                card.transform,
                greenFrame,
                energyIcon,
                font,
                "获取奖励2",
                new Vector2(112f, -218f));

            GameObject eventSystem = new GameObject("Local EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            eventSystem.transform.SetParent(root.transform, false);
            eventSystem.SetActive(false);

            TreasureChestRewardWidget widget = root.GetComponent<TreasureChestRewardWidget>();
            SerializedObject serialized = new SerializedObject(widget);
            serialized.FindProperty("popupMode").enumValueIndex = (int)TreasureChestPopupMode.ManualClick;
            serialized.FindProperty("countdownSeconds").floatValue = 35f;
            serialized.FindProperty("restartCountdownAfterReward").boolValue = true;
            serialized.FindProperty("entryButton").objectReferenceValue = entryButton;
            serialized.FindProperty("countdownText").objectReferenceValue = countdown;
            serialized.FindProperty("panelRoot").objectReferenceValue = panel;
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.FindProperty("reward1Button").objectReferenceValue = reward1Button;
            serialized.FindProperty("reward2Button").objectReferenceValue = reward2Button;
            serialized.FindProperty("localEventSystem").objectReferenceValue = eventSystem;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            panel.SetActive(false);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            if (prefab == null)
                throw new InvalidOperationException("PrefabUtility.SaveAsPrefabAsset 返回空。");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidatePrefab();
            Debug.Log("TREASURE_CHEST_PREFAB_OK " + PrefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    public static void ValidatePrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("Prefab 未生成", PrefabPath);

        TreasureChestRewardWidget widget = prefab.GetComponent<TreasureChestRewardWidget>();
        if (widget == null)
            throw new InvalidOperationException("Prefab 根节点缺少 TreasureChestRewardWidget。");

        Button[] buttons = prefab.GetComponentsInChildren<Button>(true);
        TextMeshProUGUI[] texts = prefab.GetComponentsInChildren<TextMeshProUGUI>(true);
        Canvas[] canvases = prefab.GetComponentsInChildren<Canvas>(true);
        EventSystem[] eventSystems = prefab.GetComponentsInChildren<EventSystem>(true);
        Transform panel = prefab.transform.Find("Treasure Chest Panel");

        if (buttons.Length != 4)
            throw new InvalidOperationException("按钮数量应为4，实际为" + buttons.Length);
        if (texts.Length < 5)
            throw new InvalidOperationException("文本节点数量不足：" + texts.Length);
        if (canvases.Length != 1)
            throw new InvalidOperationException("Canvas 数量应为1，实际为" + canvases.Length);
        if (eventSystems.Length != 1 || eventSystems[0].gameObject.activeSelf)
            throw new InvalidOperationException("本地 EventSystem 应存在且默认为未激活。");
        if (panel == null || panel.gameObject.activeSelf)
            throw new InvalidOperationException("宝箱 Panel 应存在且默认为关闭。");

        SerializedObject serialized = new SerializedObject(widget);
        string[] requiredReferences =
        {
            "entryButton", "countdownText", "panelRoot", "closeButton",
            "reward1Button", "reward2Button", "localEventSystem"
        };
        foreach (string propertyName in requiredReferences)
        {
            if (serialized.FindProperty(propertyName).objectReferenceValue == null)
                throw new InvalidOperationException("序列化引用为空：" + propertyName);
        }

        Debug.Log($"TREASURE_CHEST_PREFAB_VALIDATION_OK buttons={buttons.Length} texts={texts.Length} images={prefab.GetComponentsInChildren<Image>(true).Length}");
    }

    public static void PolishExistingPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            throw new FileNotFoundException("Prefab 未生成", PrefabPath);

        try
        {
            Transform card = root.transform.Find("Treasure Chest Panel/Panel Card");
            if (card == null)
                throw new InvalidOperationException("Prefab 缺少 Panel Card。");

            Image cardImage = card.GetComponent<Image>();
            cardImage.sprite = null;
            cardImage.color = Mint;
            Outline outline = card.GetComponent<Outline>();
            if (outline == null)
                outline = card.gameObject.AddComponent<Outline>();
            outline.effectColor = DarkBrown;
            outline.effectDistance = new Vector2(4f, -4f);
            outline.useGraphicAlpha = true;

            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = transforms.Length - 1; i >= 0; i--)
            {
                if (transforms[i] != null && transforms[i].name == "Glow")
                    UnityEngine.Object.DestroyImmediate(transforms[i].gameObject);
            }

            Transform panel = root.transform.Find("Treasure Chest Panel");
            Transform entry = root.transform.Find("Treasure Chest Button");
            if (panel != null)
                panel.gameObject.SetActive(false);
            if (entry != null)
                entry.gameObject.SetActive(true);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        ValidatePrefab();
        Debug.Log("TREASURE_CHEST_PREFAB_POLISH_OK " + PrefabPath);
    }

    [MenuItem("抖音/原子化UI/应用指定宝箱按钮图标并导出")]
    public static void ApplyProvidedChestButtonIconAndExport()
    {
        AssetDatabase.ImportAsset(ChestButtonIconPath, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(ChestButtonIconPath) as TextureImporter;
        if (importer == null)
            throw new InvalidOperationException("无法获取宝箱图片导入器：" + ChestButtonIconPath);

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();

        Sprite chestButtonIcon = LoadRequired<Sprite>(ChestButtonIconPath);
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null)
            throw new FileNotFoundException("Prefab 未生成", PrefabPath);

        try
        {
            Transform iconTransform = root.transform.Find("Treasure Chest Button/Chest Icon");
            if (iconTransform == null)
                throw new InvalidOperationException("Prefab 缺少 Treasure Chest Button/Chest Icon。");

            RectTransform iconRect = iconTransform as RectTransform;
            iconRect.anchoredPosition = new Vector2(0f, -68f);
            iconRect.sizeDelta = new Vector2(136f, 124f);
            ConfigureChestButtonImage(iconRect, chestButtonIcon);

            for (int i = 0; i < iconTransform.childCount; i++)
                iconTransform.GetChild(i).gameObject.SetActive(false);

            if (PrefabUtility.SaveAsPrefabAsset(root, PrefabPath) == null)
                throw new InvalidOperationException("PrefabUtility.SaveAsPrefabAsset 返回空。");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidatePrefab();
        ValidateProvidedChestButtonIcon();
        CapturePreviews();
        ExportDouyinAdEnergyPackage.Export();
        Debug.Log("TREASURE_CHEST_BUTTON_ICON_EXPORT_OK " + ChestButtonIconPath);
    }

    private static void ValidateProvidedChestButtonIcon()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Transform iconTransform = prefab == null
            ? null
            : prefab.transform.Find("Treasure Chest Button/Chest Icon");
        Image image = iconTransform == null ? null : iconTransform.GetComponent<Image>();
        if (image == null || image.sprite == null || AssetDatabase.GetAssetPath(image.sprite) != ChestButtonIconPath)
            throw new InvalidOperationException("宝箱按钮未正确引用指定图片。");
        if (image.raycastTarget || !image.preserveAspect)
            throw new InvalidOperationException("宝箱按钮图片属性不正确。");

        for (int i = 0; i < iconTransform.childCount; i++)
        {
            if (iconTransform.GetChild(i).gameObject.activeSelf)
                throw new InvalidOperationException("旧宝箱组装图层应保留但默认禁用。");
        }

        Debug.Log($"TREASURE_CHEST_BUTTON_ICON_VALIDATION_OK path={ChestButtonIconPath} size={image.rectTransform.sizeDelta}");
    }

    private static void ConfigureChestButtonImage(RectTransform iconRect, Sprite sprite)
    {
        Image image = iconRect.GetComponent<Image>();
        if (image == null)
            image = iconRect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.raycastTarget = false;
        image.color = Color.white;
    }

    public static void ValidateRuntimeContract()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("Prefab 未生成", PrefabPath);

        float originalTimeScale = Time.timeScale;
        GameObject instance = null;
        try
        {
            instance = UnityEngine.Object.Instantiate(prefab);
            TreasureChestRewardWidget widget = instance.GetComponent<TreasureChestRewardWidget>();
            InvokeLifecycle(widget, "Awake");
            InvokeLifecycle(widget, "OnEnable");

            Transform panel = instance.transform.Find("Treasure Chest Panel");
            Transform entry = instance.transform.Find("Treasure Chest Button");
            Button reward1 = FindButton(instance, "Reward Button 1");

            widget.SetPopupMode(TreasureChestPopupMode.ManualClick);
            widget.SetCountdownSeconds(0f);
            if (!widget.IsReady || widget.IsPanelOpen || panel.gameObject.activeSelf)
                throw new InvalidOperationException("手动模式倒计时完成后应只进入可领取状态。");

            widget.OpenPanel();
            if (!widget.IsPanelOpen || !panel.gameObject.activeSelf || entry.gameObject.activeSelf || !Mathf.Approximately(Time.timeScale, 0f))
                throw new InvalidOperationException("手动打开 Panel 时应隐藏入口并暂停游戏。");

            widget.ClosePanel();
            if (widget.IsPanelOpen || panel.gameObject.activeSelf || !entry.gameObject.activeSelf || !Mathf.Approximately(Time.timeScale, originalTimeScale))
                throw new InvalidOperationException("关闭 Panel 时应恢复入口与打开前的游戏速度。");

            widget.SetPopupMode(TreasureChestPopupMode.AutoOnCountdownComplete);
            widget.SetCountdownSeconds(0f);
            if (!widget.IsReady || !widget.IsPanelOpen || !panel.gameObject.activeSelf || !Mathf.Approximately(Time.timeScale, 0f))
                throw new InvalidOperationException("自动模式倒计时完成后应立即打开 Panel。");

            widget.ClosePanel();
            widget.SetPopupMode(TreasureChestPopupMode.ManualClick);
            widget.SetCountdownSeconds(35f);
            widget.OpenPanel();
            int reward1Invocations = 0;
            widget.onReward1Selected.AddListener(() => reward1Invocations++);
            reward1.onClick.Invoke();
            if (reward1Invocations != 1 || widget.IsPanelOpen || widget.IsReady || widget.RemainingSeconds <= 0f)
                throw new InvalidOperationException(
                    $"奖励1选择状态异常：invocations={reward1Invocations} " +
                    $"panel={widget.IsPanelOpen} ready={widget.IsReady} remaining={widget.RemainingSeconds:0.###}");

            InvokeLifecycle(widget, "OnDisable");
            Debug.Log("TREASURE_CHEST_RUNTIME_CONTRACT_OK manual=1 auto=1 pauseRestore=1 reward1=1 restart=1");
        }
        finally
        {
            Time.timeScale = originalTimeScale;
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    [MenuItem("抖音/原子化UI/导出宝箱Prefab预览")]
    public static void CapturePreviews()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new FileNotFoundException("Prefab 未生成", PrefabPath);

        string outputDirectory = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "BuildPackages");
        Directory.CreateDirectory(outputDirectory);
        string buttonPreview = Path.Combine(outputDirectory, "TreasureChestRewardWidget_ButtonPreview.png");
        string panelPreview = Path.Combine(outputDirectory, "TreasureChestRewardWidget_PanelPreview.png");

        Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject instance = null;
        GameObject cameraObject = null;
        RenderTexture renderTexture = null;
        try
        {
            instance = PrefabUtility.InstantiatePrefab(prefab, previewScene) as GameObject;
            if (instance == null)
                throw new InvalidOperationException("Prefab 预览实例化失败。");

            cameraObject = new GameObject("Treasure Chest Preview Camera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, previewScene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Html("#EAF5EF");
            camera.orthographic = true;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f;
            camera.transform.position = new Vector3(0f, 0f, -100f);

            Canvas canvas = instance.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 100f;

            renderTexture = new RenderTexture(540, 960, 24, RenderTextureFormat.ARGB32);
            renderTexture.Create();
            camera.targetTexture = renderTexture;

            Transform panel = instance.transform.Find("Treasure Chest Panel");
            if (panel == null)
                throw new InvalidOperationException("预览实例缺少 Treasure Chest Panel。");

            Transform entry = instance.transform.Find("Treasure Chest Button");
            panel.gameObject.SetActive(false);
            if (entry != null)
                entry.gameObject.SetActive(true);
            RenderPreview(camera, renderTexture, buttonPreview);
            panel.gameObject.SetActive(true);
            if (entry != null)
                entry.gameObject.SetActive(false);
            RenderPreview(camera, renderTexture, panelPreview);

            Debug.Log($"TREASURE_CHEST_PREVIEW_OK button={buttonPreview} panel={panelPreview}");
        }
        finally
        {
            if (renderTexture != null)
            {
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
            }
            if (cameraObject != null)
                UnityEngine.Object.DestroyImmediate(cameraObject);
            if (instance != null)
                UnityEngine.Object.DestroyImmediate(instance);
        }
    }

    private static void RenderPreview(Camera camera, RenderTexture renderTexture, string outputPath)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        try
        {
            texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply(false, false);
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
            RenderTexture.active = previous;
        }
    }

    private static void InvokeLifecycle(TreasureChestRewardWidget widget, string methodName)
    {
        MethodInfo method = typeof(TreasureChestRewardWidget).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null)
            throw new MissingMethodException(typeof(TreasureChestRewardWidget).FullName, methodName);
        method.Invoke(widget, null);
    }

    private static Button FindButton(GameObject root, string objectName)
    {
        Button[] buttons = root.GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i].name == objectName)
                return buttons[i];
        }

        throw new InvalidOperationException("未找到按钮：" + objectName);
    }

    private static void BuildChestIcon(RectTransform parent, Sprite orangeFrame)
    {
        GameObject body = CreateImageObject("Chest Body", parent, orangeFrame, Color.white, false);
        SetRect(body.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -10f), new Vector2(94f, 55f));

        GameObject lid = CreateImageObject("Chest Lid", parent, orangeFrame, Color.white, false);
        SetRect(lid.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 23f), new Vector2(104f, 34f));

        GameObject band = CreateImageObject("Gold Band", parent, null, Gold, false);
        SetRect(band.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0f), new Vector2(15f, 67f));

        GameObject lockPlate = CreateImageObject("Lock Plate", parent, null, DarkBrown, false);
        SetRect(lockPlate.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(25f, 25f));

        GameObject lockCore = CreateImageObject("Lock Core", parent, null, Gold, false);
        SetRect(lockCore.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(13f, 13f));

        GameObject highlight = CreateImageObject("Highlight", parent, null, Cream, false);
        SetRect(highlight.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-24f, 26f), new Vector2(23f, 5f));
    }

    private static Button CreateRewardButton(
        string name,
        Transform parent,
        Sprite background,
        Sprite icon,
        TMP_FontAsset font,
        string label,
        Vector2 anchoredPosition)
    {
        GameObject buttonObject = CreateImageObject(name, parent, background, Color.white, true);
        SetRect(buttonObject.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), anchoredPosition, new Vector2(205f, 82f));
        Button button = buttonObject.AddComponent<Button>();
        button.targetGraphic = buttonObject.GetComponent<Image>();
        button.navigation = NoNavigation();

        GameObject iconObject = CreateImageObject("Reward Icon", buttonObject.transform, icon, Color.white, false);
        SetRect(iconObject.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(43f, 0f), new Vector2(45f, 45f));

        TextMeshProUGUI text = CreateText("Label", buttonObject.transform, font, label, 25f, DarkBrown, TextAlignmentOptions.Center);
        SetRect(text.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(25f, 0f), new Vector2(-20f, -6f));
        return button;
    }

    private static RectTransform CreateRectObject(string name, Transform parent)
    {
        GameObject gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.layer = LayerMask.NameToLayer("UI");
        RectTransform rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static GameObject CreateImageObject(string name, Transform parent, Sprite sprite, Color color, bool raycastTarget)
    {
        RectTransform rect = CreateRectObject(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = raycastTarget;
        image.preserveAspect = sprite != null && (name.Contains("Icon") || name.Contains("Glow"));
        return rect.gameObject;
    }

    private static TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        TMP_FontAsset font,
        string value,
        float fontSize,
        Color color,
        TextAlignmentOptions alignment)
    {
        RectTransform rect = CreateRectObject(name, parent);
        TextMeshProUGUI text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = fontSize;
        text.fontStyle = FontStyles.Normal;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localRotation = Quaternion.identity;
        rect.localScale = Vector3.one;
    }

    private static Navigation NoNavigation()
    {
        Navigation navigation = new Navigation { mode = Navigation.Mode.None };
        return navigation;
    }

    private static T LoadRequired<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null)
            throw new FileNotFoundException("必需资源不存在或类型不匹配", path);
        return asset;
    }

    private static void EnsureFolder(string folderPath)
    {
        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static Color Html(string html)
    {
        if (!ColorUtility.TryParseHtmlString(html, out Color color))
            throw new ArgumentException("无效颜色：" + html);
        return color;
    }
}
