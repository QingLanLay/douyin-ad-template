using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.UI;

public enum TreasureChestPopupMode
{
    ManualClick,
    AutoOnCountdownComplete
}

/// <summary>原子化宝箱入口与弹窗：倒计时完成后手动点击或自动打开奖励面板。</summary>
[DisallowMultipleComponent]
public sealed class TreasureChestRewardWidget : MonoBehaviour
{
    [Header("弹出方式")]
    [SerializeField] private TreasureChestPopupMode popupMode = TreasureChestPopupMode.ManualClick;
    [SerializeField, Min(0f)] private float countdownSeconds = 35f;
    [SerializeField] private bool restartCountdownAfterReward = true;

    [Header("入口与面板")]
    [SerializeField] private Button entryButton;
    [SerializeField] private TextMeshProUGUI countdownText;
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button reward1Button;
    [SerializeField] private Button reward2Button;
    [SerializeField] private GameObject localEventSystem;

    [Header("文案")]
    [SerializeField] private string readyLabel = "获取宝箱";
    [SerializeField] private string claimedLabel = "已领取";

    [Header("事件")]
    public UnityEvent onCountdownCompleted = new UnityEvent();
    public UnityEvent onPanelOpened = new UnityEvent();
    public UnityEvent onPanelClosed = new UnityEvent();
    public UnityEvent onReward1Selected = new UnityEvent();
    public UnityEvent onReward2Selected = new UnityEvent();

    private float _remainingSeconds;
    private float _timeScaleBeforePanel;
    private bool _isReady;
    private bool _isPanelOpen;
    private bool _ownsPause;
    private bool _buttonsBound;

    public TreasureChestPopupMode PopupMode => popupMode;
    public float RemainingSeconds => Mathf.Max(0f, _remainingSeconds);
    public bool IsReady => _isReady;
    public bool IsPanelOpen => _isPanelOpen;

    private void Awake()
    {
        ResolveReferences();
        if (panelRoot != null)
            panelRoot.SetActive(false);

        StartCountdown();
        EnsureEventSystem();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButtons();
        EnsureEventSystem();
        RefreshCountdownText();
    }

    private void OnDisable()
    {
        UnbindButtons();
        RestoreGameTime();
    }

    private void OnDestroy()
    {
        RestoreGameTime();
    }

    private void Update()
    {
        EnsureEventSystem();

        if (_isReady)
            return;

        _remainingSeconds = Mathf.Max(0f, _remainingSeconds - Time.unscaledDeltaTime);
        RefreshCountdownText();
        if (_remainingSeconds <= 0f)
            CompleteCountdown();
    }

    /// <summary>重新开始完整倒计时。</summary>
    public void StartCountdown()
    {
        ClosePanel();
        _remainingSeconds = Mathf.Max(0f, countdownSeconds);
        _isReady = false;

        if (entryButton != null)
            entryButton.interactable = false;

        RefreshCountdownText();
        if (_remainingSeconds <= 0f)
            CompleteCountdown();
    }

    /// <summary>外部可直接切换为手动或自动弹窗模式。</summary>
    public void SetPopupMode(TreasureChestPopupMode mode)
    {
        popupMode = mode;
        if (_isReady && popupMode == TreasureChestPopupMode.AutoOnCountdownComplete)
            OpenPanel();
    }

    /// <summary>修改后立即以新时长重新倒计时。</summary>
    public void SetCountdownSeconds(float seconds)
    {
        countdownSeconds = Mathf.Max(0f, seconds);
        StartCountdown();
    }

    /// <summary>打开宝箱面板并暂停游戏。</summary>
    public void OpenPanel()
    {
        if (panelRoot == null || _isPanelOpen)
            return;

        _timeScaleBeforePanel = Time.timeScale;
        Time.timeScale = 0f;
        _ownsPause = true;
        _isPanelOpen = true;
        if (entryButton != null)
            entryButton.gameObject.SetActive(false);
        panelRoot.SetActive(true);
        onPanelOpened.Invoke();
    }

