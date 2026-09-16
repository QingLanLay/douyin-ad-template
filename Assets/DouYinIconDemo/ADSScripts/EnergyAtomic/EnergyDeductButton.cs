using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>原子扣体力按钮，可直接放到任意场景或 UI 中。</summary>
[RequireComponent(typeof(Button))]
[DisallowMultipleComponent]
public sealed class EnergyDeductButton : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField, Min(1)] private int amount = 1;

    public UnityEvent onDeducted = new UnityEvent();
    public UnityEvent onUnavailable = new UnityEvent();

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();
    }

    private void OnEnable()
    {
        if (button == null)
            button = GetComponent<Button>();
        button.onClick.AddListener(Execute);
    }

    private void OnDisable()
    {
        if (button != null)
            button.onClick.RemoveListener(Execute);
    }

    private void Execute()
    {
        EnergySystem energy = EnergySystem.Instance;
        if (energy == null)
        {
            Debug.LogWarning("[EnergyDeductButton] 常驻 DouyinAdSystem 上没有 EnergySystem。", this);
            onUnavailable.Invoke();
            return;
        }

        energy.DeductEnergy(amount);
        onDeducted.Invoke();
    }
}
