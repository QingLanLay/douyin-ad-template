using System.Collections;
using UnityEngine;

/// <summary>
/// 当前模板项目的推荐流适配层。具体游戏接入时可按 payload 替换为真实关卡加载逻辑。
/// </summary>
public static class FeedRecommendationProjectAdapter
{
    private static bool registered;
    private static FeedVideoPlayerController video;
    private static float timeScaleBeforePause = 1f;
    private static bool audioPausedBeforePause;
    private static bool experiencePaused;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterBeforeSceneLoad()
    {
        Register();
    }

    /// <summary>注册当前项目的推荐流业务事件，重复调用安全。</summary>
    public static void Register()
    {
        if (registered) return;
        registered = true;

        FeedRecommendationManager manager = FeedRecommendationManager.Instance;
        manager.SchemePrepared += OnPrepared;
        manager.CardShown += OnCardShown;
        manager.CardHidden += OnCardHidden;
        manager.SchemeEntered += OnEntered;
        manager.FeedEntered += OnFeedEntered;
        manager.FeedExited += OnExited;
        manager.ConfigurationFailed += OnConfigurationFailed;
    }

    private static void OnPrepared(FeedLaunchContext context)
    {
        Debug.Log("[推荐流适配] 预加载方案：" + context);
        PauseExperience(context, "隐藏预加载");
        if (context.Scheme == FeedExperienceScheme.Video)
        {
            PrepareVideo(context.Payload);
            return;
        }

        // 当前仓库是广告模板，没有业务关卡。现有 SampleScene 即安全预览页，
        // 等待一帧确保场景 UI 已渲染后再通知平台就绪。
        FeedRecommendationManager.Instance.StartCoroutine(ReportCurrentSceneAfterFrame());
    }

    private static IEnumerator ReportCurrentSceneAfterFrame()
    {
        yield return new WaitForEndOfFrame();
        FeedRecommendationManager.Instance.ReportSceneReady();
    }

    private static void OnEntered(FeedLaunchContext context)
    {
        if (video != null)
        {
            video.Close();
            video = null;
        }

        AdManagerImpl.Instance?.BeginPostSdkInitialization();

        bool isNewDay = DayCheckManager.IsNewDay();
        if (isNewDay) DayCheckManager.SaveCurrentDate();
        Debug.Log("[推荐流适配] 玩家已正式进入，开放交互：" + context);
    }

    private static void OnFeedEntered(FeedLaunchContext context)
    {
        ResumeExperience(context, "进入完整小游戏");
    }

    private static void OnCardShown(FeedLaunchContext context)
    {
        ResumeExperience(context, "卡片 OnShow");
        if (video != null) video.SetCardVisible(true);
        Debug.Log("[推荐流适配] 卡片可见，开始或恢复卡片体验：" + context);
    }

    private static void OnCardHidden(FeedLaunchContext context)
    {
        if (video != null) video.SetCardVisible(false);
        PauseExperience(context, "卡片 OnHide");
    }

    private static void OnExited(FeedLaunchContext context)
    {
        PauseExperience(context, "退出完整小游戏");
    }

    private static void PauseExperience(FeedLaunchContext context, string reason)
    {
        if (!experiencePaused)
        {
            timeScaleBeforePause = Time.timeScale;
            audioPausedBeforePause = AudioListener.pause;
            experiencePaused = true;
        }
        Time.timeScale = 0f;
        AudioListener.pause = true;
        Debug.Log($"[推荐流适配] {reason}，已暂停计时和声音：{context}");
    }

    private static void ResumeExperience(FeedLaunchContext context, string reason)
    {
        if (experiencePaused)
        {
            Time.timeScale = timeScaleBeforePause;
            AudioListener.pause = audioPausedBeforePause;
            experiencePaused = false;
        }
        Debug.Log($"[推荐流适配] {reason}，已恢复体验：{context}");
    }

    private static void PrepareVideo(string url)
    {
        video = FeedVideoPlayerController.Create();
        video.Ready += () => FeedRecommendationManager.Instance.ReportSceneReady();
        video.Failed += message =>
        {
            Debug.LogWarning("[推荐流适配] 视频准备失败，回退当前场景：" + message);
            if (video != null) video.Close();
            video = null;
            FeedRecommendationManager.Instance.StartCoroutine(ReportFallbackAfterFrame());
        };
        video.PrepareAndPlay(url);
    }

    private static void OnConfigurationFailed(string reason)
    {
        Debug.LogWarning("[推荐流适配] 配置未命中，使用当前场景兜底：" + reason);
        FeedRecommendationManager.Instance.StartCoroutine(ReportFallbackAfterFrame());
    }

    private static IEnumerator ReportFallbackAfterFrame()
    {
        yield return new WaitForEndOfFrame();
        FeedRecommendationManager.Instance.ReportFallbackSceneReady();
    }
}
