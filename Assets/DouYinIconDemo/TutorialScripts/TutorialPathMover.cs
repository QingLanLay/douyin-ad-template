using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>让教程指针沿起点、三个中间点和终点组成的平滑轨迹移动。</summary>
public sealed class TutorialPathMover : MonoBehaviour
{
    public enum PathLoopMode
    {
        PingPong,
        RestartFromStart
    }

    [Header("移动目标")]
    [SerializeField] private Image targetImage;

    [Header("轨迹点（相对目标初始位置）")]
    [SerializeField] private Vector2 startPoint = Vector2.zero;
    [Tooltip("可通过列表右下角的 + 和 - 自由增删中间控制点。")]
    [SerializeField] private List<Vector2> middlePoints = new List<Vector2>
    {
        new Vector2(0f, 75f),
        new Vector2(50f, 150f),
        new Vector2(-50f, 225f)
    };
    [SerializeField] private Vector2 endPoint = new Vector2(0f, 300f);

    [Header("移动参数")]
    [Tooltip("单程从起点移动到终点所需的秒数。")]
    [Min(0.01f)]
    [SerializeField] private float travelDuration = 2f;

    [Tooltip("来回模式：到终点后沿原路线返回；原点刷新模式：到终点后立即回到起点重新移动。")]
    [SerializeField] private PathLoopMode loopMode = PathLoopMode.PingPong;

    [Tooltip("控制整段移动的缓入缓出效果。")]
    [SerializeField] private AnimationCurve movementCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [SerializeField] private bool useUnscaledTime = true;

    [Header("Scene 路线辅助")]
    [SerializeField] private bool showPathGuide = true;
    [SerializeField] private Color pathColor = new Color(1f, 0.55f, 0.1f, 1f);

