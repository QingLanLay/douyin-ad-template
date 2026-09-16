using UnityEngine;
using UnityEngine.UI;

/// <summary>抖音原子按钮基类：按钮只依赖常驻 AdManager，不持有其他场景对象。</summary>
[RequireComponent(typeof(Button))]
public abstract class DouyinAtomicButtonBase : MonoBehaviour
{
    [SerializeField] private Button button;

    protected virtual void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        button.onClick.AddListener(Execute);
    }

    protected virtual void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(Execute);
    }

    protected bool TryGetManager(out AdManager manager)
    {
        manager = AdManager.Instance;
        if (manager != null)
            return true;

        Debug.LogWarning($"[{GetType().Name}] 场景中没有常驻 AdManager，按钮调用已取消。", this);
        return false;
    }

    protected abstract void Execute();
}
