using System;
using System.Collections;
using System.Collections.Generic;
using TTSDK;
using TTSDK.UNBridgeLib.LitJson;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 推荐流接入的核心管理器，负责解析启动参数、获取配置并转发平台生命周期事件。
/// </summary>
public sealed class FeedRecommendationManager : MonoBehaviour
{
    // 宿主前缀可能变化，只识别直玩场景后缀，禁止枚举宿主白名单。
    private const string FeedSceneCode = "3041";
    private const string ChannelKey = "feed_game_channel";
    private const string ContentIdKey = "feed_game_content_id";
    private const string SceneKey = "feed_game_scene";

    private static FeedRecommendationManager instance;
    private FeedRecommendationConfig config;
    private FeedLaunchContext context;
    private bool listenerRegistered;
    private bool lifecycleRegistered;
    private bool enterHandled;
    private bool enterPending;
    private bool exitPending;
    private bool readyReported;
    private bool isDirectPlay;
    private bool isCardVisible;
    private bool isInFullGame;
    private bool cardShowDispatched;
    private bool subscriptionPromptAttemptedThisSession;
    private bool returnScenesReadyReportedThisSession;
    private bool returnScenesReadyRequestInProgress;
    private float launchStartedAt;
    private int launchVersion;
    private bool isAppVisible;
    private bool configurationResolving;
    private bool resourcesReadyObserved;
    private UnityWebRequest activeLaunchRequest;
    private readonly List<FeedLifecycleRecord> lifecycleHistory = new List<FeedLifecycleRecord>();
#if UNITY_EDITOR
    private bool editorPreview;
    private bool debugAutoSceneReady = true;
    private bool debugReadinessPending;
    private int debugPendingSceneId = 7001;
    private FeedRecommendationConfig debugConfig;
    private bool debugUseRemote;
#endif
    private OnFeedStatusChangeCallback feedStatusCallback;
    private TTAppLifeCycle appLifeCycle;
    private TTAppLifeCycle.OnShowEventWithDict appShowCallback;
    private TTAppLifeCycle.OnAppHideEvent appHideCallback;

    /// <summary>
    /// 获取全局唯一管理器；首次访问时自动创建常驻对象。
    /// </summary>
    public static FeedRecommendationManager Instance
    {
        get
        {
            if (instance != null) return instance;
            var go = new GameObject(nameof(FeedRecommendationManager));
            instance = go.AddComponent<FeedRecommendationManager>();
            DontDestroyOnLoad(go);
            return instance;
        }
    }

    /// <summary>体验方案配置已解析，可以开始预加载资源。</summary>
    public event Action<FeedLaunchContext> SchemePrepared;

    /// <summary>平台通知进入推荐流体验，可以开放正式交互。</summary>
    public event Action<FeedLaunchContext> SchemeEntered;

    /// <summary>平台每次通知进入完整小游戏时触发；首次进入和退出后再次进入都会触发。</summary>
    public event Action<FeedLaunchContext> FeedEntered;

    /// <summary>平台通知退出完整小游戏回到推荐流。</summary>
    public event Action<FeedLaunchContext> FeedExited;

    /// <summary>直玩卡片由隐藏预加载切换为可见；卡片体验、配音和字幕从这里开始或恢复。</summary>
    public event Action<FeedLaunchContext> CardShown;

    /// <summary>直玩卡片划走或容器隐藏；卡片体验、配音和字幕从这里暂停。</summary>
    public event Action<FeedLaunchContext> CardHidden;

    /// <summary>远程和本地配置均未命中时触发，参数为失败原因。</summary>
    public event Action<string> ConfigurationFailed;