    [Range(8, 100)]
    [SerializeField] private int pathPreviewSegments = 48;

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
        targetRectTransform.anchoredPosition = originAnchoredPosition + EvaluatePath(0f);
    }

    private void Update()
    {
        if (targetRectTransform == null)
            return;

        elapsedTime += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
        float rawProgress = loopMode == PathLoopMode.PingPong
            ? Mathf.PingPong(elapsedTime / travelDuration, 1f)
            : Mathf.Repeat(elapsedTime / travelDuration, 1f);
        float curvedProgress = movementCurve != null
            ? movementCurve.Evaluate(rawProgress)
            : rawProgress;

        targetRectTransform.anchoredPosition = originAnchoredPosition + EvaluatePath(curvedProgress);
    }

    private void OnDisable()
    {
        if (targetRectTransform != null)
            targetRectTransform.anchoredPosition = originAnchoredPosition;
    }

    private Vector2 EvaluatePath(float progress)
    {
        float clampedProgress = Mathf.Clamp01(progress);
        int segmentCount = PathPointCount - 1;
        float scaledProgress = clampedProgress * segmentCount;
        int segmentIndex = Mathf.Min(Mathf.FloorToInt(scaledProgress), segmentCount - 1);
        float segmentProgress = scaledProgress - segmentIndex;

        Vector2 point0 = GetPathPoint(Mathf.Max(segmentIndex - 1, 0));
        Vector2 point1 = GetPathPoint(segmentIndex);
        Vector2 point2 = GetPathPoint(segmentIndex + 1);
        Vector2 point3 = GetPathPoint(Mathf.Min(segmentIndex + 2, PathPointCount - 1));

        return EvaluateCatmullRom(point0, point1, point2, point3, segmentProgress);
    }

    private static Vector2 EvaluateCatmullRom(
        Vector2 point0,
        Vector2 point1,
        Vector2 point2,
        Vector2 point3,
        float progress)
    {
        float progressSquared = progress * progress;
        float progressCubed = progressSquared * progress;

        return 0.5f * (
            2f * point1
            + (-point0 + point2) * progress
            + (2f * point0 - 5f * point1 + 4f * point2 - point3) * progressSquared
            + (-point0 + 3f * point1 - 3f * point2 + point3) * progressCubed);
    }

    private Vector2 GetPathPoint(int index)
    {
        if (index <= 0)
            return startPoint;

        if (index >= PathPointCount - 1)
            return endPoint;

        return middlePoints[index - 1];
    }

    private void SetPathPoint(int index, Vector2 value)
    {
        if (index <= 0)
            startPoint = value;
        else if (index >= PathPointCount - 1)
            endPoint = value;
        else
            middlePoints[index - 1] = value;
    }

    private int PathPointCount => (middlePoints != null ? middlePoints.Count : 0) + 2;

    private void CacheTarget()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        targetRectTransform = targetImage != null ? targetImage.rectTransform : null;
    }

    private void OnValidate()
    {
        if (middlePoints == null)
            middlePoints = new List<Vector2>();

        travelDuration = Mathf.Max(0.01f, travelDuration);
        pathPreviewSegments = Mathf.Clamp(pathPreviewSegments, 8, 100);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showPathGuide || !TryGetScenePathContext(out Transform parentTransform, out Vector3 originWorld))
            return;

        Vector3[] previewPoints = new Vector3[pathPreviewSegments + 1];
        for (int index = 0; index <= pathPreviewSegments; index++)
        {
            float progress = index / (float)pathPreviewSegments;
            previewPoints[index] = originWorld + parentTransform.TransformVector(EvaluatePath(progress));
        }

        Handles.color = pathColor;
        Handles.DrawAAPolyLine(2f, previewPoints);

        for (int index = 0; index < PathPointCount; index++)
        {
            Vector3 pointWorld = originWorld + parentTransform.TransformVector(GetPathPoint(index));
            bool isEndpoint = index == 0 || index == PathPointCount - 1;
            float pointSize = HandleUtility.GetHandleSize(pointWorld) * (isEndpoint ? 0.055f : 0.04f);

            if (index == 0)
                Handles.DrawWireDisc(pointWorld, targetRectTransform.forward, pointSize);
            else
                Handles.DrawSolidDisc(pointWorld, targetRectTransform.forward, pointSize);
        }
    }

    internal bool TryGetScenePathContext(out Transform parentTransform, out Vector3 originWorld)
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

    internal Vector2 GetEditorPathPoint(int index)
    {
        return GetPathPoint(index);
    }

    internal void SetEditorPathPoint(int index, Vector2 value)
    {
        SetPathPoint(index, value);
    }

    internal int GetEditorPathPointCount()
    {
        return PathPointCount;
    }
#endif
}

#if UNITY_EDITOR
[CustomEditor(typeof(TutorialPathMover))]
public sealed class TutorialPathMoverEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("选中该对象后，可在 Scene 视图直接拖动五个轨迹控制点。", MessageType.Info);
    }

    private void OnSceneGUI()
    {
        TutorialPathMover pathMover = (TutorialPathMover)target;
        if (!pathMover.TryGetScenePathContext(out Transform parentTransform, out Vector3 originWorld))
            return;

        int pointCount = pathMover.GetEditorPathPointCount();
        for (int index = 0; index < pointCount; index++)
        {
            Vector2 point = pathMover.GetEditorPathPoint(index);
            Vector3 pointWorld = originWorld + parentTransform.TransformVector(point);
            float labelOffset = HandleUtility.GetHandleSize(pointWorld) * 0.12f;

            EditorGUI.BeginChangeCheck();
            Vector3 movedWorld = Handles.PositionHandle(pointWorld, parentTransform.rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(pathMover, "调整教程指针轨迹点");
                Vector3 localOffset = parentTransform.InverseTransformVector(movedWorld - originWorld);
                pathMover.SetEditorPathPoint(index, new Vector2(localOffset.x, localOffset.y));
                EditorUtility.SetDirty(pathMover);
            }

            string pointName = index == 0
                ? "起点"
                : index == pointCount - 1
                    ? "终点"
                    : $"中间点 {index}";
            Handles.Label(pointWorld + Vector3.up * labelOffset, pointName);
        }
    }
}
#endif
