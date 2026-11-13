using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>使用已放置的测试广告预制体；不会在运行时生成或加载UI。</summary>
public sealed class AdTestSimulator : MonoBehaviour
{
    [SerializeField] private AdTestPanel rewardPanel;
    [SerializeField] private AdTestPanel interstitialPanel;
    [Min(0f)] public float initializationSeconds = 0.1f;
    [Tooltip("模拟全屏广告期间暂停游戏，关闭或失败后恢复原值。")]
    [SerializeField] private bool pauseGame = true;
    [SerializeField] private bool pauseAudio = true;
    [Header("模拟广告前后台业务通知（不是操作系统原生回调）")]
    public UnityEvent onAdShown = new UnityEvent();
    public UnityEvent onAdClosed = new UnityEvent();
    public bool IsBusy => activePanel != null;
    public AdTestPanel RewardPanel { get { ResolvePanels(); return rewardPanel; } }
    public AdTestPanel InterstitialPanel { get { ResolvePanels(); return interstitialPanel; } }
    private AdTestPanel activePanel;
    private bool captured;
    private bool ownsGamePause;
    private bool ownsAudioPause;
    private float previousTimeScale;
    private bool previousAudioPause;

    /// <summary>先查自身子对象，再关联场景中唯一、未被其他广告系统占用的预置面板。</summary>
    public void ResolvePanels()
    {
        if (rewardPanel == null) rewardPanel = FindPanel(true);
        if (interstitialPanel == null) interstitialPanel = FindPanel(false);
    }

    private AdTestPanel FindPanel(bool rewarded)
    {
        foreach (AdTestPanel panel in GetComponentsInChildren<AdTestPanel>(true))
            if (panel.IsRewarded == rewarded) return panel;
        AdTestPanel found = null;
        AdManager owner = GetComponentInParent<AdManager>() ?? AdManager.Instance;
        foreach (AdTestPanel panel in UnityEngine.Object.FindObjectsOfType<AdTestPanel>(true))
        {
            if (!panel.gameObject.scene.IsValid() || !panel.gameObject.scene.isLoaded || panel.IsRewarded != rewarded) continue;
            AdManager panelOwner = panel.GetComponentInParent<AdManager>();
            if (panelOwner != null && panelOwner != owner) continue;
            if (found != null) return null; // 有多个未接线面板时不猜测、不串到其他系统。
            found = panel;
        }
        return found;
    }

    private static void PreparePanel(AdTestPanel panel)
    {
        if (panel == null) return;
        // 只激活测试预制体自身；不会擅自打开游戏里被关闭的上层页面。
        if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
        if (!panel.enabled) panel.enabled = true;
        if (!panel.isActiveAndEnabled)
        {
            Debug.LogWarning("[AdTest] 测试面板的上层对象未激活，请将测试预制体放在常驻、启用的广告节点下：" + panel.name, panel);
            return;
        }
        panel.Prepare();
    }

    public void PrepareReward() { ResolvePanels(); PreparePanel(rewardPanel); }
    public void PrepareInterstitial() { ResolvePanels(); PreparePanel(interstitialPanel); }
    public bool ShowReward(Action<bool> closed, Action failed, Action shown)
    { ResolvePanels(); return TryShow(rewardPanel, closed, failed, shown); }
    public bool ShowInterstitial(Action closed, Action failed, Action shown)
    { ResolvePanels(); return TryShow(interstitialPanel, _ => closed?.Invoke(), failed, shown); }

    private bool TryShow(AdTestPanel panel, Action<bool> closed, Action failed, Action shown)
    {
        if (IsBusy || panel == null) return false;
        PreparePanel(panel);
        activePanel = panel;
        bool accepted = panel.TryShow(
            ended => { EndFullscreen(); closed?.Invoke(ended); },
            () => { EndFullscreen(); failed?.Invoke(); },
            () => { BeginFullscreen(); shown?.Invoke(); });
        if (!accepted)
        {
            activePanel = null;
            Debug.LogWarning("[AdTest] 测试广告尚未就绪：" + panel.name + "，状态=" + panel.State + "；加载完成后再次请求。", panel);
        }
        return accepted;
    }

    private void BeginFullscreen()
    {
        previousTimeScale = Time.timeScale;
        previousAudioPause = AudioListener.pause;
        captured = true;
        ownsGamePause = pauseGame;
        ownsAudioPause = pauseAudio;
        if (ownsGamePause) Time.timeScale = 0f;
        if (ownsAudioPause) AudioListener.pause = true;
        Debug.Log("[AdTest] 广告展示 → 暂停游戏/音频；倒计时使用非缩放时间");
        onAdShown.Invoke();
    }

    private void EndFullscreen()
    {
        activePanel = null;
        if (!captured) return;
        captured = false;
        if (ownsGamePause) Time.timeScale = previousTimeScale;
        if (ownsAudioPause) AudioListener.pause = previousAudioPause;
        Debug.Log("[AdTest] 广告结束 → 恢复游戏/音频原状态，再执行业务回调");
        onAdClosed.Invoke();
    }

    private void OnDisable()
    {
        if (activePanel != null) activePanel.Cancel();
        EndFullscreen();
    }
}