    /// <summary>当前已解析的推荐流启动上下文；尚未解析完成时为 null。</summary>
    public FeedLaunchContext CurrentContext => context;
    public static FeedRecommendationManager ExistingInstance => instance;
    public int LaunchVersion => launchVersion;
    public bool IsDirectPlay => isDirectPlay;
    public bool IsCardVisible => cardShowDispatched;
    public bool IsInFullGame => isInFullGame;
    public bool IsAppVisible => isAppVisible;
    public bool IsSceneReady => readyReported;
    public bool IsResolvingConfiguration => configurationResolving;
    public FeedLifecycleStage LifecycleStage { get; private set; }
    public string LastConfigurationFailure { get; private set; }
    public IReadOnlyList<FeedLifecycleRecord> LifecycleHistory => lifecycleHistory;
    public event Action<FeedLifecycleRecord> LifecycleChanged;
    /// <summary>完整游戏进入后台，与卡片 OnHide 分开。</summary>
    public event Action<FeedLaunchContext> FullGameHidden;
    /// <summary>完整游戏从后台恢复，不重复触发首次进入。</summary>
    public event Action<FeedLaunchContext> FullGameShown;
    /// <summary>当前内容结束；切换 ID 时释放视频并恢复该体验占用的暂停状态。</summary>
    public event Action<FeedLaunchContext> SessionEnded;
    public bool IsEditorPreview
    {
        get
        {
#if UNITY_EDITOR
            return editorPreview;
#else
            return false;
#endif
        }
    }

    public bool IsCurrentSession(int version) => isDirectPlay && version == launchVersion;

    private void RecordLifecycle(FeedLifecycleStage stage, string detail)
    {
        LifecycleStage = stage;
        var record = new FeedLifecycleRecord(stage, detail, context, launchVersion);
        if (lifecycleHistory.Count >= 120) lifecycleHistory.RemoveAt(0);
        lifecycleHistory.Add(record);
        LifecycleChanged?.Invoke(record);
    }

#if UNITY_EDITOR
    /// <summary>兼容原调试入口，保留正式远程优先解析。</summary>
    public void DebugSimulateLaunch(FeedUserChannel channel, string contentId, int sceneId)
    {
        ResetState();
        editorPreview = true;
        isDirectPlay = true;
        launchStartedAt = Time.realtimeSinceStartup;
        RecordLifecycle(FeedLifecycleStage.LaunchStarted, "编辑器模拟启动");
        StartCoroutine(ResolveConfig(channel, contentId, sceneId, launchVersion));
    }

    /// <summary>精确预览所选配置。默认仅使用所选本地资产，不受服务器旧配置覆盖。</summary>
    public void DebugPreviewContent(FeedRecommendationConfig asset, FeedContentConfig item,
        bool useRemote = false, bool showCard = true, bool autoReady = true)
    {
        if (asset == null || !FeedPreviewSequence.IsValid(item)) return;
        ResetState();
        editorPreview = true;
        debugConfig = asset;
        debugUseRemote = useRemote;
        debugAutoSceneReady = autoReady;
        isDirectPlay = true;
        launchStartedAt = Time.realtimeSinceStartup;
        RecordLifecycle(FeedLifecycleStage.LaunchStarted, "编辑器内容：" + item.contentId);
        StartCoroutine(ResolveConfig(item.channel, item.contentId, item.returnSceneId, launchVersion));
        if (showCard) HandleAppShow(null);
    }

    public bool DebugReadinessPending => debugReadinessPending;
    public void DebugSimulateSceneReady()
    {
        if (!isDirectPlay || context == null || readyReported) return;
        if (!debugReadinessPending)
        {
            Debug.LogWarning("[推荐流调试] 资源或视频首帧尚未就绪，不能提前上报 7001。");
            return;
        }
        debugAutoSceneReady = true;
        ReportSceneReadyInternal(debugPendingSceneId);
    }
    public void DebugSimulateShow() => HandleAppShow(null);
    public void DebugSimulateHide() => HandleAppHide();
    public void DebugSimulateEnter() => HandleFeedEnterSignal();
    public void DebugSimulateExit() => HandleFeedExitSignal();
    public void DebugEndPreview()
    {
        if (editorPreview) ResetState();
    }
    public void DebugClearLifecycleHistory() => lifecycleHistory.Clear();
#endif

    /// <summary>仅供玩家主动点击时调用；每次运行最多尝试拉起一次订阅弹窗。</summary>
    public void TryRequestFeedSubscribeAllSceneFromUserGesture()
    {
        if (subscriptionPromptAttemptedThisSession)
        {
            return;
        }

        subscriptionPromptAttemptedThisSession = true;
        CheckFeedSubscribeStatus(subscribed =>
        {
            if (!subscribed)
            {
                RequestFeedSubscribeAllScene();
            }
            else
            {
                RefreshReturnSceneReadyData();
            }
        });
    }