    /// <summary>关闭宝箱面板并恢复打开前的游戏速度。</summary>
    public void ClosePanel()
    {
        bool wasOpen = _isPanelOpen || (panelRoot != null && panelRoot.activeSelf);
        if (panelRoot != null)
            panelRoot.SetActive(false);
        if (entryButton != null)
            entryButton.gameObject.SetActive(true);

        _isPanelOpen = false;
        RestoreGameTime();
        if (wasOpen)
            onPanelClosed.Invoke();
    }

    private void CompleteCountdown()
    {
        if (_isReady)
            return;

        _remainingSeconds = 0f;
        _isReady = true;
        if (entryButton != null)
            entryButton.interactable = true;
        if (countdownText != null)
            countdownText.text = readyLabel;

        onCountdownCompleted.Invoke();
        if (popupMode == TreasureChestPopupMode.AutoOnCountdownComplete)
            OpenPanel();
    }

    private void OnEntryClicked()
    {
        if (_isReady)
            OpenPanel();
    }

    private void OnReward1Clicked()
    {
        onReward1Selected.Invoke();
        FinishRewardSelection();
    }

    private void OnReward2Clicked()
    {
        onReward2Selected.Invoke();
        FinishRewardSelection();
    }

    private void FinishRewardSelection()
    {
        ClosePanel();
        if (restartCountdownAfterReward)
        {
            StartCountdown();
            return;
        }

        _isReady = false;
        if (entryButton != null)
            entryButton.interactable = false;
        if (countdownText != null)
            countdownText.text = claimedLabel;
    }

    private void RefreshCountdownText()
    {
        if (countdownText == null || _isReady)
            return;

        int totalSeconds = Mathf.CeilToInt(Mathf.Max(0f, _remainingSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        countdownText.text = $"{minutes}:{seconds:00}s";
    }

    private void RestoreGameTime()
    {
        if (!_ownsPause)
            return;

        if (Mathf.Approximately(Time.timeScale, 0f))
            Time.timeScale = _timeScaleBeforePanel;
        _ownsPause = false;
    }

    private void ResolveReferences()
    {
        if (entryButton == null)
            entryButton = FindButton("Treasure Chest Button");
        if (closeButton == null)
            closeButton = FindButton("Close Button");
        if (reward1Button == null)
            reward1Button = FindButton("Reward Button 1");
        if (reward2Button == null)
            reward2Button = FindButton("Reward Button 2");

        if (panelRoot == null)
        {
            Transform panel = transform.Find("Treasure Chest Panel");
            if (panel != null)
                panelRoot = panel.gameObject;
        }

        if (countdownText == null && entryButton != null)
            countdownText = entryButton.GetComponentInChildren<TextMeshProUGUI>(true);
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
        if (_buttonsBound)
            return;

        if (entryButton != null)
            entryButton.onClick.AddListener(OnEntryClicked);
        if (closeButton != null)
            closeButton.onClick.AddListener(ClosePanel);
        if (reward1Button != null)
            reward1Button.onClick.AddListener(OnReward1Clicked);
        if (reward2Button != null)
            reward2Button.onClick.AddListener(OnReward2Clicked);
        _buttonsBound = true;
    }

    private void UnbindButtons()
    {
        if (!_buttonsBound)
            return;

        if (entryButton != null)
            entryButton.onClick.RemoveListener(OnEntryClicked);
        if (closeButton != null)
            closeButton.onClick.RemoveListener(ClosePanel);
        if (reward1Button != null)
            reward1Button.onClick.RemoveListener(OnReward1Clicked);
        if (reward2Button != null)
            reward2Button.onClick.RemoveListener(OnReward2Clicked);
        _buttonsBound = false;
    }

    private void EnsureEventSystem()
    {
        if (EventSystem.current == null && localEventSystem != null && !localEventSystem.activeSelf)
            localEventSystem.SetActive(true);
    }
}
