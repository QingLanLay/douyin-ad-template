#define USE_DY_AD     // 定义此宏启用抖音广告SDK (发布前取消注释)
// #define USE_TEST_MODE // 定义此宏启用测试模式 (仅模拟回调，不实际调用SDK)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using Cysharp.Threading.Tasks;
#if !USE_DY_AD
using TTSDK;
#endif
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// AdManager的实现层 —— 所有内部实现逻辑
/// 外部调用者只需使用 AdManager 的公开API，无需关心此处细节
/// </summary>
public class AdManagerImpl : MonoBehaviour
{
    public static AdManagerImpl Instance { get; private set; }

    // 以下配置项从 AdManager.Instance 读取，方便在 Inspector 中统一修改
    private string rewardVideoAdUnitId => AdManager.Instance != null ? AdManager.Instance.rewardVideoAdUnitId : "";
    private string interstitialAdUnitId => AdManager.Instance != null ? AdManager.Instance.interstitialAdUnitId : "";
    private bool isTest => AdManager.Instance != null && AdManager.Instance.isTest;
    private string reportConversionUrl => AdManager.Instance != null ? AdManager.Instance.reportConversionUrl : "";

    // 回调
    private Action _rewardCallback;
    private Action _rewardFailCallback;
    private long _rewardRequestSequence;
    private long _activeRewardRequestId;
    private long _rewardGrantedRequestId = -1;
    private TTRewardedVideoAd _rewardHandlerOwner;
    private RewardedAdClosedDelegate _rewardCloseHandler;
    private AdErrorDelegate _rewardErrorHandler;

    // 抖音SDK广告实例
    private TTRewardedVideoAd _rewardVideoAd;
    private TTInterstitialAd _interstitialAd;
    private bool _isInterstitialAdLoaded;
    private bool _isInterstitialLoadFailed;
    private bool _isInterstitialAdLoading;
    private bool _isInterstitialRequestInProgress;
    private Action _interstitialCloseCallback;

    private const float InterstitialStartupDelaySeconds = 30f;
    private const float FullscreenAdIntervalSeconds = 60f;
    private float _lastFullscreenAdTime = float.NegativeInfinity;

    // 平台标识
    private bool _isIOS;
    private bool _isSDKInitialized;
    private bool _isFromSideBar;
    private bool _isFeedLaunch;
    private bool _postSdkInitializationStarted;
    private bool _hasCheckedDesktopShortcut;
    private bool _isDesktopShortcutAdded;

    // 激活上报 - 防止重复上报
    private bool _hasReportedActive;

    // 属性暴露给AdManager
    public bool IsSDKInitialized => _isSDKInitialized;
    public bool HasReportedActive { get => _hasReportedActive; set => _hasReportedActive = value; }

    public bool IsFromSideBarImpl
    {
        get
        {
#if USE_DY_AD
            return (TT.s_ContainerEnv != null &&
                    TT.s_ContainerEnv.GetLaunchFrom() == "homepage" &&
                    TT.s_ContainerEnv.GetLocation() == "sidebar_card") ||
                   _isFromSideBar;
#else
            return _isFromSideBar;
#endif
        }
        set => _isFromSideBar = value;
    }

    private void Awake()
    {
        Debug.Log($"[AdManagerImpl] ====== Awake 开始, isTest={isTest} ======");

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

        if (isTest)
        {
            // 测试模式初始化：只记录平台状态，不持有或操作场景UI
            Debug.Log("[AdManagerImpl] 测试模式初始化");

            CheckDesktopShortcut((isAdded) =>
            {
                Debug.Log($"[AdManagerImpl] 桌面快捷方式状态: {isAdded}");
            });

            Debug.Log($"[AdManagerImpl] 【侧边栏初始化】isTest模式 - IsSideBarRewardClaimed={IsSideBarRewardClaimed}");
            if (IsSideBarRewardClaimed)
            {
                Debug.Log("[AdManagerImpl] 【侧边栏初始化】测试模式 - 奖励已领取，调用 HideSideBarUI");
                HideSideBarUI();
            }

            return;
        }

        Debug.Log($"[AdManagerImpl] 非isTest模式，开始 TT.InitSDK");
        Debug.Log($"[AdManagerImpl] 【侧边栏初始化】IsFromSideBarImpl={IsFromSideBarImpl}, _isFromSideBar={_isFromSideBar}");
#if USE_DY_AD
        try
        {
            Debug.Log($"[AdManagerImpl] 【侧边栏初始化】TT.s_ContainerEnv.GetLaunchFrom()={TT.s_ContainerEnv?.GetLaunchFrom()}");
            Debug.Log($"[AdManagerImpl] 【侧边栏初始化】TT.s_ContainerEnv.GetLocation()={TT.s_ContainerEnv?.GetLocation()}");
        }
        catch { }
#endif

#if UNITY_WEBGL
        var sysInfo = TT.GetSystemInfo();
        if (sysInfo != null)
        {
            _isIOS = sysInfo.platform.ToLower().Equals("ios");
        }
        else
        {
            Debug.LogWarning("[AdManagerImpl] TT.GetSystemInfo() 返回 null，默认作为非iOS平台");
            _isIOS = false;
        }
#endif

        TT.InitSDK((code, env) =>
        {
            _isSDKInitialized = true;
            Debug.Log($"[AdManagerImpl] SDK初始化完成 code:{code}");

            LaunchOption launchOption = TT.GetLaunchOptionsSync();
            FeedRecommendationProjectAdapter.Register();
            _isFeedLaunch = FeedRecommendationManager.Instance.HandleLaunchOptions(launchOption);

            if (!_isFeedLaunch)
            {
                // 普通启动维持原有上报、登录和平台初始化流程。
                ReportLaunchScene();
                BeginPostSdkInitialization();
            }
            else
            {
                // 推荐流预启动不强制登录、不上报正式激活，等待首次 FeedEnter。
                Debug.Log("[AdManagerImpl] 推荐流预加载：延后登录和激活上报至首次 FeedEnter");
            }

            // 创建广告实例
            CreateInterstitialAd();
            CreateRewardVideoAd();

            // 巨量引擎转化追踪初始化
            InitOceanEngineTracker();

            Debug.Log("Unity message init sdk callback");
            Debug.Log("Unity message code: " + code);
            Debug.Log("Unity message HostEnum: " + env.m_HostEnum);
        });
    }

