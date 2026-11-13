using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>在独立的编辑器运行场景中验证无广告按钮与回调，不保存项目场景。</summary>
[InitializeOnLoad]
public static class NoAdsButtonsValidation
{
    private const string StateKey = "NoAdsButtonsValidation.State";
    private const string ExitKey = "NoAdsButtonsValidation.ExitCode";
    private static readonly List<string> Checks = new List<string>();
    private static readonly string OutputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../.artifacts/NoAdsButtons20261106"));

    [Serializable]
    private sealed class Report
    {
        public bool passed;
        public string editorVersion;
        public string projectPath;
        public string mode = "PlayMode";
        public string[] checks;
        public string failure;
    }

    static NoAdsButtonsValidation()
    {
        if (!string.IsNullOrEmpty(SessionState.GetString(StateKey, "")))
            EditorApplication.update += Tick;
    }

    public static void Run()
    {
        Directory.CreateDirectory(OutputDirectory);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetString(StateKey, "waiting");
        SessionState.SetInt(ExitKey, 1);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        string state = SessionState.GetString(StateKey, "");
        if (state == "waiting" && EditorApplication.isPlaying && !EditorApplication.isCompiling)
        {
            SessionState.SetString(StateKey, "running");
            var report = new Report { editorVersion = Application.unityVersion, projectPath = Application.dataPath };
            try
            {
                RunChecks();
                report.passed = true;
                SessionState.SetInt(ExitKey, 0);
            }
            catch (Exception exception)
            {
                report.failure = exception.ToString();
                Debug.LogException(exception);
            }
            report.checks = Checks.ToArray();
            File.WriteAllText(Path.Combine(OutputDirectory, "result.json"), JsonUtility.ToJson(report, true));
            Debug.Log("[NoAdsButtonsValidation] passed=" + report.passed + ", checks=" + Checks.Count);
            SessionState.SetString(StateKey, "exiting");
            EditorApplication.ExitPlaymode();
        }
        else if (state == "exiting" && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.EraseString(StateKey);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(SessionState.GetInt(ExitKey, 1));
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Checks.Add(description);
    }

    private static void RunChecks()
    {
        Type storeType = typeof(AdManager).Assembly.GetType("LocalNoAdsSaveStore", true);
        FieldInfo storeField = storeType.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic);
        object originalStore = storeField.GetValue(null);
        object isolatedStore = Activator.CreateInstance(storeType, BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { Path.Combine(OutputDirectory, "noads-local.json") }, null);
        storeField.SetValue(null, isolatedStore);

        string[] keys = { "DouyinAddDesktopButton", "DouyinSidebarButton", "DouyinShareButton", "DouyinRankButton" };
        var savedKeys = keys.ToDictionary(name => "douyin_atomic_button_clicked_date_" + name,
            name => PlayerPrefs.HasKey("douyin_atomic_button_clicked_date_" + name)
                ? PlayerPrefs.GetString("douyin_atomic_button_clicked_date_" + name) : null);
        foreach (string key in savedKeys.Keys) PlayerPrefs.DeleteKey(key);

        GameObject system = null;
        GameObject canvas = null;
        GameObject panel = null;
        GameObject extra = null;
        GameObject inactiveExtra = null;
        try
        {
            system = new GameObject("NoAdsValidationSystem");
            system.SetActive(false);
            AdManager manager = system.AddComponent<AdManager>();
            manager.noAds = true;
            manager.isTest = false;
            AdManagerImpl implementation = system.AddComponent<AdManagerImpl>();
            Check(AdManager.NoAdsEnabled, "noAds is available before manager Awake");
            system.SetActive(true);
            Check(AdManager.Instance == manager && !implementation.IsSDKInitialized, "noAds startup skips SDK initialization");

            canvas = new GameObject("NoAdsValidationCanvas", typeof(RectTransform), typeof(Canvas));
            var prefabs = new[] { "01_添加桌面", "02_侧边栏", "03_分享", "04_排行榜" };
            var buttons = new List<DouyinAtomicButtonBase>();
            foreach (string prefabName in prefabs)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DouYinIconDemo/Prefabs/AtomicButtons/" + prefabName + ".prefab");
                Check(prefab != null, "button prefab exists: " + prefabName);
                GameObject instance = UnityEngine.Object.Instantiate(prefab, canvas.transform);
                if (!instance.activeSelf) instance.SetActive(true);
                DouyinAtomicButtonBase atomic = instance.GetComponent<DouyinAtomicButtonBase>();
                buttons.Add(atomic);
                Check(instance.activeInHierarchy && instance.GetComponent<Button>().interactable,
                    "button is active and interactable: " + atomic.GetType().Name);
                Check(instance.transform.Find("DailyRedDot").gameObject.activeSelf,
                    "daily red dot is visible before clicking: " + atomic.GetType().Name);
            }
            panel = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DouYinIconDemo/Prefabs/Panels/SiderBarPanel.prefab"));
            manager.ApplyNoAdsVisibility();
            Check(!panel.activeInHierarchy, "sidebar teaching panel remains hidden");
            Check(buttons.All(item => item.gameObject.activeInHierarchy), "visibility pass retains all four buttons");

            int desktop = 0, share = 0, sidebar = 0, rank = 0, failures = 0, alreadyAdded = 0;
            var desktopButton = buttons.OfType<DouyinAddDesktopButton>().Single();
            var shareButton = buttons.OfType<DouyinShareButton>().Single();
            var sidebarButton = buttons.OfType<DouyinSidebarButton>().Single();
            var rankButton = buttons.OfType<DouyinRankButton>().Single();
            desktopButton.onSuccess.AddListener(() => desktop++);
            desktopButton.onFailed.AddListener(() => failures++);
            desktopButton.onAlreadyAdded.AddListener(() => alreadyAdded++);
            shareButton.onSuccess.AddListener(() => share++);
            shareButton.onFailed.AddListener(() => failures++);
            sidebarButton.onPanelOpened.AddListener(() => sidebar++);
            sidebarButton.onPanelMissing.AddListener(() => failures++);
            rankButton.onRequested.AddListener(() => rank++);

            foreach (bool isTest in new[] { false, true })
            {
                manager.isTest = isTest;
                foreach (DouyinAtomicButtonBase item in buttons) item.GetComponent<Button>().onClick.Invoke();
                int expected = isTest ? 2 : 1;
                Check(desktop == expected && share == expected && sidebar == expected && rank == expected,
                    "each button completes exactly once per click with isTest=" + isTest);
                Check(failures == 0 && alreadyAdded == 0 && !panel.activeInHierarchy,
                    "button clicks skip failure branches and sidebar UI with isTest=" + isTest);
            }
            Check(buttons.All(item => !item.transform.Find("DailyRedDot").gameObject.activeSelf),
                "clicking each button clears its daily red dot");
            Check(!implementation.IsSDKInitialized, "button clicks do not initialize the SDK");

            ValidateCallbacks(manager, implementation);

            FieldInfo implementationField = typeof(AdManagerImpl).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic);
            implementationField.SetValue(null, null);
            try
            {
                foreach (DouyinAtomicButtonBase item in buttons) item.GetComponent<Button>().onClick.Invoke();
                Check(desktop == 3 && share == 3 && sidebar == 3 && rank == 3 && failures == 0,
                    "all four callbacks work without an implementation instance");
            }
            finally { implementationField.SetValue(null, implementation); }