    /// <summary>查询用户是否已经完成推荐流全场景订阅。</summary>
    public void CheckFeedSubscribeStatus(Action<bool> completed)
    {
#if UNITY_EDITOR
        Debug.Log("[推荐流订阅] 编辑器中跳过全场景订阅状态查询");
        completed?.Invoke(true);
        return;
#else
        if (!CanIUse.CheckFeedSubscribeStatus)
        {
            Debug.LogWarning("[推荐流订阅] 当前环境不支持查询订阅状态");
            completed?.Invoke(true);
            return;
        }

        TT.CheckFeedSubscribeStatus(
            CreateAllSceneSubscribeParameters(),
            result =>
            {
                bool subscribed = false;
                string field;
                if (!TryReadSubscriptionFlag(result, out subscribed, out field))
                {
                    Debug.LogWarning($"[推荐流订阅] 回包未包含可识别的 subscribed/status/success，按未订阅处理，raw={result?.ToJson()}");
                }

                Debug.Log($"[推荐流订阅] 全场景订阅状态：subscribed={subscribed}, field={field ?? "unknown"}, raw={result?.ToJson()}");
                completed?.Invoke(subscribed);
            },
            (code, message) =>
            {
                Debug.LogWarning($"[推荐流订阅] 查询全场景订阅状态失败：{code} {message}");
                completed?.Invoke(true);
            },
            null);
#endif
    }

    /// <summary>请求用户授权推荐流全场景订阅。</summary>
    public void RequestFeedSubscribeAllScene()
    {
        // 当前模板以全场景申请为主链：一旦发起申请，立即把 Scene 1/2/3 写为默认就绪。
        // 订阅回包与 StoreFeedData 回包分别记录，不用体力、离线收益或重要事件作前置判断。
        RefreshReturnSceneReadyData();
#if UNITY_EDITOR
        Debug.Log("[推荐流订阅] 编辑器中跳过全场景订阅请求");
        return;
#else
        TT.RequestFeedSubscribe(
            CreateAllSceneSubscribeParameters(),
            result =>
            {
                Debug.Log($"[推荐流订阅] 用户全场景订阅返回：{result?.ToJson()}");
            },
            (code, message) => Debug.LogWarning($"[推荐流订阅] 请求全场景订阅失败：{code} {message}"),
            null);
#endif
    }

    private static JsonData CreateAllSceneSubscribeParameters()
    {
        return new JsonData
        {
            ["type"] = "play",
            ["allScene"] = true
        };
    }

    private static bool TryReadSubscriptionFlag(JsonData result, out bool subscribed, out string field)
    {
        subscribed = false;
        field = null;
        if (result == null) return false;

        string[] candidates = { "subscribed", "status", "success" };
        foreach (string candidate in candidates)
        {
            if (!result.Keys.Contains(candidate)) continue;
            field = candidate;
            JsonData value = result[candidate];
            try
            {
                subscribed = (bool)value;
                return true;
            }
            catch
            {
                string text = value == null ? string.Empty : value.ToString().Trim();
                bool booleanValue;
                if (bool.TryParse(text, out booleanValue))
                {
                    subscribed = booleanValue;
                    return true;
                }

                int numericValue;
                if (int.TryParse(text, out numericValue))
                {
                    subscribed = numericValue != 0;
                    return true;
                }
            }
        }

        field = null;
        return false;
    }

    /// <summary>优先读取远程配置并上报全部复访 ContentID；远程失败时回退本地配置。</summary>
    public void RefreshReturnSceneReadyData()
    {
        if (returnScenesReadyReportedThisSession || returnScenesReadyRequestInProgress)
        {
            return;
        }

        StartCoroutine(RefreshReturnSceneReadyDataCoroutine());
    }