    /// <summary>
    /// 推荐流首次真实进入后补做普通平台初始化；多次调用只执行一次。
    /// </summary>
    public void BeginPostSdkInitialization()
    {
        if (_postSdkInitializationStarted) return;
        _postSdkInitializationStarted = true;

        Debug.Log("[AdManagerImpl] 【侧边栏初始化】开始 TTLogin");
        TTLogin(() =>
        {
            TT.SetOnBeforeExitAppListener(() =>
            {
                DateTime startTime = TimeZone.CurrentTimeZone.ToLocalTime(new DateTime(1970, 1, 1));
                long time = (long)(DateTime.Now - startTime).TotalMilliseconds;
                TTSaveManager.Instance.SetString("ExitGame", time.ToString());
                TTSaveManager.Instance.Save();
                return true;
            });

            CheckDesktopShortcut(isAdded => Debug.Log($"[AdManagerImpl] 桌面快捷方式状态: {isAdded}"));
            CheckSideBarSupport(
                onSupported: () =>
                {
                    if (IsSideBarRewardClaimed) HideSideBarUI();
                },
                onNotSupported: HideSideBarUI);
        });

        ReportActive();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        if (!isTest && !_isSDKInitialized)
        {
            CreateRewardVideoAd();
            CreateInterstitialAd();
        }

        ReportLtvRoi7Day();
    }

    // ============================================================
    // TTLogin（与DYADSS.TTLogin逻辑完全一致）
    // ============================================================

    /// <summary>
    /// 抖音登录：登录成功后执行回调
    /// 与DYADSS.TTLogin()逻辑完全一致
    /// </summary>
    private void TTLogin(Action loginOk)
    {
#if UNITY_EDITOR
        Debug.Log("[AdManagerImpl] TTLogin 编辑器模式");
        loginOk?.Invoke();
        return;
#endif
        var force = true;
        TT.Login((code, anonymousCode, isLogin) =>
        {
            Debug.Log($"[AdManagerImpl] TTLogin success: force:{force}, code:{code}, anonymousCode:{anonymousCode}, isLogin:{isLogin}");
            loginOk?.Invoke();
        },
            (msg) => { Debug.Log($"[AdManagerImpl] TTLogin failed: force:{force}, {msg}"); },
            force);
    }

    // ============================================================
    // 激励视频
    // ============================================================

    public void ShowRewardVideoAd(Action onComplete, Action onFailed = null)
    {
        long requestId = ++_rewardRequestSequence;
        if (_activeRewardRequestId != 0)
        {
            Debug.Log($"[Ad] 激励视频新请求替换旧请求：old={_activeRewardRequestId}, new={requestId}");
        }

        _activeRewardRequestId = requestId;
        _rewardCallback = onComplete;
        _rewardFailCallback = onFailed;

        if (isTest)
        {
            Debug.Log("[Ad] 激励视频 - 测试模式");
            CompleteRewardRequest(requestId, true);
            return;
        }

#if USE_TEST_MODE || UNITY_EDITOR
        Debug.Log("[Ad] 测试模式 - 模拟激励视频");
        CompleteRewardRequest(requestId, true);
#elif USE_DY_AD && UNITY_WEBGL
        ShowRewardVideoWebGL(requestId);
#elif USE_DY_AD && UNITY_ANDROID
        ShowRewardVideoAndroid(requestId);
#elif USE_DY_AD && UNITY_IOS
        ShowRewardVideoIOS(requestId);
#else
        Debug.LogWarning("[Ad] 未启用任何广告模式");
        CompleteRewardRequest(requestId, false);
#endif
    }

    private void CreateRewardVideoAd()
    {
#if USE_DY_AD
        var param = new CreateRewardedVideoAdParam { AdUnitId = rewardVideoAdUnitId };
        TTRewardedVideoAd createdAd = TT.CreateRewardedVideoAd(param);
        _rewardVideoAd = createdAd;

        if (createdAd == null)
        {
            Debug.LogWarning("[Ad] 创建激励视频实例失败，后续点击仍会重新尝试");
            return;
        }

#endif
    }

