using UnityEngine;
using TMPro;

/// <summary>场景内手动广告测试入口。只统计回调，不发放奖励、不写玩家存档、不动态创建 UI。</summary>
[DisallowMultipleComponent]
public sealed class AdManualTestControls : MonoBehaviour
{
    [Header("场景引用")]
    [SerializeField] private AdManager adManager;
    [SerializeField] private TMP_Text modeText;
    [SerializeField] private TMP_Text resultText;

    private int rewardRequests;
    private int rewardCompleted;
    private int rewardFailed;
    private int interstitialRequests;
    private int interstitialClosed;
    private bool lastNoAds;
    private bool lastIsTest;

    private void OnEnable()
    {
        RefreshMode();
        ShowResult("等待点击按钮");
    }

    private void Update()
    {
        AdManager manager = ResolveManager();
        if (manager != null && (manager.noAds != lastNoAds || manager.isTest != lastIsTest))
            RefreshMode();
    }

    /// <summary>绑定场景激励按钮；成功、失败都只更新本面板计数。</summary>
    public void TestRewardAd()
    {
        AdManager manager = ResolveManager();
        if (manager == null)
        {
            ShowResult("未找到场景中的 AdManager");
            return;
        }

        int request = ++rewardRequests;
        RefreshMode();
        ShowResult($"激励请求 #{request}，等待完成或关闭");
        manager.ShowRewardVideoAd(
            () =>
            {
                rewardCompleted++;
                ShowResult($"激励请求 #{request}：成功回调");
                Debug.Log($"[广告手动测试] 激励 #{request} 成功；仅统计，不发放奖励。", this);
            },
            () =>
            {
                rewardFailed++;
                ShowResult($"激励请求 #{request}：失败回调");
                Debug.Log($"[广告手动测试] 激励 #{request} 失败。", this);
            });
    }

    /// <summary>绑定场景插屏按钮；遵守当前模式和原保护期，不绕过 SDK 包装。</summary>
    public void TestInterstitialAd()
    {
        AdManager manager = ResolveManager();
        if (manager == null)
        {
            ShowResult("未找到场景中的 AdManager");
            return;
        }

        int request = ++interstitialRequests;
        RefreshMode();
        string hint = manager.noAds
            ? "无广模式：直接继续"
            : "正常模式受启动保护期和广告间隔限制";
        ShowResult($"插屏请求 #{request}，{hint}");
        manager.ShowInterstitialAd(() =>
        {
            interstitialClosed++;
            ShowResult($"插屏请求 #{request}：关闭／继续回调");
            Debug.Log($"[广告手动测试] 插屏 #{request} 关闭／继续。", this);
        });
    }

    private AdManager ResolveManager()
    {
        if (adManager == null) adManager = AdManager.Instance;
        return adManager;
    }

    private void RefreshMode()
    {
        AdManager manager = ResolveManager();
        if (manager == null)
        {
            if (modeText != null) modeText.text = "未绑定 AdManager";
            return;
        }

        lastNoAds = manager.noAds;
        lastIsTest = manager.isTest;
        if (modeText != null)
            modeText.text = $"noAds：{(lastNoAds ? "开" : "关")}     isTest：{(lastIsTest ? "开" : "关")}";
    }

    private void ShowResult(string message)
    {
        if (resultText != null)
            resultText.text = $"{message}\n激励成功 {rewardCompleted} / 失败 {rewardFailed}    插屏继续 {interstitialClosed}";
    }
}