    private IEnumerator RefreshReturnSceneReadyDataCoroutine()
    {
        // 复访入口生成前必须先调用 StoreFeedData。这里不能只依赖构建时打进包内的配置，
        // 否则游戏发布后新增的复访 ContentID 无法被旧版本识别。
        returnScenesReadyRequestInProgress = true;
        FeedRecommendationConfig local = LoadLocalConfig();
        if (local == null)
        {
            returnScenesReadyRequestInProgress = false;
            yield break;
        }

        IEnumerable<FeedContentConfig> contents = local.Contents;
        string source = "本地";
        if (!string.IsNullOrWhiteSpace(local.RemoteConfigUrl))
        {
            // 远程成功时以服务器内容为准；只有网络或 JSON 解析失败才使用包内配置兜底。
            FeedRemoteConfig remote = null;
            yield return RequestRemoteConfig(local.RemoteConfigUrl, local.RemoteTimeoutSeconds, value => remote = value);
            if (remote != null)
            {
                contents = ConvertRemoteContents(remote);
                source = "远程";
            }
            else
            {
                Debug.LogWarning("[推荐流复访] 远程配置不可用，使用本地配置上报 StoreFeedData");
            }
        }

        string readyTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var sceneContentIds = new Dictionary<int, string>();
        string fallbackContentId = null;
        foreach (FeedContentConfig content in contents)
        {
            // StoreFeedData 只处理复访渠道；每个场景优先使用显式 ContentID。
            if (content == null || content.channel != FeedUserChannel.ReturnVisit ||
                string.IsNullOrWhiteSpace(content.contentId) || content.returnSceneId < 1 || content.returnSceneId > 3)
            {
                continue;
            }

            string contentId = content.contentId.Trim();
            if (fallbackContentId == null) fallbackContentId = contentId;
            if (!sceneContentIds.ContainsKey(content.returnSceneId))
            {
                sceneContentIds.Add(content.returnSceneId, contentId);
            }
        }

        returnScenesReadyRequestInProgress = false;
        if (fallbackContentId == null)
        {
            Debug.LogWarning($"[推荐流复访] {source}配置中没有有效的复访 ContentID");
            yield break;
        }

        for (int sceneId = 1; sceneId <= 3; sceneId++)
        {
            string contentId;
            if (!sceneContentIds.TryGetValue(sceneId, out contentId))
            {
                contentId = fallbackContentId;
                Debug.Log($"[推荐流复访] Scene {sceneId} 未配置独立 ContentID，复用默认复访 ID={contentId}");
            }
            StoreReturnSceneReady(sceneId, contentId, readyTimestamp);
        }

        returnScenesReadyReportedThisSession = true;
        Debug.Log($"[推荐流复访] 已按{source}配置提交 Scene 1/2/3 三个默认就绪上报");
    }

    private static List<FeedContentConfig> ConvertRemoteContents(FeedRemoteConfig remote)
    {
        // 将精简服务器字段转换为运行时配置；视频/试玩参数不会参与 StoreFeedData，
        // 但仍完整保留，便于后续统一复用 FeedContentConfig。
        var result = new List<FeedContentConfig>();
        if (remote.items == null) return result;
        foreach (FeedRemoteConfigItem item in remote.items)
        {
            if (item == null || item.c != (int)FeedUserChannel.ReturnVisit) continue;
            result.Add(new FeedContentConfig
            {
                contentId = item.id,
                channel = FeedUserChannel.ReturnVisit,
                returnSceneId = item.r,
                scheme = (FeedExperienceScheme)item.s,
                payload = item.p
            });
        }
        return result;
    }

    private IEnumerator RequestRemoteConfig(string url, int timeout, Action<FeedRemoteConfig> completed)
    {
        // 本请求专门获取完整配置列表，不按启动 ContentID 过滤。
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeout;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                completed(null);
                yield break;
            }

