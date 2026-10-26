using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using UnityEngine;

/// <summary>
/// 广告管理器 —— 公开API层
/// 所有实现细节在 AdManagerImpl 中，此处仅做转发，方便调用者查看可用API
/// </summary>
[DefaultExecutionOrder(-10000)]
public class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }

    [Header("无广告链接模式")]
    [Tooltip("勾选后使用 Unity PlayerPrefs / 本地 JSON，不初始化抖音 SDK，激励直接成功，并隐藏平台专用入口。发布前设置，切换后重新运行。")]
    public bool noAds;

    [Tooltip("可选：额外需要在无广告模式隐藏的抖音专用场景对象。不要放入玩法、体力或 AdManager 自身。")]
    public GameObject[] noAdsHideObjects = Array.Empty<GameObject>();

    [Serializable]
    private sealed class PlatformObjectState
    {
        public GameObject target;
        public bool active;
    }

    // 保存原显隐状态，取消 noAds 时只恢复本开关隐藏的对象。
    [SerializeField, HideInInspector] private List<PlatformObjectState> platformObjectStates = new List<PlatformObjectState>();

    /// <summary>无广告模式。兼容在 AdManager.Awake 前调用的存档和按钮组件。</summary>
    public static bool NoAdsEnabled
    {
        get
        {
            if (Instance != null) return Instance.noAds;
            foreach (AdManager manager in Resources.FindObjectsOfTypeAll<AdManager>())
                if (manager.gameObject.scene.IsValid() && manager.gameObject.scene.isLoaded)
                    return manager.noAds;
            return false;
        }
    }

    [Header("广告位ID")]
    [Tooltip("激励视频广告位ID")]
    public string rewardVideoAdUnitId = "j9p6hj29jm6dfnlym1";

    [Tooltip("插屏广告广告位ID")]
    public string interstitialAdUnitId = "23nbcefbl3iha3bihk";

    [Header("测试模式")]
    [Tooltip("开启后使用场景内测试广告预制体模拟加载、展示、关闭和失败，不请求真实广告")]
    public bool isTest = true;

    [Header("转化回传配置")]
    [Tooltip("巨量引擎转化回传API地址")]
    public string reportConversionUrl = "https://analytics.oceanengine.com/api/v2/conversion";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ConfigureSdkNoAds()
    {
        TTSDK.TTInterstitialAdWrapper.NoAdsProvider = () => NoAdsEnabled;
    }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            ConfigureSdkNoAds();
            DontDestroyOnLoad(gameObject);
            ApplyNoAdsVisibility();
            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyNoAdsVisibility();

    /// <summary>收集已加载场景的抖音专用对象，不包含体力、激励和玩法 UI。</summary>
    public List<GameObject> GetNoAdsPlatformObjects()
    {
        var targets = new List<GameObject>();
        if (!gameObject.scene.IsValid()) return targets; // 选中预制体资产时不修改当前场景。
        foreach (DouyinAtomicButtonBase button in Resources.FindObjectsOfTypeAll<DouyinAtomicButtonBase>())
            AddPlatformTarget(targets, button.gameObject);
        foreach (SideBarManager panel in Resources.FindObjectsOfTypeAll<SideBarManager>())
            AddPlatformTarget(targets, panel.gameObject);
        if (noAdsHideObjects != null)
            foreach (GameObject target in noAdsHideObjects) AddPlatformTarget(targets, target);
        foreach (PlatformObjectState state in platformObjectStates) AddPlatformTarget(targets, state.target);
        return targets;
    }

    private void AddPlatformTarget(List<GameObject> targets, GameObject target)
    {
        if (target == null || !target.scene.IsValid() || !target.scene.isLoaded ||
            transform.IsChildOf(target.transform) || targets.Contains(target)) return;
#if UNITY_EDITOR
        // Prefab Stage 的预览只处理该预制体，不能改动背后的真实场景。
        if (!Application.isPlaying && UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null &&
            target.scene != gameObject.scene) return;
#endif
        targets.Add(target);
    }

    /// <summary>隐藏或恢复平台专用预制体，保留原来未激活的对象状态。</summary>
    public void ApplyNoAdsVisibility()
    {
        if (!noAds)
        {
            foreach (PlatformObjectState state in platformObjectStates)
                if (state.target != null) state.target.SetActive(state.active);
            platformObjectStates.Clear();
            return;
        }
        foreach (GameObject target in GetNoAdsPlatformObjects()) HideNoAdsPlatformObject(target);
    }

    /// <summary>动态实例化的平台专用对象同样不显示，不创建替代 UI。</summary>
    public static void HideNoAdsPlatformObject(GameObject target)
    {
        if (!NoAdsEnabled || target == null) return;
        AdManager manager = Instance;
        if (manager == null)
            foreach (AdManager candidate in Resources.FindObjectsOfTypeAll<AdManager>())
                if (candidate.gameObject.scene.IsValid() && candidate.gameObject.scene.isLoaded) { manager = candidate; break; }
        if (manager == null || manager.transform.IsChildOf(target.transform)) return;
        if (!manager.platformObjectStates.Exists(state => state.target == target))
            manager.platformObjectStates.Add(new PlatformObjectState { target = target, active = target.activeSelf });
        if (target.activeSelf) target.SetActive(false);
    }

    private void OnApplicationPause(bool paused)
    {
        if (noAds && paused) TTSaveManager.Instance.Save();
    }

    private void OnApplicationFocus(bool focused)
    {
        if (noAds && !focused) TTSaveManager.Instance.Save();
    }

    private void OnApplicationQuit()
    {
        if (noAds) TTSaveManager.Instance.Save();
    }

