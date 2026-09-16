using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>原子体力状态入口：显示当前体力，并打开当前场景中的原子体力面板。</summary>
[DisallowMultipleComponent]
public sealed class EnergyStatusButton : MonoBehaviour
{
    [SerializeField] private Button openPanelButton;
    [SerializeField] private TextMeshProUGUI energyText;
    [SerializeField] private EnergyPanelView targetPanel;

    private EnergySystem _energy;
    private float _nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        if (openPanelButton != null)
            openPanelButton.onClick.AddListener(OpenPanel);

        BindEnergySystem();
        Refresh();
    }

    private void OnDisable()
    {
        if (openPanelButton != null)
            openPanelButton.onClick.RemoveListener(OpenPanel);

        UnbindEnergySystem();
    }

    private void Update()
    {
        if (_energy == null)
            BindEnergySystem();

        if (Time.unscaledTime < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.unscaledTime + 1f;
        Refresh();
    }

    /// <summary>打开任意已加载场景中的原子体力面板。</summary>
    public void OpenPanel()
    {
        if (targetPanel == null)
            targetPanel = EnergyPanelView.FindAvailable();

        if (targetPanel == null)
        {
            Debug.LogWarning("[EnergyStatusButton] 当前已加载场景中没有 EnergyPanelView。", this);
            return;
        }

        targetPanel.OpenPanel();
    }

    private void ResolveReferences()
    {
        if (openPanelButton == null)
            openPanelButton = GetComponentInChildren<Button>(true);

        if (energyText == null)
            energyText = GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private void BindEnergySystem()
    {
        EnergySystem current = EnergySystem.Instance;
        if (_energy == current)
            return;

        UnbindEnergySystem();
        _energy = current;
        if (_energy != null)
            _energy.OnChanged += Refresh;
    }

    private void UnbindEnergySystem()
    {
        if (_energy != null)
            _energy.OnChanged -= Refresh;
        _energy = null;
    }

    private void Refresh()
    {
        if (_energy == null || energyText == null)
            return;

        if (_energy.IsInfinite())
        {
            energyText.text = "∞ " + _energy.GetInfiniteTimeText();
            return;
        }

        int seconds = _energy.GetCountdownSeconds();
        string countdown = seconds > 0 ? " " + seconds + "s" : string.Empty;
        energyText.text = _energy.GetDisplayText() + "/" + _energy.maxEnergy + countdown;
    }
}
