using UnityEngine.Events;

/// <summary>原子分享按钮，可直接添加到任意 Unity UI Button。</summary>
public sealed class DouyinShareButton : DouyinAtomicButtonBase
{
    public UnityEvent onSuccess = new UnityEvent();
    public UnityEvent onFailed = new UnityEvent();

    protected override void Execute()
    {
        if (!TryGetManager(out AdManager manager))
        {
            onFailed.Invoke();
            return;
        }

        manager.Share(onSuccess.Invoke, onFailed.Invoke);
    }
}