#region 激励视频

    /// <summary>播放激励视频广告</summary>
    public void ShowRewardVideoAd(Action onComplete, Action onFailed = null)
    {
        if (noAds) { onComplete?.Invoke(); return; }
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法播放激励视频");
            onFailed?.Invoke();
            return;
        }

        AdManagerImpl.Instance.ShowRewardVideoAd(onComplete, onFailed);
    }

#endregion

#region 插屏广告

    /// <summary>播放插屏广告</summary>
    public void ShowInterstitialAd(Action onClose = null)
    {
        ConfigureSdkNoAds();
        if (TTSDK.TTInterstitialAdWrapper.TrySkip(onClose)) return;
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法播放插屏广告");
            return;
        }

        AdManagerImpl.Instance.ShowInterstitialAd(onClose);
    }

#endregion

#region 数据上报

    /// <summary>初始化OceanEngineTracker（巨量引擎转化追踪）</summary>
    public void InitOceanEngineTracker()
        { if (!noAds) AdManagerImpl.Instance?.InitOceanEngineTracker(); }

    /// <summary>激活上报 (active) —— 游戏首次启动完成，进入主界面时调用。只能上报一次</summary>
    public void ReportActive()
        { if (!noAds) AdManagerImpl.Instance?.ReportActive(); }

    /// <summary>关键行为上报 (game_addiction) —— 用户看完一次激励广告时调用</summary>
    public void ReportGameAddiction()
        { if (!noAds) AdManagerImpl.Instance?.ReportGameAddiction(); }

    /// <summary>变现ROI上报 (lt_roi) —— 用户每产生一次广告收益时立即调用</summary>
    public void ReportLtRoi()
        { if (!noAds) AdManagerImpl.Instance?.ReportLtRoi(); }

    /// <summary>变现ROI-7日检查上报 (ltv_roi_7d) —— 游戏启动时自动调用</summary>
    public void ReportLtvRoi7Day()
        { if (!noAds) AdManagerImpl.Instance?.ReportLtvRoi7Day(); }

    /// <summary>启动场景数据上报</summary>
    public void ReportLaunchScene()
        { if (!noAds) AdManagerImpl.Instance?.ReportLaunchScene(); }

#endregion

#region Inspector 按钮默认回调（兼容旧场景，新UI请使用原子按钮组件）

    /// <summary>分享（默认回调，可重复点击）</summary>
    public void Share()
    {
        if (noAds) return;
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法分享");
            return;
        }
        AdManagerImpl.Instance.Share(null, null);
    }

    /// <summary>从用户点击同步添加桌面，不得经异步查询或延迟转发；不操作场景UI。</summary>
    public void AddToDesktop()
    {
        if (noAds) return;
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法添加桌面");
            return;
        }
        AdManagerImpl.Instance.AddToDesktop();
    }

#endregion

#region 分享（有参版，供代码调用）

    /// <summary>分享</summary>
    public void Share(Action onSuccess, Action onFailed = null)
    {
        if (noAds) { onFailed?.Invoke(); return; }
        AdManagerImpl.Instance.Share(onSuccess, onFailed);
    }

#endregion

#region 添加桌面（有参版，供代码调用）

    /// <summary>必须从用户点击同步调用，不能在 CheckDesktopShortcut 回调中调用。</summary>
    public void AddToDesktop(Action<bool> onSuccess, Action<int, string> onFailed = null,
        Action onAlreadyAdded = null)
    {
        if (noAds) { onFailed?.Invoke(-1, "无广告模式不支持添加桌面"); return; }
        AdManagerImpl.Instance.AddToDesktop(onSuccess, onFailed, onAlreadyAdded);
    }

    /// <summary>检查是否已添加到桌面快捷方式</summary>
    public void CheckDesktopShortcut(Action<bool> onResult)
        { if (noAds) onResult?.Invoke(false); else AdManagerImpl.Instance?.CheckDesktopShortcut(onResult); }