            try
            {
                FeedRemoteConfig remote = JsonUtility.FromJson<FeedRemoteConfig>(request.downloadHandler.text);
                completed(remote != null && remote.items != null ? remote : null);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[推荐流复访] 远程配置解析失败：" + exception.Message);
                completed(null);
            }
        }
    }

    private static void StoreReturnSceneReady(int sceneId, string contentId, string readyTimestamp)
    {
        // RightValue 使用当前毫秒时间戳，表示该复访内容从现在开始已经可以被平台召回。
#if UNITY_EDITOR
        Debug.Log($"[推荐流复访测试] 模拟 StoreFeedData：scene={sceneId}, contentId={contentId}, readyTime={readyTimestamp}");
#else
        TT.StoreFeedData(new StoreFeedDataParam
        {
            Scene = sceneId,
            Status = 1,
            ContentID = contentId,
            LeftValue = "timeStampMs",
            Operator = ">=",
            RightValue = readyTimestamp,
            Success = result => Debug.Log($"[推荐流复访] StoreFeedData 成功：scene={sceneId}, contentId={contentId}, result={result}"),
            Fail = error => Debug.LogError($"[推荐流复访] StoreFeedData 失败：scene={sceneId}, contentId={contentId}, error={error}")
        });
#endif
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            if (activeLaunchRequest != null) AbortNativeRequest(activeLaunchRequest);
            activeLaunchRequest = null;
            if (isDirectPlay) SessionEnded?.Invoke(context);
            instance = null;
        }

        if (listenerRegistered && feedStatusCallback != null)
        {
            TT.OffFeedStatusChange(feedStatusCallback);
            listenerRegistered = false;
        }

        if (lifecycleRegistered && appLifeCycle != null)
        {
            if (appShowCallback != null) appLifeCycle.OnShow -= appShowCallback;
            if (appHideCallback != null) appLifeCycle.OnHide -= appHideCallback;
            lifecycleRegistered = false;
        }
    }

    /// <summary>
    /// 处理 TTSDK 启动参数。应在游戏获取 LaunchOption 后尽早调用。
    /// </summary>
    /// <returns>属于有效推荐流启动时返回 true，否则返回 false。</returns>
    public bool HandleLaunchOptions(LaunchOption options)
    {
        ResetState();
        launchStartedAt = Time.realtimeSinceStartup;

        // 非 3041 场景按普通游戏启动处理，不注册推荐流流程。
        if (options == null || !IsDirectPlayScene(options.Scene)) return false;

        string channelText = Query(options, ChannelKey);
        string contentId = Query(options, ContentIdKey);
        FeedUserChannel channel = ParseChannel(channelText);
        if (channel == FeedUserChannel.Unknown || string.IsNullOrWhiteSpace(contentId))
        {
            Debug.LogError($"[推荐流] 启动参数无效：channel={channelText}, contentId={contentId}");
            return false;
        }

        int sceneId;
        int.TryParse(Query(options, SceneKey), out sceneId);
        isDirectPlay = true;
        // 先监听卡片可见性和完整游戏进出，再异步解析配置，避免快速回调造成事件丢失。
        RegisterPlatformListeners();
        RecordLifecycle(FeedLifecycleStage.LaunchStarted, "平台推荐流启动");
        StartCoroutine(ResolveConfig(channel, contentId, sceneId, launchVersion));
        return true;
    }

    /// <summary>
    /// 当前推荐流方案的场景或首帧已可展示时，向平台上报场景就绪。
    /// </summary>
    public void ReportSceneReady(int platformSceneId = 7001)
    {
        if (context == null || readyReported) return;
        ReportSceneReadyInternal(platformSceneId);
    }

    /// <summary>远程和本地配置都失败时，项目安全兜底页面加载完成后调用。</summary>
    public void ReportFallbackSceneReady(int platformSceneId = 7001)
    {
        if (readyReported) return;
        ReportSceneReadyInternal(platformSceneId);
    }

    /// <summary>按 3041 后缀识别各宿主的直玩入口，不限制宿主名称或前缀。</summary>
    public static bool IsDirectPlayScene(string scene)
    {
        return !string.IsNullOrEmpty(scene) &&
               scene.EndsWith(FeedSceneCode, StringComparison.Ordinal);
    }

    private void ReportSceneReadyInternal(int platformSceneId)
    {
        if (!isDirectPlay || readyReported) return;
        if (!resourcesReadyObserved)
        {
            resourcesReadyObserved = true;
            RecordLifecycle(FeedLifecycleStage.ResourcesReady, "资源 / 可展示首帧已就绪");
        }
#if UNITY_EDITOR
        if (editorPreview && !debugAutoSceneReady)
        {
            if (!debugReadinessPending) RecordLifecycle(FeedLifecycleStage.ResourcesReady, "资源/首帧就绪，等待手动上报 7001");
            debugReadinessPending = true;
            debugPendingSceneId = platformSceneId;
            return;
        }
        debugReadinessPending = false;
#endif
        // 7001 上报适用于平台开放的各宿主，不得增加 hostName/IsDouyin 前置拦截。
        // 在调用入口处统一防重，确保一次启动只上报一次场景就绪。
      readyReported = true;
      RecordLifecycle(FeedLifecycleStage.SceneReady, "ReportScene(" + platformSceneId + ")，本次启动防重");
      int cost = Mathf.Max(1, Mathf.RoundToInt((Time.realtimeSinceStartup - launchStartedAt) * 1000f));
#if UNITY_EDITOR
      Debug.Log($"[推荐流调试] 模拟 ReportScene：sceneId={platformSceneId}, costTime={cost}");
#else
      var parameters = new JsonData { ["sceneId"] = platformSceneId, ["costTime"] = cost };

        TT.ReportScene(parameters,
            _ => Debug.Log($"[推荐流] ReportScene 成功，costTime={cost}"),
          (code, message) => Debug.LogError($"[推荐流] ReportScene 失败：{code} {message}"),
          null);
#endif
        if (enterPending) DispatchEnter();
        else DispatchCardShownIfReady();
  }

    private IEnumerator ResolveConfig(FeedUserChannel channel, string contentId, int sceneId, int version)
    {
        configurationResolving = true;
        RecordLifecycle(FeedLifecycleStage.ResolvingConfiguration, "匹配 ContentID=" + contentId);
        FeedRecommendationConfig local = null;
        bool useRemote = true;
#if UNITY_EDITOR
        if (editorPreview && debugConfig != null)
        {
            local = debugConfig;
            useRemote = debugUseRemote;
        }
#endif
        if (local == null) local = LoadLocalConfig();
        FeedContentConfig selected = null;

        // 优先读取远程配置，便于不发版动态调整推荐内容。
        if (useRemote && local != null && !string.IsNullOrWhiteSpace(local.RemoteConfigUrl))
        {
            yield return RequestRemote(local.RemoteConfigUrl, local.RemoteTimeoutSeconds,
                channel, contentId, value => selected = value);
        }

        // 已切换 ID 的旧请求不能覆盖新预览，也不能补发旧生命周期。
        if (!IsCurrentSession(version)) yield break;
        configurationResolving = false;
        // 远程请求失败或没有命中时，再读取随包发布的本地兜底配置。
        if (selected == null && local != null)
        {
            selected = local.Find(channel, contentId);
            if (selected != null) Debug.Log("[推荐流] 使用本地兜底配置");
        }

        if (selected == null && local != null)
        {
            selected = local.FindFirst(channel);
            if (selected != null)
            {
                Debug.LogWarning($"[推荐流] 未知 ContentID={contentId}，回退 {channel} 首条安全配置 {selected.contentId}");
                contentId = selected.contentId;
            }
        }

        if (selected == null)
        {
            string reason = $"远程和本地均无配置：channel={channel}, contentId={contentId}";
            Debug.LogError("[推荐流] " + reason);
            LastConfigurationFailure = reason;
            // 安全兜底也保留启动上下文，允许首帧就绪后正常接收 OnShow/FeedEnter。
            context = new FeedLaunchContext(channel, contentId, sceneId, FeedExperienceScheme.TrialLevel, string.Empty);
            RecordLifecycle(FeedLifecycleStage.ConfigurationFailed, reason);
            ConfigurationFailed?.Invoke(reason);
            yield break;
        }

        int effectiveSceneId = sceneId;
        if (channel == FeedUserChannel.ReturnVisit)
        {
            // 平台本次真实下发的 Scene 1/2/3 优先；缺失或越界时才回退配置。
            if (sceneId >= 1 && sceneId <= 3)
            {
                effectiveSceneId = sceneId;
            }
            else if (selected.returnSceneId >= 1 && selected.returnSceneId <= 3)
            {
                effectiveSceneId = selected.returnSceneId;
            }
        }

        context = new FeedLaunchContext(channel, contentId, effectiveSceneId, selected.scheme, selected.payload);
        Debug.Log("[推荐流] 已匹配 " + context);
        RecordLifecycle(FeedLifecycleStage.SchemePrepared, "方案已匹配，进入隐藏预加载");
        SchemePrepared?.Invoke(context);
        if (!IsCurrentSession(version)) yield break;

        // 平台事件可能早于异步配置返回，此处补发之前暂存的状态。
        if (exitPending)
        {
            FeedExited?.Invoke(context);
        }
        else if (enterPending)
        {
            DispatchEnter();
        }
        else
        {
            DispatchCardShownIfReady();
        }
    }

    private IEnumerator RequestRemote(string url, int timeout, FeedUserChannel channel,
        string contentId, Action<FeedContentConfig> completed)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = timeout;
            activeLaunchRequest = request;
            Debug.Log("[推荐流] 请求远程配置：" + url);
            yield return request.SendWebRequest();
            if (activeLaunchRequest == request) activeLaunchRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[推荐流] 远程请求失败，回退本地：{request.responseCode} {request.error}");
                completed(null);
                yield break;
            }

            try
            {
                FeedRemoteConfig remote = JsonUtility.FromJson<FeedRemoteConfig>(request.downloadHandler.text);
                FeedContentConfig value = remote == null ? null : remote.Find(channel, contentId);
                Debug.Log(value == null ? "[推荐流] 远程无当前 ID，回退本地" : "[推荐流] 使用远程配置");
                completed(value);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[推荐流] 远程解析失败，回退本地：" + exception.Message);
                completed(null);
            }
        }
    }

    private void RegisterPlatformListeners()
    {
        if (!listenerRegistered)
        {
            listenerRegistered = true;
            feedStatusCallback = result =>
            {
                if (result.Type == FeedStatusEnum.FeedEnter) HandleFeedEnterSignal();
                else if (result.Type == FeedStatusEnum.FeedExit) HandleFeedExitSignal();
            };
            TT.OnFeedStatusChange(feedStatusCallback);
        }

        if (lifecycleRegistered) return;
        lifecycleRegistered = true;
        appLifeCycle = TT.GetAppLifeCycle();
        appShowCallback = HandleAppShow;
        appHideCallback = HandleAppHide;
        appLifeCycle.OnShow += appShowCallback;
        appLifeCycle.OnHide += appHideCallback;
    }

    private void HandleAppShow(Dictionary<string, object> args)
    {
        if (!isDirectPlay) return;
        bool wasVisible = isAppVisible;
        isAppVisible = true;
        if (isInFullGame)
        {
            if (!wasVisible && context != null && readyReported && !enterPending)
            {
                RecordLifecycle(FeedLifecycleStage.FullGameShown, "完整游戏前台恢复，不重复 SchemeEntered");
                FullGameShown?.Invoke(context);
            }
            return;
        }
        if (isCardVisible) return;
        isCardVisible = true;
        RecordLifecycle(FeedLifecycleStage.CardShowRequested, "收到 OnShow，等待配置与首帧就绪");
        DispatchCardShownIfReady();
    }

    private void HandleAppHide()
    {
        if (!isDirectPlay) return;
        bool wasVisible = isAppVisible;
        isAppVisible = false;
        if (isInFullGame)
        {
            if (wasVisible && context != null)
            {
                RecordLifecycle(FeedLifecycleStage.FullGameHidden, enterPending ? "完整游戏尚在加载，保持后台等待" : "完整游戏进入后台");
                if (!enterPending) FullGameHidden?.Invoke(context);
            }
            return;
        }
        if (!isCardVisible) return;
        isCardVisible = false;
        RecordLifecycle(FeedLifecycleStage.CardHidden, "卡片划走 / 容器隐藏");
        if (!cardShowDispatched || context == null) return;
        cardShowDispatched = false;
        CardHidden?.Invoke(context);
    }

    private void DispatchCardShownIfReady()
    {
        if (!isDirectPlay || isInFullGame || !isCardVisible || !readyReported || cardShowDispatched || context == null) return;
        cardShowDispatched = true;
        RecordLifecycle(FeedLifecycleStage.CardShown, "卡片可见且就绪，开始/恢复体验与配音");
        CardShown?.Invoke(context);
    }

    private void HandleFeedEnterSignal()
    {
        if (!isDirectPlay || isInFullGame) return;
        if (cardShowDispatched && context != null) CardHidden?.Invoke(context);
        isInFullGame = true;
        isAppVisible = true;
        isCardVisible = false;
        cardShowDispatched = false;
        exitPending = false;
        enterPending = true;
        RecordLifecycle(FeedLifecycleStage.FeedEnterRequested, "玩家进入完整游戏，未就绪时暂存");
        DispatchEnter();
    }

    private void HandleFeedExitSignal()
    {
        if (!isDirectPlay || !isInFullGame) return;
        isInFullGame = false;
        isAppVisible = false;
        isCardVisible = false;
        cardShowDispatched = false;
        enterPending = false;
        exitPending = true;
        RecordLifecycle(FeedLifecycleStage.FeedExited, "退出完整游戏，等待卡片下一次 OnShow");
        if (context != null) FeedExited?.Invoke(context);
    }

    private void DispatchEnter()
    {
        if (context == null || !readyReported || !enterPending) return;
        enterPending = false;
        RecordLifecycle(FeedLifecycleStage.FullGameEntered, enterHandled ? "再次进入完整游戏" : "首次进入完整游戏");
        FeedEntered?.Invoke(context);
        if (!enterHandled)
        {
            enterHandled = true;
            SchemeEntered?.Invoke(context);
        }
        // FeedEnter 在后台加载期间到达，随后 OnHide：不能在资源就绪时意外恢复声音。
        if (!isAppVisible) FullGameHidden?.Invoke(context);
    }

    private FeedRecommendationConfig LoadLocalConfig()
    {
        if (config == null) config = Resources.Load<FeedRecommendationConfig>(nameof(FeedRecommendationConfig));
        if (config == null) Debug.LogError("[推荐流] 缺少 Resources/FeedRecommendationConfig.asset");
        return config;
    }

    private static string Query(LaunchOption options, string key)
    {
        return options.Query != null && options.Query.ContainsKey(key) ? options.Query[key] : null;
    }

    private static FeedUserChannel ParseChannel(string value)
    {
        if (value == "1") return FeedUserChannel.ReturnVisit;
        if (value == "2") return FeedUserChannel.Acquisition;
        return FeedUserChannel.Unknown;
    }

    // 原生请求取消与纯托管状态转换分离；无引擎隔离检查不会执行此原生分支。
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void AbortNativeRequest(UnityWebRequest request) => request.Abort();

    private void ResetState()
    {
        if (activeLaunchRequest != null)
        {
            AbortNativeRequest(activeLaunchRequest);
            activeLaunchRequest = null;
        }
        if (isDirectPlay)
        {
            RecordLifecycle(FeedLifecycleStage.SessionEnded, "结束旧内容，清理体验资源");
            SessionEnded?.Invoke(context);
        }
        launchVersion++;
        configurationResolving = false;
        resourcesReadyObserved = false;
        isAppVisible = false;
        LastConfigurationFailure = null;
#if UNITY_EDITOR
        editorPreview = false;
        debugConfig = null;
        debugUseRemote = false;
        debugAutoSceneReady = true;
        debugReadinessPending = false;
        debugPendingSceneId = 7001;
#endif
        context = null;
        isDirectPlay = false;
        isCardVisible = false;
        isInFullGame = false;
        cardShowDispatched = false;
        enterHandled = false;
        enterPending = false;
        exitPending = false;
        readyReported = false;
    }
}

/// <summary>
/// 一次推荐流启动解析后的只读业务上下文。
/// </summary>
public sealed class FeedLaunchContext
{
    public FeedLaunchContext(FeedUserChannel channel, string contentId, int sceneId,
        FeedExperienceScheme scheme, string payload)
    {
        Channel = channel;
        ContentId = contentId;
        SceneId = sceneId;
        Scheme = scheme;
        Payload = payload;
    }

    /// <summary>用户来源渠道。</summary>
    public FeedUserChannel Channel { get; }

    /// <summary>平台推荐内容 ID。</summary>
    public string ContentId { get; }

    /// <summary>平台下发的业务场景 ID。</summary>
    public int SceneId { get; }

    /// <summary>本次启动命中的体验方案。</summary>
    public FeedExperienceScheme Scheme { get; }

    /// <summary>交由项目适配层解释的业务参数。</summary>
    public string Payload { get; }

    public override string ToString()
    {
        return $"channel={Channel}, contentId={ContentId}, sceneId={SceneId}, scheme={Scheme}, payload={Payload}";
    }
}
