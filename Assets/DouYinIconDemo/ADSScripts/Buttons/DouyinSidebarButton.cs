using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>侧边栏按钮：打开场景教学面板；无广告模式直接执行完成回调。</summary>
public sealed class DouyinSidebarButton : DouyinAtomicButtonBase
{
    [Tooltip("侧边栏面板打开后的回调；无广告模式点击时直接执行，可绑定奖励业务。")]
    [FormerlySerializedAs("onOpened")]
    public UnityEvent onPanelOpened = new UnityEvent();

    [FormerlySerializedAs("onFailed")]
    public UnityEvent onPanelMissing = new UnityEvent();

    protected override void Execute()
    {
        if (AdManager.NoAdsEnabled)
        {
            onPanelOpened.Invoke();
            return;
        }

        if (SideBarManager.TryOpenAvailablePanel())
        {
            onPanelOpened.Invoke();
            return;
        }

        onPanelMissing.Invoke();
    }
}
