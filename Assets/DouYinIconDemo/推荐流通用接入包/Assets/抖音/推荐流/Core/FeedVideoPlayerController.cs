using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// 推荐流视频播放器，在全屏画布中循环播放远程视频并上报首帧状态。
/// </summary>
public sealed class FeedVideoPlayerController : MonoBehaviour
{
    // 超过该时间仍未收到首帧时，通知项目适配层切换到安全兜底页面。
    private const float FirstFrameTimeout = 8f;
    private VideoPlayer player;
    private AudioSource audioSource;
    private RenderTexture texture;
    private AspectRatioFitter fitter;
    private bool completed;
    private bool firstFrameReady;
    private bool cardVisibleRequested;

    /// <summary>视频首帧已经可展示。</summary>
    public event Action Ready;

    /// <summary>视频准备或播放失败，参数为失败原因。</summary>
    public event Action<string> Failed;

    /// <summary>
    /// 创建一个跨场景保留的播放器对象。
    /// </summary>
    public static FeedVideoPlayerController Create()
    {
        var go = new GameObject(nameof(FeedVideoPlayerController));
        DontDestroyOnLoad(go);
        return go.AddComponent<FeedVideoPlayerController>();
    }

    /// <summary>
    /// 创建播放界面并异步准备指定 URL 的视频。
    /// </summary>
    public void PrepareAndPlay(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            Fail("视频 URL 为空");
            return;
        }

        CreateCanvas();
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.mute = true;
        player = gameObject.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.waitForFirstFrame = true;
        player.sendFrameReadyEvents = true;
        player.skipOnDrop = true;
        player.isLooping = true;
        player.source = VideoSource.Url;
        player.url = url.Trim();
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = texture;
        player.audioOutputMode = VideoAudioOutputMode.AudioSource;
        player.SetTargetAudioSource(0, audioSource);
        // 隐藏预加载阶段静音解码首帧；卡片 OnShow 后才从起点有声播放。
        player.prepareCompleted += OnPrepared;
        player.frameReady += OnFrameReady;
        player.errorReceived += (_, message) => Fail(message);
        player.Prepare();
        StartCoroutine(Timeout());
    }

    /// <summary>根据卡片 OnShow/OnHide 启动、恢复或暂停正式视频播放。</summary>
    public void SetCardVisible(bool visible)
    {
        cardVisibleRequested = visible;
        if (player == null) return;

        if (!visible)
        {
            if (audioSource != null) audioSource.mute = true;
            player.Pause();
            return;
        }

        if (!firstFrameReady) return;
        if (audioSource != null) audioSource.mute = false;
        player.Play();
    }

    /// <summary>
    /// 停止播放、释放渲染纹理并销毁播放器对象。
    /// </summary>
    public void Close()
    {
        completed = true;
        if (player != null) player.Stop();
        if (texture != null)
        {
            texture.Release();
            Destroy(texture);
            texture = null;
        }
        Destroy(gameObject);
    }

    private void CreateCanvas()
    {
        // 使用当前屏幕尺寸创建纹理，最低 16 像素用于避免无效纹理尺寸。
        texture = new RenderTexture(Mathf.Max(16, Screen.width), Mathf.Max(16, Screen.height), 0);
        texture.Create();
        var canvasObject = new GameObject("FeedVideoCanvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // 使用最高排序值，确保推荐视频覆盖普通游戏 UI。
        canvas.sortingOrder = short.MaxValue;

        var imageObject = new GameObject("Video");
        imageObject.transform.SetParent(canvasObject.transform, false);
        RawImage image = imageObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = Color.white;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        fitter = imageObject.AddComponent<AspectRatioFitter>();
        // 保持视频原始宽高比，空余区域由画布背景自然填充。
        fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
    }

    private void OnPrepared(VideoPlayer source)
    {
        if (source.height > 0) fitter.aspectRatio = (float)source.width / source.height;
        if (audioSource != null) audioSource.mute = true;
        source.Play();
    }

    private void OnFrameReady(VideoPlayer source, long frame)
    {
        if (completed) return;
        completed = true;
        // 首帧只需通知一次，立即取消逐帧回调以减少后续开销。
        source.frameReady -= OnFrameReady;
        source.Pause();
        source.time = 0d;
        firstFrameReady = true;
        Ready?.Invoke();
        if (cardVisibleRequested)
        {
            if (audioSource != null) audioSource.mute = false;
            source.Play();
        }
    }

    private IEnumerator Timeout()
    {
        yield return new WaitForSecondsRealtime(FirstFrameTimeout);
        // 如果首帧已经到达，Fail 内部的 completed 检查会忽略本次超时。
        Fail($"{FirstFrameTimeout:0} 秒内没有视频首帧");
    }

    private void Fail(string message)
    {
        if (completed) return;
        completed = true;
        Failed?.Invoke(message);
    }
}
