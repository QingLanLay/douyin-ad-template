using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>预先放置的测试广告：模拟创建、加载、展示、观看完成、关闭与错误回调。</summary>
public sealed class AdTestPanel : MonoBehaviour
{
    public enum AdState { None, Created, Loading, Loaded, Showing, Completed, Closed, Error, Destroyed }

    [Header("广告类型与时长")]
    [SerializeField] private bool rewarded;
    [Min(0f)] [SerializeField] private float loadSeconds = 0.5f;
    [Min(0.1f)] [SerializeField] private float watchSeconds = 15f;
    [Header("下一次故障模拟（触发后自动复位）")]
    [SerializeField] private bool failNextLoad;
    [SerializeField] private bool failNextShow;
    [Header("已序列化的界面")]
    [SerializeField] private GameObject presentation;
    [SerializeField] private Image statusImage;
    [SerializeField] private Image closeImage;
    [SerializeField] private Sprite playingStatusSprite;
    [SerializeField] private Sprite completedStatusSprite;
    [SerializeField] private Sprite interstitialStatusSprite;
    [SerializeField] private Sprite closeAdSprite;
    [SerializeField] private Sprite closeRewardSprite;
    [SerializeField] private Sprite[] digitSprites = new Sprite[10];
    [SerializeField] private Image[] countdownImages = Array.Empty<Image>();
    [SerializeField] private Image progressFill;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button finishButton;
    [SerializeField] private Button errorButton;

    public AdState State { get; private set; }
    public bool IsReady => State == AdState.Loaded;
    public bool IsShowing => State == AdState.Showing || State == AdState.Completed;
    public bool IsRewarded => rewarded;
    private Coroutine loading;
    private float watchedSeconds;
    private bool applicationPaused;
    private Action<bool> closed;
    private Action failed;

    private void Awake()
    {
        if (presentation != null) presentation.SetActive(false);
    }

    public void Prepare()
    {
        if (IsShowing || State == AdState.Loading || IsReady || !isActiveAndEnabled) return;
        Transition(AdState.Created, "创建广告实例");
        loading = StartCoroutine(Load());
    }

    private IEnumerator Load()
    {
        Transition(AdState.Loading, "开始加载");
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, loadSeconds));
        loading = null;
        if (failNextLoad)
        {
            failNextLoad = false;
            Transition(AdState.Error, "OnError：模拟加载失败；下次请求才重试");
            yield break;
        }
        Transition(AdState.Loaded, "OnLoad：广告已就绪");
    }

    public bool TryShow(Action<bool> onClosed, Action onFailed, Action onShown)
    {
        if (!IsReady || !isActiveAndEnabled || presentation == null) { Prepare(); return false; }
        closed = onClosed;
        failed = onFailed;
        if (failNextShow)
        {
            failNextShow = false;
            SimulateError();
            return true;
        }
        watchedSeconds = 0f;
        applicationPaused = false;
        Transition(AdState.Showing, "Show：广告开始展示");
        presentation.SetActive(true);
        UpdatePresentation();
        onShown?.Invoke();
        return true;
    }

    private void Update()
    {
        if (!IsShowing || applicationPaused) return;
        if (rewarded && State == AdState.Showing)
        {
            watchedSeconds += Time.unscaledDeltaTime;
            if (watchedSeconds >= Mathf.Max(0.1f, watchSeconds)) CompleteImmediately();
        }
        UpdatePresentation();
    }

    /// <summary>一键看完：只标记观看完成，仍由关闭按钮触发 OnClose 和业务奖励。</summary>
    public void CompleteImmediately()
    {
        if (!rewarded || State != AdState.Showing) return;
        watchedSeconds = Mathf.Max(0.1f, watchSeconds);
        Transition(AdState.Completed, "视频已看完；等待关闭后回调发奖");
        UpdatePresentation();
    }

    /// <summary>测试广告的“立即下载”：快速完成观看并关闭，沿既有关闭链只发放一次奖励，不打开外链。</summary>
    public void QuickClaim()
    {
        if (!rewarded || !IsShowing) return;
        if (State == AdState.Showing) CompleteImmediately();
        CloseAd();
    }

    public void CloseAd()
    {
        if (!IsShowing) return;
        bool ended = rewarded && State == AdState.Completed;
        Action<bool> callback = closed;
        ClearCallbacks();
        presentation.SetActive(false);
        Transition(AdState.Closed, "OnClose：isEnded=" + ended);
        // 先开始下一轮加载，再调用业务，防止回调重入或停用对象造成状态错乱。
        Prepare();
        callback?.Invoke(ended);
    }

    public void SimulateError()
    {
        if (closed == null && failed == null && !IsShowing) return;
        Action callback = failed;
        ClearCallbacks();
        if (presentation != null) presentation.SetActive(false);
        Transition(AdState.Error, "OnError：模拟广告展示失败（不发奖、不重置插屏冷却）");
        callback?.Invoke();
    }

    public void Cancel()
    {
        if (IsShowing || closed != null || failed != null) SimulateError();
    }

    private void ClearCallbacks() { closed = null; failed = null; }

    private void UpdatePresentation()
    {
        bool completed = State == AdState.Completed;
        if (statusImage != null)
            statusImage.sprite = !rewarded ? interstitialStatusSprite : completed ? completedStatusSprite : playingStatusSprite;
        if (closeImage != null) closeImage.sprite = rewarded && completed ? closeRewardSprite : closeAdSprite;
        UpdateCountdown(rewarded && !completed);
        if (progressFill != null)
        {
            float progress = rewarded ? Mathf.Clamp01(watchedSeconds / Mathf.Max(0.1f, watchSeconds)) : 1f;
            progressFill.fillAmount = progress;
            // 无 Sprite 的纯色 Image 不处理 Filled 网格，直接调整已序列化进度条的锚点。
            progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
        }
        if (finishButton != null) finishButton.interactable = rewarded && IsShowing;
    }

    // 倒计时也使用预置数字图片；不创建 UI，不访问 TMP 字体、材质或动态字库。
    private void UpdateCountdown(bool visible)
    {
        string value = Mathf.CeilToInt(Mathf.Max(0f, watchSeconds - watchedSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        for (int i = 0; i < countdownImages.Length; i++)
        {
            Image image = countdownImages[i];
            if (image == null) continue;
            bool show = visible && i < value.Length;
            if (image.gameObject.activeSelf != show) image.gameObject.SetActive(show);
            if (show && digitSprites != null && digitSprites.Length == 10)
                image.sprite = digitSprites[value[i] - '0'];
        }
    }

    private void OnApplicationPause(bool paused) { applicationPaused = paused; }
    private void OnDisable()
    {
        Cancel();
        if (loading != null) { StopCoroutine(loading); loading = null; }
        if (State == AdState.Loading) State = AdState.None;
    }
    private void OnDestroy()
    {
        Cancel();
        Transition(AdState.Destroyed, "Destroy：销毁测试广告");
    }
    private void Transition(AdState state, string description)
    {
        State = state;
        if (Application.isPlaying) Debug.Log("[AdTest][" + (rewarded ? "激励" : "插屏") + "] " + description, this);
    }
}
