using System;
using UnityEngine;

/// <summary>
/// 广告管理器 —— 公开API层
/// 所有实现细节在 AdManagerImpl 中，此处仅做转发，方便调用者查看可用API
/// </summary>
[DefaultExecutionOrder(-100)]
public class AdManager : MonoBehaviour
{
    public static AdManager Instance { get; private set; }

    [Header("广告位ID")]
    [Tooltip("激励视频广告位ID")]
    public string rewardVideoAdUnitId = "j9p6hj29jm6dfnlym1";

    [Tooltip("插屏广告广告位ID")]
    public string interstitialAdUnitId = "23nbcefbl3iha3bihk";

    [Header("测试模式")]
    [Tooltip("开启后广告直接回调，用于编辑器测试")]
    public bool isTest = true;

    [Header("转化回传配置")]
    [Tooltip("巨量引擎转化回传API地址")]
    public string reportConversionUrl = "https://analytics.oceanengine.com/api/v2/conversion";

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

#region 激励视频

    /// <summary>播放激励视频广告</summary>
    public void ShowRewardVideoAd(Action onComplete, Action onFailed = null)
    {
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
        => AdManagerImpl.Instance.InitOceanEngineTracker();

    /// <summary>激活上报 (active) —— 游戏首次启动完成，进入主界面时调用。只能上报一次</summary>
    public void ReportActive()
        => AdManagerImpl.Instance.ReportActive();

    /// <summary>关键行为上报 (game_addiction) —— 用户看完一次激励广告时调用</summary>
    public void ReportGameAddiction()
        => AdManagerImpl.Instance.ReportGameAddiction();

    /// <summary>变现ROI上报 (lt_roi) —— 用户每产生一次广告收益时立即调用</summary>
    public void ReportLtRoi()
        => AdManagerImpl.Instance.ReportLtRoi();

    /// <summary>变现ROI-7日检查上报 (ltv_roi_7d) —— 游戏启动时自动调用</summary>
    public void ReportLtvRoi7Day()
        => AdManagerImpl.Instance.ReportLtvRoi7Day();

    /// <summary>启动场景数据上报</summary>
    public void ReportLaunchScene()
        => AdManagerImpl.Instance.ReportLaunchScene();

#endregion

#region Inspector 按钮默认回调（兼容旧场景，新UI请使用原子按钮组件）

    /// <summary>分享（默认回调，可重复点击）</summary>
    public void Share()
    {
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
        AdManagerImpl.Instance.Share(onSuccess, onFailed);
    }

#endregion

#region 添加桌面（有参版，供代码调用）

    /// <summary>必须从用户点击同步调用，不能在 CheckDesktopShortcut 回调中调用。</summary>
    public void AddToDesktop(Action<bool> onSuccess, Action<int, string> onFailed = null,
        Action onAlreadyAdded = null)
    {
        AdManagerImpl.Instance.AddToDesktop(onSuccess, onFailed, onAlreadyAdded);
    }

    /// <summary>检查是否已添加到桌面快捷方式</summary>
    public void CheckDesktopShortcut(Action<bool> onResult)
        => AdManagerImpl.Instance.CheckDesktopShortcut(onResult);

#endregion

#region 抖音侧边栏

    /// <summary>是否从抖音侧边栏进入游戏</summary>
    public bool IsFromSideBar => AdManagerImpl.Instance != null && AdManagerImpl.Instance.IsFromSideBarImpl;

    /// <summary>检测设备是否支持抖音侧边栏功能</summary>
    public void CheckSideBarSupport(Action onSupported = null, Action onNotSupported = null)
    {
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
        get => AdManagerImpl.Instance != null && AdManagerImpl.Instance.IsSideBarRewardClaimed;
        set
        {
            if (AdManagerImpl.Instance != null)
                AdManagerImpl.Instance.IsSideBarRewardClaimed = value;
        }
    }

    /// <summary>领取侧边栏奖励；场景UI由按钮或面板组件自行处理</summary>
    public void ClaimSideBarReward(Action onClaimed = null)
    {
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
        => AdManagerImpl.Instance.ReportFeedReadyStatus(scene, contentId, readyTimestampMs);

    /// <summary>兼容旧按钮：参数不再区分单场景，统一发起全场景订阅。</summary>
    public void CheckFeedSubscription(AdManager.FeedGameScene sceneId)
        => FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture();

    /// <summary>胜利结算按钮调用；每次游戏启动最多发起一次全场景订阅。</summary>
    public void RequestFeedSubscriptionOnWin()
        => FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture();

    /// <summary>失败结算按钮调用；与胜利共用同一个全场景订阅防重复门闩。</summary>
    public void RequestFeedSubscriptionOnFailure()
        => FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture();

    /// <summary>游戏消息订阅</summary>
    public void RequestGameMessageSubscription()
        => AdManagerImpl.Instance.RequestGameMessageSubscription();

#endregion

#region 排行榜

    /// <summary>提交分数到排行榜</summary>
    public void SubmitScoreToRank(int score)
        => AdManagerImpl.Instance.SubmitScoreToRank(score);

    /// <summary>显示排行榜</summary>
    public void ShowRank()
        => AdManagerImpl.Instance.ShowRank();

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
