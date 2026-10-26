using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>抖音原子按钮基类：依赖常驻 AdManager，并按按钮类型保存每日红点的点击状态。</summary>
[RequireComponent(typeof(Button))]
public abstract class DouyinAtomicButtonBase : MonoBehaviour
{
    private const string DailyClickKeyPrefix = "douyin_atomic_button_clicked_date_";

    [SerializeField] private Button button;
    [Tooltip("按钮右下角的固定红点子物体。每天首次显示，点击本按钮后当天不再显示。")]
    [SerializeField] private GameObject dailyRedDot;

    private float nextDailyRefreshTime;

    private string DailyClickKey => DailyClickKeyPrefix + GetType().Name;
    private static string Today => DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    protected virtual void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        button.onClick.AddListener(HandleClick);
    }

    protected virtual void OnEnable()
    {
        if (AdManager.NoAdsEnabled) { AdManager.HideNoAdsPlatformObject(gameObject); return; }
        RefreshDailyRedDot();
    }

    protected virtual void Update()
    {
        // 使用非缩放时间，暂停游戏或跨过午夜时也能刷新日期。
        if (Time.unscaledTime >= nextDailyRefreshTime)
            RefreshDailyRedDot();
    }

    protected virtual void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
            RefreshDailyRedDot();
    }

    protected virtual void OnApplicationPause(bool paused)
    {
        if (!paused)
            RefreshDailyRedDot();
    }

    protected virtual void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        if (AdManager.NoAdsEnabled) return;
        // 以点击为准消除红点，不等待业务成功；保持加桌 API 位于同步用户手势调用链内。
        MarkDailyRedDotClicked();
        Execute();
    }

    private void MarkDailyRedDotClicked()
    {
        TTSaveManager save = TTSaveManager.Instance;
        string today = Today;
        if (save.GetString(DailyClickKey) != today)
        {
            save.SetString(DailyClickKey, today);
            save.Save();
        }

        if (dailyRedDot != null)
            dailyRedDot.SetActive(false);
    }

    private void RefreshDailyRedDot()
    {
        nextDailyRefreshTime = Time.unscaledTime + 1f;
        if (dailyRedDot == null)
            return;

        bool show = TTSaveManager.Instance.GetString(DailyClickKey) != Today;
        if (dailyRedDot.activeSelf != show)
            dailyRedDot.SetActive(show);
    }

    protected bool TryGetManager(out AdManager manager)
    {
        manager = AdManager.Instance;
        if (manager != null)
            return true;

        Debug.LogWarning($"[{GetType().Name}] 场景中没有常驻 AdManager，按钮调用已取消。", this);
        return false;
    }

    protected abstract void Execute();
}
