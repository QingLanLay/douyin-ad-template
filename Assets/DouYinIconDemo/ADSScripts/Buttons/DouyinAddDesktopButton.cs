using UnityEngine;
using UnityEngine.Events;

/// <summary>原子加桌按钮。按钮始终保留，由平台状态回调区分成功、已添加和失败。</summary>
public sealed class DouyinAddDesktopButton : DouyinAtomicButtonBase
{
    public UnityEvent onSuccess = new UnityEvent();
    public UnityEvent onAlreadyAdded = new UnityEvent();
    public UnityEvent onFailed = new UnityEvent();

    protected override void Execute()
    {
        if (!TryGetManager(out AdManager manager))
        {
            onFailed.Invoke();
            return;
        }

        manager.AddToDesktop(
            success =>
            {
                if (!success)
                    return;

                onSuccess.Invoke();
            },
            (code, message) =>
            {
                Debug.LogWarning($"[DouyinAddDesktopButton] 添加桌面失败 code:{code}, message:{message}", this);
                onFailed.Invoke();
            },
            () =>
            {
                onAlreadyAdded.Invoke();
            });
    }
}
