using UnityEngine;

/// <summary>
/// 侧边栏管理器
/// 挂载到 SiderBarPanel prefab 根节点，Inspector 中绑定各 UI 引用和按钮事件。
/// </summary>
public class SideBarManager : MonoBehaviour
{
    public static SideBarManager Instance { get; private set; }

    [Header("UI 引用")]
    [Tooltip("侧边栏教学面板（整个 Panel，含教程图和关闭按钮）")]
    public GameObject sideBarPanel;

    [Tooltip("关闭按钮（右上角）")]
    public GameObject closeBtn;

    [Tooltip("进入侧边栏按钮（前置，初始显示）")]
    public GameObject openSideBarBtn;

    [Tooltip("领取奖励按钮（后置，侧边栏返回后显示）")]
    public GameObject getRewardBtn;

    [Tooltip("今日已领取按钮（当天领取成功后显示，点击关闭面板）")]
    public GameObject claimedTodayBtn;

    private string _sidebarVisitDate = string.Empty;
    private bool _initialSidebarStateChecked;

    private void Awake()
    {
        if (AdManager.NoAdsEnabled) { AdManager.HideNoAdsPlatformObject(gameObject); return; }
        Instance = this;
        // 初始化按钮状态：前置按钮显示，后置按钮隐藏
        RefreshButtonState();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// 激活当前已加载场景中的侧边栏面板 Prefab，包括初始为未激活状态的面板。
    /// 此方法只控制面板显隐，不会调用进入抖音侧边栏的 API。
    /// </summary>
    public static bool TryOpenAvailablePanel()
    {
        if (AdManager.NoAdsEnabled) return false;
        if (Instance != null && Instance.gameObject.scene.IsValid() && Instance.gameObject.scene.isLoaded)
        {
            Instance.OpenSideBarPanel();
            return true;
        }

        SideBarManager[] managers = Resources.FindObjectsOfTypeAll<SideBarManager>();
        for (int i = 0; i < managers.Length; i++)
        {
            SideBarManager manager = managers[i];
            if (manager == null || !manager.gameObject.scene.IsValid() || !manager.gameObject.scene.isLoaded)
                continue;

            manager.OpenSideBarPanel();
            return true;
        }

        Debug.LogWarning("[SideBarManager] 当前已加载场景中没有 SiderBarPanel Prefab");
        return false;
    }

    /// <summary>
    /// 根据是否已领取奖励来刷新按钮显隐
    /// - 今日已领取：只显示今日已领取按钮
    /// - 从侧边栏进入：显示领取按钮
    /// - 普通进入：显示进入侧边栏按钮
    /// </summary>
    public void RefreshButtonState()
    {
        if (AdManager.Instance == null)
            return;

        string today = System.DateTime.Now.ToString("yyyy-MM-dd");
        if (!_initialSidebarStateChecked)
        {
            _initialSidebarStateChecked = true;
            if (AdManager.Instance.IsFromSideBar)
                _sidebarVisitDate = today;
        }

        // 跨自然日后必须重新进入一次侧边栏，不能沿用昨天的返回资格。
        if (_sidebarVisitDate != today)
            _sidebarVisitDate = string.Empty;

        bool claimed = AdManager.Instance.IsSideBarRewardClaimed;
        bool visitedToday = _sidebarVisitDate == today;

        if (claimed)
        {
            if (openSideBarBtn != null) openSideBarBtn.SetActive(false);
            if (getRewardBtn != null) getRewardBtn.SetActive(false);
            if (claimedTodayBtn != null) claimedTodayBtn.SetActive(true);
            return;
        }

        if (openSideBarBtn != null)
            openSideBarBtn.SetActive(!visitedToday);

        if (getRewardBtn != null)
            getRewardBtn.SetActive(visitedToday);

        if (claimedTodayBtn != null)
            claimedTodayBtn.SetActive(false);
    }

    /// <summary>
    /// 打开侧边栏教学面板
    /// </summary>
    public void OpenSideBarPanel()
    {
        if (AdManager.NoAdsEnabled) return;
        if (sideBarPanel != null)
        {
            sideBarPanel.SetActive(true);
            // 每次打开都重新按当天领取记录刷新，确保跨过零点后无需重启游戏即可再次领取。
            RefreshButtonState();
            Debug.Log("[SideBarManager] 打开侧边栏教学面板");
        }
    }

    /// <summary>
    /// 关闭侧边栏教学面板
    /// </summary>
    public void CloseSideBarPanel()
    {
        if (sideBarPanel != null)
        {
            sideBarPanel.SetActive(false);
            Debug.Log("[SideBarManager] 关闭侧边栏教学面板");
        }
    }

    /// <summary>
    /// 进入抖音侧边栏（调用 AdManager.OpenSideBar）
    /// 成功后 onReturn 回调中会隐藏 openSideBarBtn，显示 getRewardBtn
    /// </summary>
    public void OpenTTSidebar()
    {
        Debug.Log("[SideBarManager] 点击进入侧边栏");

        if (AdManager.Instance != null)
        {
            AdManager.Instance.OpenSideBar(
                onSuccess: () =>
                {
                    Debug.Log("[SideBarManager] 侧边栏打开成功");
                },
                onFailed: () =>
                {
                    Debug.Log("[SideBarManager] 侧边栏打开失败");
                },
                onReturn: () =>
                {
                    // 从侧边栏返回游戏 —— 切换按钮：隐藏进入按钮，显示领取按钮
                    Debug.Log("[SideBarManager] 从侧边栏返回，切换按钮状态");
                    _sidebarVisitDate = System.DateTime.Now.ToString("yyyy-MM-dd");
                    RefreshButtonState();
                }
            );
        }
        else
        {
            Debug.LogWarning("[SideBarManager] AdManager.Instance 为空，无法打开侧边栏");
        }
    }

    /// <summary>
    /// 领取侧边栏奖励
    /// 调用 AdManager.ClaimSideBarReward，成功后切换为今日已领取按钮
    /// </summary>
    public void GetReward()
    {
        Debug.Log("[SideBarManager] 点击领取侧边栏奖励");

        if (AdManager.Instance != null)
        {
            if (AdManager.Instance.IsSideBarRewardClaimed)
            {
                Debug.Log("[SideBarManager] 奖励已领取，跳过");
                return;
            }

            AdManager.Instance.ClaimSideBarReward(onClaimed: () =>
            {
                // TODO:领取奖励内容
                
                
                // 保持面板打开，让玩家看到“今日已领取”；点击该按钮后再关闭面板。
                RefreshButtonState();
            });
        }
        else
        {
            Debug.LogWarning("[SideBarManager] AdManager.Instance 为空，无法领取奖励");
        }
    }
}
