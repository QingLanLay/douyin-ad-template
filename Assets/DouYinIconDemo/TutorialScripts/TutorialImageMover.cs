using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>让教程 Image UI 在起点和终点两个参考点之间往返移动。</summary>
public sealed class TutorialImageMover : MonoBehaviour
{
    [Header("移动目标")]
    [SerializeField] private Image targetImage;

    [Header("参考点（相对目标初始位置）")]
    [SerializeField] private Vector2 startPoint = Vector2.zero;
    [SerializeField] private Vector2 endPoint = new Vector2(0f, 50f);

    [Header("移动参数")]

    [Tooltip("完成一次往返移动所需的秒数。")]
    [Min(0.01f)]
    [SerializeField] private float cycleDuration = 1f;

    [Tooltip("控制单程移动的贝塞尔缓动效果：横轴为时间，纵轴为移动进度。")]
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("勾选后，暂停游戏时引导动画仍会继续。")]
    [SerializeField] private bool useUnscaledTime = true;

    [Header("Scene 可视化")]
    [Tooltip("选中该组件时，在 Scene 视图显示移动轨迹。")]
    [SerializeField] private bool showMovementGuide = true;

    [SerializeField] private Color guideColor = new Color(0.1f, 0.9f, 1f, 1f);

    private RectTransform targetRectTransform;
    private Vector2 originAnchoredPosition;
    private float elapsedTime;

    private void Awake()
    {
        CacheTarget();
    }

    private void OnEnable()
    {
        CacheTarget();
        elapsedTime = 0f;

        if (targetRectTransform == null)
            return;

        originAnchoredPosition = targetRectTransform.anchoredPosition;
        targetRectTransform.anchoredPosition = originAnchoredPosition + startPoint;
    }

    private void Update()
    {
        if (targetRectTransform == null)
            return;

        elapsedTime += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

        float progress = Mathf.PingPong(elapsedTime * 2f / cycleDuration, 1f);
        float curvedProgress = movementCurve != null ? movementCurve.Evaluate(progress) : progress;
        targetRectTransform.anchoredPosition = originAnchoredPosition
            + Vector2.LerpUnclamped(startPoint, endPoint, curvedProgress);
    }

    private void OnDisable()
    {
        if (targetRectTransform != null)
            targetRectTransform.anchoredPosition = originAnchoredPosition;
    }

    private void CacheTarget()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        targetRectTransform = targetImage != null ? targetImage.rectTransform : null;
    }

    private void OnValidate()
    {
        cycleDuration = Mathf.Max(0.01f, cycleDuration);
    }

    private void OnDrawGizmosSelected()
    {
#if UNITY_EDITOR
        if (!showMovementGuide)
            return;

        if (!TryGetSceneReferenceContext(out Transform parentTransform, out Vector3 originWorld))
            return;

        Vector3 startWorld = originWorld + parentTransform.TransformVector(startPoint);
        Vector3 endWorld = originWorld + parentTransform.TransformVector(endPoint);
        Vector3 canvasNormal = targetRectTransform.forward;
        float startHandleSize = HandleUtility.GetHandleSize(startWorld) * 0.04f;
        float endHandleSize = HandleUtility.GetHandleSize(endWorld) * 0.05f;

        Handles.color = guideColor;
        Handles.DrawAAPolyLine(2f, startWorld, endWorld);
        Handles.DrawWireDisc(startWorld, canvasNormal, startHandleSize);
        Handles.DrawSolidDisc(endWorld, canvasNormal, endHandleSize);

        Vector3 worldDirection = (endWorld - startWorld).normalized;
        Vector3 worldPerpendicular = Vector3.Cross(canvasNormal, worldDirection).normalized;
        float arrowLength = HandleUtility.GetHandleSize(endWorld) * 0.12f;
        float arrowWidth = arrowLength * 0.45f;
        Vector3 arrowSideA = endWorld - worldDirection * arrowLength + worldPerpendicular * arrowWidth;
        Vector3 arrowSideB = endWorld - worldDirection * arrowLength - worldPerpendicular * arrowWidth;
        Handles.DrawAAPolyLine(2f, arrowSideA, endWorld, arrowSideB);
#endif
    }

#if UNITY_EDITOR
    internal bool TryGetSceneReferenceContext(out Transform parentTransform, out Vector3 originWorld)
    {
        CacheTarget();
        if (targetRectTransform == null || targetRectTransform.parent == null)
        {
            parentTransform = null;
            originWorld = Vector3.zero;
            return false;
        }

        parentTransform = targetRectTransform.parent;
        originWorld = Application.isPlaying
            ? parentTransform.TransformPoint(originAnchoredPosition)
            : targetRectTransform.position;
        return true;
    }

    internal Vector2 GetEditorReferencePoint(int index)
    {
        return index == 0 ? startPoint : endPoint;
    }

    internal void SetEditorReferencePoint(int index, Vector2 value)
    {
        if (index == 0)
            startPoint = value;
        else
            endPoint = value;
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(TutorialImageMover))]
public sealed class TutorialImageMoverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("选中对象后，可在 Scene 视图直接拖动起点和终点。", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        TutorialImageMover imageMover = (TutorialImageMover)target;
        if (!imageMover.TryGetSceneReferenceContext(out Transform parentTransform, out Vector3 originWorld))
            return;

        for (int index = 0; index < 2; index++)
        {
            Vector2 point = imageMover.GetEditorReferencePoint(index);
            Vector3 pointWorld = originWorld + parentTransform.TransformVector(point);

            EditorGUI.BeginChangeCheck();
            Vector3 movedWorld = Handles.PositionHandle(pointWorld, parentTransform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(imageMover, "调整教程指针参考点");
                Vector3 localOffset = parentTransform.InverseTransformVector(movedWorld - originWorld);
                imageMover.SetEditorReferencePoint(index, new Vector2(localOffset.x, localOffset.y));
                EditorUtility.SetDirty(imageMover);
            }

            float labelOffset = HandleUtility.GetHandleSize(pointWorld) * 0.12f;
            Handles.Label(pointWorld + Vector3.up * labelOffset, index == 0 ? "起点" : "终点");
        }
    }
}
#endif
