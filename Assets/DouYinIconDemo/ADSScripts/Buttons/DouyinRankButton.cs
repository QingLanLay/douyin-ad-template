using UnityEngine.Events;

/// <summary>原子排行榜按钮，直接调用抖音排行榜。</summary>
public sealed class DouyinRankButton : DouyinAtomicButtonBase
{
    public UnityEvent onRequested = new UnityEvent();

    protected override void Execute()
    {
        if (!TryGetManager(out AdManager manager))
            return;

        manager.ShowRank();
        onRequested.Invoke();
    }
}