#endregion

#region 抖音侧边栏

    /// <summary>是否从抖音侧边栏进入游戏</summary>
    public bool IsFromSideBar => !noAds && AdManagerImpl.Instance != null && AdManagerImpl.Instance.IsFromSideBarImpl;

    /// <summary>检测设备是否支持抖音侧边栏功能</summary>
    public void CheckSideBarSupport(Action onSupported = null, Action onNotSupported = null)
    {
        if (noAds) { onNotSupported?.Invoke(); return; }
        if (AdManagerImpl.Instance == null)
        {
            onNotSupported?.Invoke();
            return;
        }

        AdManagerImpl.Instance.CheckSideBarSupport(onSupported, onNotSupported);
    }


    /// <summary>打开抖音侧边栏（onReturn 时 SideBarManager 会切换按钮）</summary>
    public void OpenSideBar(Action onSuccess = null, Action onFailed = null, Action onReturn = null)
    {
        if (noAds) { onFailed?.Invoke(); return; }
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法打开侧边栏");
            onFailed?.Invoke();
            return;
        }

        AdManagerImpl.Instance.OpenSideBar(onSuccess, onFailed, onReturn);
    }

    /// <summary>侧边栏奖励今日是否已领取</summary>
    public bool IsSideBarRewardClaimed
    {
        get => !noAds && AdManagerImpl.Instance != null && AdManagerImpl.Instance.IsSideBarRewardClaimed;
        set
        {
            if (!noAds && AdManagerImpl.Instance != null)
                AdManagerImpl.Instance.IsSideBarRewardClaimed = value;
        }
    }

    /// <summary>领取侧边栏奖励；场景UI由按钮或面板组件自行处理</summary>
    public void ClaimSideBarReward(Action onClaimed = null)
    {
        if (noAds) return;
        if (AdManagerImpl.Instance == null)
        {
            Debug.LogWarning("[AdManager] AdManagerImpl.Instance 为空，无法领取侧边栏奖励");
            return;
        }

        AdManagerImpl.Instance.ClaimSideBarReward(onClaimed);
    }

#endregion

#region 推荐流 / 直玩就绪状态 / 订阅

    /// <summary>推荐流场景枚举</summary>
    public enum FeedGameScene
    {
        UserAcquisition, // 获客
        OfflineReward, // 离线收益
        StaminaRecovery, // 体力恢复
        ImportantEvent // 重要事件提醒
    }

    /// <summary>兼容旧接口：直接写入指定复访场景的默认就绪状态。</summary>
    public void ReportFeedReadyStatus(int scene, string contentId, string readyTimestampMs)
        { if (!noAds) AdManagerImpl.Instance?.ReportFeedReadyStatus(scene, contentId, readyTimestampMs); }

    /// <summary>兼容旧按钮：参数不再区分单场景，统一发起全场景订阅。</summary>
    public void CheckFeedSubscription(AdManager.FeedGameScene sceneId)
        { if (!noAds) FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture(); }

    /// <summary>胜利结算按钮调用；每次游戏启动最多发起一次全场景订阅。</summary>
    public void RequestFeedSubscriptionOnWin()
        { if (!noAds) FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture(); }

    /// <summary>失败结算按钮调用；与胜利共用同一个全场景订阅防重复门闩。</summary>
    public void RequestFeedSubscriptionOnFailure()
        { if (!noAds) FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture(); }

    /// <summary>游戏消息订阅</summary>
    public void RequestGameMessageSubscription()
        { if (!noAds) AdManagerImpl.Instance?.RequestGameMessageSubscription(); }

#endregion

#region 排行榜

    /// <summary>提交分数到排行榜</summary>
    public void SubmitScoreToRank(int score)
        { if (!noAds) AdManagerImpl.Instance?.SubmitScoreToRank(score); }

    /// <summary>显示排行榜</summary>
    public void ShowRank()
        { if (!noAds) AdManagerImpl.Instance?.ShowRank(); }

#endregion

    /// <summary>初始化存档（清除现有存档并写入空存档）</summary>
    [ContextMenu("初始化存档")]
    public void ClearAllTTStorage()
    {
        if (AdManagerImpl.Instance != null)
        {
            AdManagerImpl.Instance.ClearAllTTStorage();
            return;
        }

        // Inspector 右键菜单允许在非运行状态调用，此时 AdManagerImpl.Instance 尚未初始化。
        TTSaveManager.Instance.DeleteAll();
        TTSaveManager.Instance.Save();
        Debug.Log("[AdManager] 存档初始化完成");
    }
}