    private void ReplaceRewardRequestHandlers(TTRewardedVideoAd ad, long requestId)
    {
        DetachRewardRequestHandlers();
        _rewardHandlerOwner = ad;
        _rewardCloseHandler = (ended, count) =>
        {
            // 任意激励关闭都只影响本次运行中的插屏间隔，不限制下一次激励请求。
            _lastFullscreenAdTime = Time.realtimeSinceStartup;
            Debug.Log($"[Ad] 激励视频关闭 request={requestId}, ended={ended}, count={count}，重置插屏冷却计时");
            if (!ended) Debug.Log("[Ad] 激励视频未看完");
            CompleteRewardRequest(requestId, ended);
        };
        _rewardErrorHandler = (code, message) =>
        {
            Debug.LogError($"[Ad] 激励视频错误 request={requestId}, code={code}, msg={message}");
            CompleteRewardRequest(requestId, false);
        };
        ad.OnClose += _rewardCloseHandler;
        ad.OnError += _rewardErrorHandler;
    }

    private void DetachRewardRequestHandlers()
    {
        if (_rewardHandlerOwner != null)
        {
            if (_rewardCloseHandler != null) _rewardHandlerOwner.OnClose -= _rewardCloseHandler;
            if (_rewardErrorHandler != null) _rewardHandlerOwner.OnError -= _rewardErrorHandler;
        }

        _rewardHandlerOwner = null;
        _rewardCloseHandler = null;
        _rewardErrorHandler = null;
    }

    private void CompleteRewardRequest(long requestId, bool rewarded)
    {
        if (requestId == 0 || requestId != _activeRewardRequestId)
        {
            Debug.LogWarning($"[Ad] 忽略过期激励回调：callback={requestId}, active={_activeRewardRequestId}");
            return;
        }

        Action callback;
        if (rewarded)
        {
            if (_rewardGrantedRequestId == requestId) return;
            _rewardGrantedRequestId = requestId;
            callback = _rewardCallback;
        }
        else
        {
            callback = _rewardFailCallback;
        }

        ClearRewardCallbacks(requestId);
        callback?.Invoke();
        if (rewarded) ReportAdComplete();
    }

    private void ClearRewardCallbacks(long requestId)
    {
        if (requestId != _activeRewardRequestId) return;
        DetachRewardRequestHandlers();
        _activeRewardRequestId = 0;
        _rewardCallback = null;
        _rewardFailCallback = null;
    }

    private void HandleRewardVideoUnavailable(long requestId)
    {
        Debug.LogWarning("[Ad] 激励视频尚未就绪，重新创建并通知调用方失败");
        CompleteRewardRequest(requestId, false);
        CreateRewardVideoAd();
    }



#if UNITY_WEBGL
    private void ShowRewardVideoWebGL(long requestId)
    {
        if (_rewardVideoAd != null)
        {
            ReplaceRewardRequestHandlers(_rewardVideoAd, requestId);
            try
            {
                _rewardVideoAd.Show();
                ReportAdShowReward();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Ad] WebGL激励视频请求异常：" + exception.Message);
                CompleteRewardRequest(requestId, false);
            }
        }
        else
        {
            HandleRewardVideoUnavailable(requestId);
        }
    }
#endif

#if UNITY_ANDROID
    private void ShowRewardVideoAndroid(long requestId)
    {
        if (_rewardVideoAd != null)
        {
            ReplaceRewardRequestHandlers(_rewardVideoAd, requestId);
            try
            {
                _rewardVideoAd.Show();
                ReportAdShowReward();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Ad] Android激励视频请求异常：" + exception.Message);
                CompleteRewardRequest(requestId, false);
            }
        }
        else
        {
            HandleRewardVideoUnavailable(requestId);
        }
    }
#endif

#if UNITY_IOS
    private void ShowRewardVideoIOS(long requestId)
    {
        if (_rewardVideoAd != null)
        {
            ReplaceRewardRequestHandlers(_rewardVideoAd, requestId);
            try
            {
                _rewardVideoAd.Show();
                ReportAdShowReward();
            }
            catch (Exception exception)
            {
                Debug.LogError("[Ad] iOS激励视频请求异常：" + exception.Message);
                CompleteRewardRequest(requestId, false);
            }
        }
        else
        {
            HandleRewardVideoUnavailable(requestId);
        }
    }
