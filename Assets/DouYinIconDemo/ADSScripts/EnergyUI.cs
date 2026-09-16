using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 体力UI —— 负责体力图标、体力文本、体力Panel的交互展示
/// 所有数据逻辑委托给 EnergySystem，本脚本只做UI层
/// </summary>
public class EnergyUI : MonoBehaviour
{
    [Header("体力图标 & 文本")]
    [Tooltip("体力数值 TextMeshProUGUI")]
    public TextMeshProUGUI energyText;

    [Header("体力 Panel")]
    [Tooltip("体力弹窗面板")]
    public GameObject energyPanel;

    [Tooltip("关闭面板按钮")]
    public Button closeButton;

    [Header("体力 Panel - 看广告按钮")]
    [Tooltip("按钮1：看一次广告 +5体力")]
    public Button addEnergyButton;

    [Tooltip("按钮2：看两次广告 激活无限体力")]
    public Button infiniteEnergyButton;

    [Tooltip("按钮2 上的 TextMeshProUGUI，显示进度")]
    public TextMeshProUGUI infiniteBtnText;

    private EnergySystem _energy;

    #region Unity 生命周期

    private void Start()
    {
        _energy = EnergySystem.Instance;

        if (_energy == null)
        {
            Debug.LogError("[EnergyUI] EnergySystem.Instance 为空！请确保场景中存在 EnergySystem 组件。");
            return;
        }

        // 订阅体力变化，自动刷新UI
        _energy.OnChanged += RefreshUI;

        // 关闭按钮
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);

        // 按钮1：看广告 +5体力
        if (addEnergyButton != null)
            addEnergyButton.onClick.AddListener(OnClickAddEnergy);

        // 按钮2：看2次广告激活无限体力
        if (infiniteEnergyButton != null)
            infiniteEnergyButton.onClick.AddListener(OnClickInfiniteEnergy);

        // 初始面板关闭
        if (energyPanel != null)
            energyPanel.SetActive(false);

        RefreshUI();
    }

    private void Update()
    {
        // 实时刷新倒计时文本
        UpdateCountdownText();
    }

    private void OnDestroy()
    {
        if (_energy != null)
            _energy.OnChanged -= RefreshUI;
    }

    #endregion

    #region 面板控制

    /// <summary>打开体力面板</summary>
    public void OpenPanel()
    {
        if (energyPanel != null)
        {
            energyPanel.SetActive(true);
            RefreshInfiniteBtnText();
        }
    }

    /// <summary>关闭体力面板</summary>
    public void ClosePanel()
    {
        if (energyPanel != null)
            energyPanel.SetActive(false);
    }

    /// <summary>扣除体力（测试用，绑定到调试按钮）</summary>
    public void DeductEnergy(int amount = 1)
    {
        _energy?.DeductEnergy(amount);
    }

    /// <summary>直接增加体力（测试用）</summary>
    public void AddEnergy(int amount = 5)
    {
        _energy?.AddByAd(() => { });
    }

    #endregion

    #region 按钮回调

    /// <summary>按钮1：看广告获得5点体力</summary>
    private void OnClickAddEnergy()
    {
        if (_energy == null) return;

        _energy.AddByAd(() =>
        {
            Debug.Log("[EnergyUI] 广告观看完成，+5体力");
        });
    }

    /// <summary>按钮2：看2次广告激活无限体力（Editor中跳过广告，直接计数）</summary>
    private void OnClickInfiniteEnergy()
    {
        if (_energy == null) return;

        if (_energy.IsInfinite())
        {
            Debug.Log("[EnergyUI] 已是无限体力状态，无需再次激活");
            return;
        }

        _energy.ActivateInfiniteByAd(() =>
        {
            Debug.Log("[EnergyUI] 无限体力已激活！");
        });

        RefreshInfiniteBtnText();
    }

    #endregion

    #region UI刷新

    /// <summary>刷新体力图标文本</summary>
    private void RefreshUI()
    {
        if (_energy == null) return;

        // 体力数值（格式：数量/最大 秒数倒计时，无限体力显示∞和剩余时间）
        if (energyText != null)
        {
            if (_energy.IsInfinite())
            {
                energyText.text = $"∞ {_energy.GetInfiniteTimeText()}";
            }
            else
            {
                int sec = _energy.GetCountdownSeconds();
                string cd = sec > 0 ? $" {sec}s" : "";
                energyText.text = $"{_energy.GetDisplayText()}/{_energy.maxEnergy}{cd}";
            }
        }

        // Panel 内按钮文本
        if (energyPanel != null && energyPanel.activeSelf)
            RefreshInfiniteBtnText();
    }

    /// <summary>刷新按钮2的进度文本</summary>
    private void RefreshInfiniteBtnText()
    {
        if (_energy == null || infiniteBtnText == null) return;

        if (_energy.IsInfinite())
        {
            infiniteBtnText.text = _energy.GetInfiniteTimeText();
        }
        else
        {
            var (cur, target) = _energy.GetAdProgress();
            infiniteBtnText.text = $"获取{cur}/{target}";
        }
    }

    /// <summary>实时更新倒计时（每帧刷新图标文本）</summary>
    private void UpdateCountdownText()
    {
        if (_energy == null || energyText == null) return;

        if (_energy.IsInfinite())
        {
            energyText.text = $"∞ {_energy.GetInfiniteTimeText()}";
        }
        else
        {
            int sec = _energy.GetCountdownSeconds();
            string cd = sec > 0 ? $" {sec}s" : "";
            energyText.text = $"{_energy.GetDisplayText()}/{_energy.maxEnergy}{cd}";
        }

        // Panel打开时刷新按钮文本
        if (energyPanel != null && energyPanel.activeSelf)
            RefreshInfiniteBtnText();
    }

    #endregion
}
