using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>测试广告调度器；引用常驻系统内的固定预制体，不在运行时创建 UI。</summary>
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
    public AdTestPanel RewardPanel => rewardPanel;
    public AdTestPanel InterstitialPanel => interstitialPanel;
    private AdTestPanel activePanel;
    private bool captured;
    private bool ownsGamePause;
    private bool ownsAudioPause;
    private float previousTimeScale;
    private bool previousAudioPause;

    public void PrepareReward() { if (rewardPanel != null) rewardPanel.Prepare(); }
    public void PrepareInterstitial() { if (interstitialPanel != null) interstitialPanel.Prepare(); }
    public bool ShowReward(Action<bool> closed, Action failed, Action shown)
        => TryShow(rewardPanel, closed, failed, shown);
    public bool ShowInterstitial(Action closed, Action failed, Action shown)
        => TryShow(interstitialPanel, _ => closed?.Invoke(), failed, shown);

    private bool TryShow(AdTestPanel panel, Action<bool> closed, Action failed, Action shown)
    {
        if (IsBusy || panel == null) return false;
        activePanel = panel;
        bool accepted = panel.TryShow(
            ended => { EndFullscreen(); closed?.Invoke(ended); },
            () => { EndFullscreen(); failed?.Invoke(); },
            () => { BeginFullscreen(); shown?.Invoke(); });
        if (!accepted) activePanel = null;
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