            extra = new GameObject("ExtraPlatformObject");
            inactiveExtra = new GameObject("InactiveExtraPlatformObject");
            inactiveExtra.SetActive(false);
            manager.noAdsHideObjects = new[] { canvas, buttons[0].gameObject, extra, inactiveExtra };
            manager.ApplyNoAdsVisibility();
            Check(canvas.activeSelf && buttons.All(item => item.gameObject.activeInHierarchy),
                "explicit hide list preserves buttons and their parent container");
            Check(!extra.activeSelf && !inactiveExtra.activeSelf && system.activeSelf,
                "other configured platform objects hide while manager remains active");

            AddVisibilityState(manager, buttons[0].gameObject, true);
            buttons[0].gameObject.SetActive(false);
            manager.ApplyNoAdsVisibility();
            Check(buttons[0].gameObject.activeInHierarchy, "saved visibility restores an active button");
            AddVisibilityState(manager, canvas, true);
            canvas.SetActive(false);
            manager.ApplyNoAdsVisibility();
            Check(canvas.activeInHierarchy && buttons.All(item => item.gameObject.activeInHierarchy),
                "saved visibility restores the button parent container");
            AddVisibilityState(manager, buttons[1].gameObject, false);
            buttons[1].gameObject.SetActive(false);
            manager.ApplyNoAdsVisibility();
            Check(!buttons[1].gameObject.activeSelf, "authored inactive button state is preserved");
            buttons[1].gameObject.SetActive(true);

