using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>原子体力弹窗：自行管理关闭、广告加体力和无限体力按钮。</summary>
[DisallowMultipleComponent]
public sealed class EnergyPanelView : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button addEnergyButton;
    [SerializeField] private Button infiniteEnergyButton;
    [SerializeField] private TextMeshProUGUI infiniteProgressText;

    public UnityEvent onEnergyAdded = new UnityEvent();
    public UnityEvent onInfiniteEnergyActivated = new UnityEvent();
    public UnityEvent onAdFailed = new UnityEvent();

    private EnergySystem _energy;
    private float _nextRefreshTime;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButtons();
        BindEnergySystem();
        Refresh();
    }

    private void OnDisable()
    {
        UnbindButtons();
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

    /// <summary>查找任意已加载场景中的体力面板，包括未激活面板。</summary>
    public static EnergyPanelView FindAvailable()
    {
        EnergyPanelView[] panels = Resources.FindObjectsOfTypeAll<EnergyPanelView>();
        for (int i = 0; i < panels.Length; i++)
        {
            EnergyPanelView panel = panels[i];
            if (panel != null && panel.gameObject.scene.IsValid())
                return panel;
        }

        return null;
    }

    /// <summary>打开面板并刷新显示。</summary>
    public void OpenPanel()
    {
        gameObject.SetActive(true);
        BindEnergySystem();
        Refresh();
    }

    /// <summary>关闭面板。</summary>
    public void ClosePanel()
    {
        gameObject.SetActive(false);
    }

    private void OnClickAddEnergy()
    {
        if (!TryGetEnergySystem())
            return;

        _energy.AddByAd(
            () =>
            {
                Refresh();
                onEnergyAdded.Invoke();
            },
            () => onAdFailed.Invoke());
    }

    private void OnClickInfiniteEnergy()
    {
        if (!TryGetEnergySystem())
            return;

        if (_energy.IsInfinite())
        {
            Refresh();
            return;
        }

        _energy.ActivateInfiniteByAd(
            () =>
            {
                Refresh();
                if (_energy.IsInfinite())
                    onInfiniteEnergyActivated.Invoke();
            },
            () => onAdFailed.Invoke());
    }

    private bool TryGetEnergySystem()
    {
        BindEnergySystem();
        if (_energy != null)
            return true;

        Debug.LogWarning("[EnergyPanelView] 常驻 DouyinAdSystem 上没有 EnergySystem。", this);
        return false;
    }

    private void ResolveReferences()
    {
        if (closeButton == null)
            closeButton = FindButton("Close Icon");
        if (addEnergyButton == null)
            addEnergyButton = FindButton("Energy Btuton 1");
        if (infiniteEnergyButton == null)
            infiniteEnergyButton = FindButton("Energy Btuton 2");
        if (infiniteProgressText == null && infiniteEnergyButton != null)
            infiniteProgressText = infiniteEnergyButton.GetComponentInChildren<TextMeshProUGUI>(true);
    }

    private Button FindButton(string objectName)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i].name == objectName)
                return buttons[i];
        }

        return null;
    }

    private void BindButtons()
    {
        UnbindButtons();
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
        if (addEnergyButton != null)
            addEnergyButton.onClick.AddListener(OnClickAddEnergy);
        if (infiniteEnergyButton != null)
            infiniteEnergyButton.onClick.AddListener(OnClickInfiniteEnergy);
    }

    private void UnbindButtons()
    {
        if (closeButton != null)
            closeButton.onClick.RemoveListener(ClosePanel);
        if (addEnergyButton != null)
            addEnergyButton.onClick.RemoveListener(OnClickAddEnergy);
        if (infiniteEnergyButton != null)
            infiniteEnergyButton.onClick.RemoveListener(OnClickInfiniteEnergy);
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
        if (_energy == null || infiniteProgressText == null)
            return;

        if (_energy.IsInfinite())
        {
            infiniteProgressText.text = _energy.GetInfiniteTimeText();
            return;
        }

        var progress = _energy.GetAdProgress();
        infiniteProgressText.text = "获取" + progress.cur + "/" + progress.target;
    }
}