#endif

    // ============================================================
    // 插屏广告
    // ============================================================

    public void ShowInterstitialAd(Action onClose = null)
    {
        if (isTest)
        {
            Debug.Log("[Ad] 插屏广告 - 测试模式");
            onClose?.Invoke();
            return;
        }

        // 抖音新规：本次运行前30秒禁止插屏，且全屏广告间隔只保存在会话内。
        float now = Time.realtimeSinceStartup;
        if (now < InterstitialStartupDelaySeconds)
        {
            Debug.Log($"[Ad] 插屏广告 - 本次运行不足{InterstitialStartupDelaySeconds:0}秒，跳过");
            return;
        }

        if (now - _lastFullscreenAdTime < FullscreenAdIntervalSeconds)
        {
            Debug.Log($"[Ad] 插屏广告 - 距离本次运行内上次插屏展示/激励关闭不足{FullscreenAdIntervalSeconds:0}秒");
            return;
        }

        if (_isInterstitialRequestInProgress)
        {
            Debug.Log("[Ad] 插屏广告 - 已有加载或展示流程进行中，跳过并发请求");
            return;
        }

#if UNITY_EDITOR
        Debug.Log("[Ad] 编辑器模式 - 模拟插屏广告展示");
        onClose?.Invoke();
        return;
#endif

#if USE_DY_AD
        if (!_isIOS)
        {
            // Android / WebGL：仅展示已完成加载的实例
            if (_interstitialAd != null && _isInterstitialAdLoaded)
            {
                _interstitialCloseCallback = onClose;
                _isInterstitialRequestInProgress = true;
                _isInterstitialAdLoaded = false;
                try
                {
                    _interstitialAd.Show();
                    ReportAdShowInterstitial();
                    Debug.Log("[Ad] 已调用插屏广告Show，等待关闭回调确认成功弹出");
                }
                catch (Exception exception)
                {
                    _isInterstitialRequestInProgress = false;
                    _interstitialCloseCallback = null;
                    Debug.LogError("[Ad] 插屏广告Show异常：" + exception.Message);
                    _interstitialAd.Destroy();
                    _interstitialAd = null;
                    Debug.Log("[Ad] 插屏Show失败后停止自动申请，等待下次业务主动请求");
                }
            }
            else
            {
                Debug.Log("[Ad] 插屏广告尚未就绪，重新创建并跳过本次展示");
                CreateInterstitialAd();
            }
        }
        else
        {
            // iOS：每次重新创建实例，异步加载期间禁止并发创建或展示。
            ShowInterstitialIOS(onClose);
        }
#endif
    }

    private void CreateInterstitialAd()
    {
        if (_isIOS || _isInterstitialAdLoading || _isInterstitialAdLoaded || _isInterstitialRequestInProgress) return;

#if USE_DY_AD
        _isInterstitialAdLoading = true;
        _isInterstitialAdLoaded = false;
        _isInterstitialLoadFailed = false;
        var param = new CreateInterstitialAdParam { InterstitialAdId = interstitialAdUnitId };
        TTInterstitialAd createdAd = TT.CreateInterstitialAd(param);
        _interstitialAd = createdAd;

        if (createdAd == null)
        {
            _isInterstitialAdLoading = false;
            _isInterstitialLoadFailed = true;
            Debug.LogWarning("[Ad] 创建插屏广告实例失败");
            return;
        }

        createdAd.OnClose += () =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            // TTSDK 没有 OnShow；OnClose 是确认插屏确实展示过的首个可靠回调，只在这里重置展示 CD。
            MarkConfirmedInterstitialDisplay("");
            _isInterstitialRequestInProgress = false;
            _isInterstitialAdLoading = false;
            _isInterstitialAdLoaded = false;
            _interstitialCloseCallback?.Invoke();
            _interstitialCloseCallback = null;
            createdAd.Destroy();
            _interstitialAd = null;
            CreateInterstitialAd();
            Debug.Log("[Ad] 插屏广告关闭");
        };

        createdAd.OnLoad += () =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            _isInterstitialAdLoading = false;
            _isInterstitialAdLoaded = true;
            Debug.Log("[Ad] 插屏广告加载完成");
        };

        createdAd.OnError += (code, msg) =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            _isInterstitialRequestInProgress = false;
            _isInterstitialAdLoading = false;
            _isInterstitialAdLoaded = false;
            _isInterstitialLoadFailed = true;
            _interstitialCloseCallback = null;
            Debug.LogError($"[Ad] 插屏错误 code:{code} msg:{msg}");
            createdAd.Destroy();
            _interstitialAd = null;
            Debug.Log("[Ad] 插屏失败后停止自动申请，等待下次业务主动请求");
        };
#endif
    }



    /// <summary>
    /// iOS平台插屏广告展示：重新创建实例，等待加载完成后展示
    /// 与DYADSS.ShowInterstitialAd中iOS分支逻辑完全一致
    /// </summary>
    private async void ShowInterstitialIOS(Action onClose)
    {
#if USE_DY_AD
        if (_isInterstitialRequestInProgress) return;
        _isInterstitialRequestInProgress = true;
        _interstitialCloseCallback = onClose;
        _isInterstitialAdLoaded = false;
        _isInterstitialLoadFailed = false;
        var param = new CreateInterstitialAdParam { InterstitialAdId = interstitialAdUnitId };
        TTInterstitialAd createdAd = TT.CreateInterstitialAd(param);
        _interstitialAd = createdAd;

        if (createdAd == null)
        {
            _isInterstitialRequestInProgress = false;
            _isInterstitialLoadFailed = true;
            _interstitialCloseCallback = null;
            Debug.LogWarning("[Ad] iOS创建插屏广告实例失败");
            return;
        }

        createdAd.OnClose += () =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            // TTSDK 没有 OnShow；OnClose 是确认插屏确实展示过的首个可靠回调，只在这里重置展示 CD。
            MarkConfirmedInterstitialDisplay("iOS");
            _isInterstitialRequestInProgress = false;
            _isInterstitialAdLoaded = false;
            _interstitialCloseCallback?.Invoke();
            _interstitialCloseCallback = null;
            createdAd.Destroy();
            _interstitialAd = null;
            Debug.Log("[Ad] iOS插屏广告关闭");
        };

        createdAd.OnLoad += () =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            _isInterstitialAdLoaded = true;
            Debug.Log("[Ad] iOS插屏广告加载完成");
        };

        createdAd.OnError += (code, msg) =>
        {
            if (!ReferenceEquals(createdAd, _interstitialAd)) return;
            _isInterstitialRequestInProgress = false;
            _isInterstitialLoadFailed = true;
            _interstitialCloseCallback = null;
            Debug.LogError($"[Ad] iOS插屏错误 code:{code} msg:{msg}");
        };

        // 等待广告加载完成，最多10秒，避免网络异常时永久挂起
        float deadline = Time.realtimeSinceStartup + 10f;
        while (!_isInterstitialAdLoaded && !_isInterstitialLoadFailed && Time.realtimeSinceStartup < deadline)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, this.GetCancellationTokenOnDestroy());
        }

        if (_interstitialAd == null || !_isInterstitialAdLoaded)
        {
            Debug.LogWarning("[Ad] iOS插屏加载失败或超时，跳过本次展示");
            _isInterstitialRequestInProgress = false;
            _interstitialCloseCallback = null;
            if (createdAd != null) createdAd.Destroy();
            _interstitialAd = null;
            return;
        }

        _isInterstitialAdLoaded = false;
        try
        {
            createdAd.Show();
            ReportAdShowInterstitial();
            Debug.Log("[Ad] 已调用iOS插屏广告Show，等待关闭回调确认成功弹出");
        }
        catch (Exception exception)
        {
            _isInterstitialRequestInProgress = false;
            _interstitialCloseCallback = null;
            Debug.LogError("[Ad] iOS插屏广告Show异常：" + exception.Message);
            createdAd.Destroy();
            _interstitialAd = null;
        }