            manager.noAds = false;
            manager.ApplyNoAdsVisibility();
            Check(extra.activeSelf && !inactiveExtra.activeSelf, "disabling noAds restores original extra object states");
            int normalShare = 0;
            manager.isTest = true;
            manager.Share(() => normalShare++, () => failures++);
            Check(normalShare == 1 && failures == 0, "normal test-mode sharing still uses its success callback");
            manager.noAds = true;
            manager.ApplyNoAdsVisibility();
            Check(buttons.All(item => item.gameObject.activeInHierarchy) && !implementation.IsSDKInitialized,
                "switching visibility keeps all buttons available");
        }
        finally
        {
            if (canvas != null) UnityEngine.Object.DestroyImmediate(canvas);
            if (panel != null) UnityEngine.Object.DestroyImmediate(panel);
            if (extra != null) UnityEngine.Object.DestroyImmediate(extra);
            if (inactiveExtra != null) UnityEngine.Object.DestroyImmediate(inactiveExtra);
            if (system != null) UnityEngine.Object.DestroyImmediate(system);
            foreach (var saved in savedKeys)
                if (saved.Value == null) PlayerPrefs.DeleteKey(saved.Key); else PlayerPrefs.SetString(saved.Key, saved.Value);
            PlayerPrefs.Save();
            storeField.SetValue(null, originalStore);
        }
    }

    private static void ValidateCallbacks(AdManager manager, AdManagerImpl implementation)
    {
        foreach (bool directImplementation in new[] { false, true })
        {
            int share = 0, desktop = 0, opened = 0, returned = 0, claimed = 0, supported = 0, failed = 0, alreadyAdded = 0;
            Action shareSuccess = () => share++;
            Action fail = () => failed++;
            Action<bool> desktopSuccess = success => { if (success) desktop++; else failed++; };
            if (directImplementation)
            {
                implementation.Share(shareSuccess, fail);
                implementation.AddToDesktop(desktopSuccess, (code, message) => failed++, () => alreadyAdded++);
                implementation.OpenSideBar(() => opened++, fail, () => returned++);
                implementation.ClaimSideBarReward(() => claimed++);
                implementation.CheckSideBarSupport(() => supported++, fail);
                implementation.Share(null, null);
                implementation.AddToDesktop(null, null, null);
                implementation.OpenSideBar(null, null, null);
                implementation.ClaimSideBarReward(null);
            }
            else
            {
                manager.Share(shareSuccess, fail);
                manager.AddToDesktop(desktopSuccess, (code, message) => failed++, () => alreadyAdded++);
                manager.OpenSideBar(() => opened++, fail, () => returned++);
                manager.ClaimSideBarReward(() => claimed++);
                manager.CheckSideBarSupport(() => supported++, fail);
                manager.Share(null, null);
                manager.AddToDesktop(null, null, null);
                manager.OpenSideBar(null, null, null);
                manager.ClaimSideBarReward(null);
            }
            Check(share == 1 && desktop == 1 && opened == 1 && returned == 1 && claimed == 1 && supported == 1 && failed == 0 && alreadyAdded == 0,
                "direct platform callbacks complete once, including null callback calls: implementation=" + directImplementation);
        }
        int reward = 0, rewardFailure = 0, closed = 0;
        manager.ShowRewardVideoAd(() => reward++, () => rewardFailure++);
        implementation.ShowRewardVideoAd(() => reward++, () => rewardFailure++);
        manager.ShowInterstitialAd(() => closed++);
        implementation.ShowInterstitialAd(() => closed++);
        Check(reward == 2 && rewardFailure == 0 && closed == 2, "rewarded and interstitial callbacks retain immediate completion");
        Check(!implementation.IsSDKInitialized, "direct callbacks leave SDK uninitialized");
    }

    private static void AddVisibilityState(AdManager manager, GameObject target, bool active)
    {
        Type stateType = typeof(AdManager).GetNestedType("PlatformObjectState", BindingFlags.NonPublic);
        object state = Activator.CreateInstance(stateType, true);
        stateType.GetField("target").SetValue(state, target);
        stateType.GetField("active").SetValue(state, active);
        var states = (IList)typeof(AdManager).GetField("platformObjectStates", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);
        states.Add(state);
    }
}
