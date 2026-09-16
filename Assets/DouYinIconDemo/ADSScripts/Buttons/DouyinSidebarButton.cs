using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

/// <summary>原子侧边栏按钮：只激活场景中的 SiderBarPanel，不直接调用平台侧边栏 API。</summary>
public sealed class DouyinSidebarButton : DouyinAtomicButtonBase
{
    [FormerlySerializedAs("onOpened")]
    public UnityEvent onPanelOpened = new UnityEvent();

    [FormerlySerializedAs("onFailed")]
    public UnityEvent onPanelMissing = new UnityEvent();

    protected override void Execute()
    {
        if (SideBarManager.TryOpenAvailablePanel())
        {
            onPanelOpened.Invoke();
            return;
        }

        onPanelMissing.Invoke();
    }
}