#endif
    }

    private void MarkConfirmedInterstitialDisplay(string platformLabel)
    {
        _lastFullscreenAdTime = Time.realtimeSinceStartup;
        Debug.Log($"[Ad] {platformLabel}插屏广告已确认弹出并关闭，开始{FullscreenAdIntervalSeconds:0}秒冷却");
    }

    // ============================================================
    // 数据上报（内部）
    // ============================================================

    private void ReportAdShowReward()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.ReportAdByType(0);
            ReportGameAddiction();
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] ReportAdShowReward error: {e.Message}"); }
    }

    private void ReportAdShowInterstitial()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.ReportAdByType(1);
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] ReportAdShowInterstitial error: {e.Message}"); }
    }

    private void ReportAdComplete()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.ReportGainResource();
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] ReportAdComplete error: {e.Message}"); }
    }

    // ============================================================
    // 激活上报 / 变现ROI / 关键行为 / 变现ROI-7日
    // ============================================================

    public void InitOceanEngineTracker()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.Init();
            Debug.Log("[Ad] OceanEngineTracker初始化完成");
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] OceanEngineTracker初始化失败: {e.Message}"); }
    }

    public void ReportActive()
    {
        if (_hasReportedActive)
        {
            Debug.Log("[Ad] 激活上报已执行过，跳过重复上报");
            return;
        }

#if UNITY_EDITOR
        Debug.Log("[Ad] 编辑器模式 - 模拟激活上报");
        _hasReportedActive = true;
        return;
#endif

        if (TT.s_ContainerEnv == null)
        {
            Debug.LogWarning("[Ad] TT.s_ContainerEnv is null, 跳过激活上报");
            return;
        }

        try
        {
            TTSDK.LaunchOption launchOption = TT.GetLaunchOptionsSync();
            Debug.Log($"[Ad] 启动场景: {launchOption.Scene}");

            if (launchOption.Query.ContainsKey("clickid"))
            {
                var clickid = launchOption.Query["clickid"];
                Debug.Log($"[Ad] 获取到clickid: {clickid}");
                _hasReportedActive = true;
                StartCoroutine(ReportConversionCoroutine(clickid, "active"));
            }
            else
            {
                Debug.Log("[Ad] 启动参数中未找到clickid，跳过激活上报");
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] 激活上报异常: {e.Message}"); }
    }

    public void ReportGameAddiction()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.ReportGameAddiction();
            Debug.Log("[Ad] 关键行为上报完成 (game_addiction)");
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] 关键行为上报异常: {e.Message}"); }
    }

    public void ReportLtRoi()
    {
#if UNITY_EDITOR
        Debug.Log("[Ad] 编辑器模式 - 模拟变现ROI上报");
        return;
#endif

        if (TT.s_ContainerEnv == null)
        {
            Debug.LogWarning("[Ad] TT.s_ContainerEnv is null, 跳过变现ROI上报");
            return;
        }

        try
        {
            TTSDK.LaunchOption launchOption = TT.GetLaunchOptionsSync();

            if (launchOption.Query.ContainsKey("clickid"))
            {
                var clickid = launchOption.Query["clickid"];
                Debug.Log($"[Ad] 变现ROI上报 clickid: {clickid}");
                StartCoroutine(ReportConversionCoroutine(clickid, "lt_roi"));
            }
            else
            {
                Debug.Log("[Ad] 启动参数中未找到clickid，跳过变现ROI上报");
            }
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] 变现ROI上报异常: {e.Message}"); }
    }

    public void ReportLtvRoi7Day()
    {
        try
        {
#if USE_DY_AD
            OceanEngineTracker.Ins.CheckAndReport7DayROI();
            Debug.Log("[Ad] 7日变现ROI检查上报完成 (ltv_roi_7d)");
#endif
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] 7日变现ROI上报异常: {e.Message}"); }
    }

    private IEnumerator ReportConversionCoroutine(string clickid, string eventType)
    {
        var dataObject = new
        {
            event_type = eventType,
            context = new { ad = new { callback = clickid } },
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        string data = JsonMapper.ToJson(dataObject);
        Debug.Log($"[Ad] 转化回传 上报: eventType={eventType}, data={data}");

        using (UnityWebRequest www = new UnityWebRequest(reportConversionUrl, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data));
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
                Debug.Log($"[Ad] 转化回传成功 eventType={eventType}, Response: {www.downloadHandler.text}");
            else
                Debug.LogError($"[Ad] 转化回传失败 eventType={eventType}, Code: {www.responseCode}, Error: {www.error}");
        }
    }

    // ============================================================
    // 启动场景上报
    // ============================================================

    public void ReportLaunchScene()
    {
#if UNITY_EDITOR
        Debug.Log("[Ad] 编辑器模式 - 模拟启动场景数据上报");
        return;
#endif

        try
        {
            var param = new JsonData();
            int sceneId = 7001;
            long costTime = 3000;
            param["sceneId"] = sceneId;
            param["costTime"] = costTime;

            TT.ReportScene(param,
                (res) => { Debug.Log($"[Ad] 启动场景上报成功: {res.ToJson()}"); },
                (code, msg) => { Debug.LogError($"[Ad] 启动场景上报失败 code:{code} msg:{msg}"); },
                () => { Debug.Log("[Ad] 启动场景上报完成"); }
            );
        }
        catch (Exception e) { Debug.LogWarning($"[Ad] 启动场景上报异常: {e.Message}"); }
    }

    // ============================================================
    // 辅助功能
    // ============================================================

    public void VibrateShort()
    {
#if USE_DY_AD
        TT.VibrateShort(new VibrateShortParam());
#endif
    }

    public void Share(Action onSuccess = null, Action onFailed = null)
    {
        if (isTest)
        {
            Debug.Log("[Share] 测试模式 - 模拟分享成功");
            onSuccess?.Invoke();
            return;
        }
#if USE_DY_AD
        TT.ShareAppMessage(string.Empty,
            (a) => onSuccess?.Invoke(),
            (a) => onFailed?.Invoke()
        );
#else
        onSuccess?.Invoke();
#endif
    }

    // ============================================================
    // 添加桌面
    // ============================================================

    /// <summary>
    /// 必须从用户点击同步调用；禁止放入 CheckShortcut 回调、协程或延迟任务中。
    /// 已添加状态只读取缓存，状态未知时直接请求平台，不等待异步检查。
    /// </summary>
    public void AddToDesktop(Action<bool> onSuccess = null, Action<int, string> onFailed = null,
        Action onAlreadyAdded = null)
    {
        if (isTest)
        {
            bool alreadyAdded = GetTestState("ADS_Desktop_Added", false);
            if (alreadyAdded)
            {
                Debug.Log("[AddToDesktop] 测试模式 - 已添加到桌面（从存档读取）");
                onAlreadyAdded?.Invoke();
                onSuccess?.Invoke(false);
            }
            else
            {
                Debug.Log("[AddToDesktop] 测试模式 - 模拟添加到桌面成功，保存状态");
                SetTestState("ADS_Desktop_Added", true);
                onSuccess?.Invoke(true);
            }
            return;
        }

#if USE_DY_AD
        if (_hasCheckedDesktopShortcut && _isDesktopShortcutAdded)
        {
            Debug.Log("[AddToDesktop] 已添加到桌面");
            onAlreadyAdded?.Invoke();
            onSuccess?.Invoke(false);
            return;
        }

        // 添加桌面必须直接发生在用户点击调用链中，不能等待 CheckShortcut 的异步回调。
        TT.AddShortcut((success) =>
        {
            if (success)
            {
                _hasCheckedDesktopShortcut = true;
                _isDesktopShortcutAdded = true;
                Debug.Log("[AddToDesktop] 添加到桌面成功");
                onSuccess?.Invoke(true);
            }
            else
            {
                Debug.LogError("[AddToDesktop] 添加到桌面失败");
                onFailed?.Invoke(-1, "添加失败");
            }
        });
#else
        Debug.Log("[AddToDesktop] 非抖音平台 - 模拟添加到桌面");
        onSuccess?.Invoke(true);
#endif
    }

    /// <summary>异步查询仅用于初始化或刷新状态；回调中不得发起添加桌面。</summary>
    public void CheckDesktopShortcut(Action<bool> onResult)
    {
        if (isTest)
        {
            bool added = GetTestState("ADS_Desktop_Added", false);
            Debug.Log($"[CheckDesktopShortcut] 测试模式 - 桌面快捷方式: {(added ? "已添加" : "未添加")}");
            onResult?.Invoke(added);
            return;
        }

#if USE_DY_AD
        TT.CheckShortcut((isAdded) =>
        {
            _hasCheckedDesktopShortcut = true;
            _isDesktopShortcutAdded = isAdded;
            Debug.Log($"[CheckDesktopShortcut] 检查结果: {isAdded}");
            onResult?.Invoke(isAdded);
        });
#else
        Debug.Log("[CheckDesktopShortcut] 非抖音平台 - 模拟未添加到桌面");
        onResult?.Invoke(false);
#endif
    }

    // ============================================================
    // 抖音侧边栏
    // ============================================================

    /// <summary>通知当前场景的侧边栏面板刷新；广告管理器不持有任何场景UI引用。</summary>
    private void HideSideBarUI()
    {
        SideBarManager.Instance?.RefreshButtonState();
    }

    public void CheckSideBarSupport(Action onSupported = null, Action onNotSupported = null)
    {
        Debug.Log("[AdManagerImpl] 【CheckSideBarSupport】被调用");
#if USE_DY_AD
        Debug.Log("[AdManagerImpl] 【CheckSideBarSupport】调用 TT.CheckScene(SideBar)");
        TT.CheckScene(TTSideBar.SceneEnum.SideBar, (success) =>
        {
            Debug.Log($"[AdManagerImpl] 【CheckSideBarSupport】TT.CheckScene 回调 success={success}");
            if (success) { onSupported?.Invoke(); }
            else { onNotSupported?.Invoke(); }
        }, () => { Debug.Log("[AdManagerImpl] 【CheckSideBarSupport】TT.CheckScene complete"); }, (errCode, errMsg) =>
        {
            Debug.Log($"[AdManagerImpl] 【CheckSideBarSupport】TT.CheckScene 失败 code={errCode} msg={errMsg}");
            onNotSupported?.Invoke();
        });
#else
        Debug.Log("[SideBar] 测试模式 - 模拟支持");
        onSupported?.Invoke();
#endif
    }

    public void OpenSideBar(Action onSuccess = null, Action onFailed = null, Action onReturn = null)
    {
        if (isTest)
        {
            // 测试模式：模拟打开侧边栏成功，然后模拟从侧边栏返回，保存状态
            Debug.Log("[SideBar] 测试模式 - 模拟打开侧边栏成功");
            onSuccess?.Invoke();

            // 模拟从侧边栏返回
            _isFromSideBar = true;
            Debug.Log("[SideBar] 测试模式 - 模拟从侧边栏返回");
            onReturn?.Invoke();
            return;
        }
#if USE_DY_AD
        TT.GetAppLifeCycle().OnShow += (System.Collections.Generic.Dictionary<string, object> param) =>
        {
            if ((string)param["launchFrom"] == "homepage" && (string)param["location"] == "sidebar_card")
            {
                _isFromSideBar = true;
                Debug.Log("[SideBar] 从侧边栏返回游戏");
                onReturn?.Invoke();
            }
        };

        var jsonData = new TTSDK.UNBridgeLib.LitJson.JsonData { ["scene"] = "sidebar" };

        TT.NavigateToScene(jsonData,
            () => { Debug.Log("[SideBar] 打开成功"); onSuccess?.Invoke(); },
            () => { },
            (errCode, errMsg) => { Debug.LogError($"[SideBar] 打开失败 code:{errCode} msg:{errMsg}"); onFailed?.Invoke(); });
#else
        Debug.Log("[SideBar] 测试模式 - 模拟打开成功");
        onSuccess?.Invoke();
#endif
    }

    private string SideBarRewardKey => "ADS_SideBarRewardLastClaimDate";

    private string GetTodayDateString()
    {
        return DateTime.Now.ToString("yyyy-MM-dd");
    }

    public bool IsSideBarRewardClaimed
    {
        get
        {
            string lastDate = TTSaveManager.Instance.GetString(SideBarRewardKey, "");
            string today = GetTodayDateString();
            bool claimed = lastDate == today;
            Debug.Log($"[AdManagerImpl] 【IsSideBarRewardClaimed.get】lastDate={lastDate}, today={today}, 返回={claimed}");
            return claimed;
        }
        set
        {
            string date = value ? GetTodayDateString() : "";
            Debug.Log($"[AdManagerImpl] 【IsSideBarRewardClaimed.set】key={SideBarRewardKey}, 新值={date}");
            TTSaveManager.Instance.SetString(SideBarRewardKey, date);
            TTSaveManager.Instance.Save();
        }
    }

    public void ClaimSideBarReward( Action onClaimed = null)
    {
        if (IsSideBarRewardClaimed)
        {
            Debug.Log("[SideBar] 奖励已领取");
            return;
        }

        Debug.Log($"[SideBar] 领取今日奖励，日期={GetTodayDateString()}");
        IsSideBarRewardClaimed = true;
        onClaimed?.Invoke();
    }

    // ============================================================
    // 推荐流 启动场景 / OnFeedStatusChange / DayCheckManager
    // ============================================================

    /// <summary>
    /// 推荐流场景枚举（与DYADSS的feed_game_scene保持一致）
    /// </summary>
    public enum FeedGameScene
    {
        UserAcquisition,    // 获客
        OfflineReward,      // 离线收益
        StaminaRecovery,    // 体力恢复
        ImportantEvent      // 重要事件提醒
    }

    // 推荐流各场景的content_id
    public string ContentId_UserAcquisition = "";
    public string ContentId_OfflineReward = "";
    public string ContentId_StaminaRecovery = "";
    public string ContentId_ImportantEvent = "";

    /// <summary>
    /// 获取启动场景：上报场景数据，若为推荐流进入则注册OnFeedStatusChange监听
    /// 与DYADSS.获取启动场景()逻辑完全一致
    /// </summary>
    /// <summary>
    /// 根据推荐流场景获取对应的content_id
    /// </summary>
    private string GetFeedSceneId(FeedGameScene sceneId)
    {
        switch (sceneId)
        {
            case FeedGameScene.UserAcquisition:
                return ContentId_UserAcquisition;
            case FeedGameScene.OfflineReward:
                return ContentId_OfflineReward;
            case FeedGameScene.StaminaRecovery:
                return ContentId_StaminaRecovery;
            case FeedGameScene.ImportantEvent:
                return ContentId_ImportantEvent;
            default:
                break;
        }
        return ContentId_UserAcquisition;
    }

    // ============================================================
    // 推荐流 直玩就绪状态（StoreFeedData）
    // ============================================================

    /// <summary>
    /// 兼容旧接口：直接写入指定复访场景的默认就绪状态，不判断新一天或业务条件。
    /// </summary>
    /// <param name="scene">推荐流场景枚举值</param>
    /// <param name="contentId">场景对应的content_id</param>
    /// <param name="readyTimestampMs">就绪毫秒级时间戳</param>
    public void ReportFeedReadyStatus(int scene, string contentId, string readyTimestampMs)
    {
        var parm = new StoreFeedDataParam();
        parm.Scene = scene;
        parm.Status = 1;
        parm.ContentID = contentId;
        parm.LeftValue = "timeStampMs";
        parm.Operator = ">=";
        parm.RightValue = readyTimestampMs;
        parm.Success = (result) => { Debug.Log($"[AdManagerImpl] 直玩就绪状态 成功{result}"); };
        parm.Fail = (errInfo) => { Debug.Log($"[AdManagerImpl] 直玩就绪状态 失败{errInfo}"); };

        TT.StoreFeedData(parm);
        Debug.Log($"[AdManagerImpl] 直接托管直玩默认就绪状态 scene:{scene}, contentId:{contentId}");
    }

    // ============================================================
    // 推荐流 订阅
    // ============================================================

    /// <summary>
    /// 兼容旧入口：不再按单场景查询，统一进入全场景订阅主流程。
    /// </summary>
    /// <param name="sceneId">推荐流场景</param>
    public void CheckFeedSubscription(FeedGameScene sceneId)
    {
        Debug.Log($"[AdManagerImpl] 旧单场景入口 {sceneId} 已转为全场景订阅");
        FeedRecommendationManager.Instance.TryRequestFeedSubscribeAllSceneFromUserGesture();
    }

    // ============================================================
    // 游戏消息订阅
    // ============================================================

    /// <summary>
    /// 请求游戏消息订阅，与DYADSS.游戏消息订阅()逻辑完全一致
    /// </summary>
    public void RequestGameMessageSubscription()
    {
        if (isTest) return;
#if UNITY_EDITOR
        Debug.Log("[AdManagerImpl] 编辑器模式 - 模拟游戏消息订阅");
        return;
#endif
#if USE_DY_AD
        List<string> list = new List<string>() { "MSG21729325160217624367435981998399" };
        TT.RequestSubscribeMessage(list);
        Debug.Log("[AdManagerImpl] 游戏消息订阅请求已发送");
#endif
    }

    // ============================================================
    // 排行榜
    // ============================================================

    /// <summary>
    /// 提交分数到排行榜，与DYADSS.OnSubmitScoreToRank()逻辑完全一致
    /// </summary>
    /// <param name="score">分数</param>
    public void SubmitScoreToRank(int score)
    {
#if UNITY_EDITOR
        Debug.Log($"[AdManagerImpl] 编辑器模式 - 模拟提交分数到排行榜: {score}");
        return;
#endif
#if USE_DY_AD
        var paramJson = new JsonData
        {
            ["dataType"] = 0,               // 0:数字类型，1:枚举类型
            ["value"] = score.ToString(),   // 分数值
            ["priority"] = 0,               // 优先级（枚举类型时生效）
            ["zoneId"] = "default"          // 分区ID，默认"default"
        };

        Debug.Log($"[AdManagerImpl] 提交分数参数: {paramJson.ToJson()}");

        TT.SetImRankData(paramJson, (success, message) =>
        {
            if (success)
            {
                Debug.Log("[AdManagerImpl] 分数上传成功！");
            }
            else
            {
                Debug.LogError($"[AdManagerImpl] 分数上传失败: {message}");
            }
        });
#endif
    }

    /// <summary>
    /// 显示排行榜，与DYADSS.OnShowRank()逻辑完全一致
    /// </summary>
    public void ShowRank()
    {
#if UNITY_EDITOR
        Debug.Log("[AdManagerImpl] 编辑器模式 - 模拟显示排行榜");
        return;
#endif
#if USE_DY_AD
        var paramJson = new JsonData
        {
            ["rankType"] = "day",
            ["dataType"] = 0,
            ["relationType"] = "all",
            ["suffix"] = "关",
            ["rankTitle"] = "通关数",
            ["zoneId"] = "default"
        };

        TT.GetImRankList(paramJson, (success, msg) =>
        {
            if (!success)
            {
                Debug.LogError($"[AdManagerImpl] 无法显示排行榜: {msg}");
            }
        });
#endif
    }

    // ============================================================
    // 测试模式辅助：使用 PlayerPrefs 保存状态，持久化测试数据
    // ============================================================

    /// <summary>保存测试状态（使用 PlayerPrefs + Save 确保持久化）</summary>
    private void SetTestState(string key, bool value)
    {
        TTSaveManager.Instance.SetBool(key, value);
        TTSaveManager.Instance.Save();
    }

    /// <summary>读取测试状态</summary>
    private bool GetTestState(string key, bool defaultValue = false)
    {
        return TTSaveManager.Instance.GetBool(key, defaultValue);
    }

    /// <summary>清除所有TTSDK存储数据（调试用）</summary>
    public void ClearAllTTStorage()
    {
        TTSaveManager.Instance.DeleteAll();
        TTSaveManager.Instance.Save();
        Debug.Log("[AdManagerImpl] 已清除所有TTSDK存储数据");
    }

}
