using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>在场景中调整任一按钮的红点位置时，同步同场景其他按钮的相对位置，不应用或修改预制体。</summary>
[InitializeOnLoad]
public static class DailyRedDotLayoutSync
{
    private static RectTransform pendingSource;
    private static int pendingUndoGroup;
    private static bool scheduled;
    private static bool applying;
    private static RectTransform selectedDot;
    private static Vector2 selectedPosition;
    private static Vector2 selectedAnchorMin;
    private static Vector2 selectedAnchorMax;
    private static Vector2 selectedPivot;
    private static double nextSelectionCheck;

    static DailyRedDotLayoutSync()
    {
        Undo.postprocessModifications += OnModifications;
        Selection.selectionChanged += CaptureSelectedDot;
        EditorApplication.update += CheckSelectedPosition;
    }

    private static RectTransform GetSelectedSceneRedDot()
    {
        GameObject selected = Selection.activeGameObject;
        RectTransform rect = selected == null ? null : selected.GetComponent<RectTransform>();
        return IsSceneRedDot(rect) ? rect : null;
    }

    private static void CaptureSelectedDot()
    {
        selectedDot = GetSelectedSceneRedDot();
        if (selectedDot != null)
            RememberSelectedPosition();
    }

    private static void RememberSelectedPosition()
    {
        selectedPosition = selectedDot.anchoredPosition;
        selectedAnchorMin = selectedDot.anchorMin;
        selectedAnchorMax = selectedDot.anchorMax;
        selectedPivot = selectedDot.pivot;
    }

    private static void CheckSelectedPosition()
    {
        if (applying || EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.timeSinceStartup < nextSelectionCheck)
            return;

        nextSelectionCheck = EditorApplication.timeSinceStartup + 0.08d;
        if (scheduled)
            Flush();

        // 后台 Editor 的 Selection 回调可能延后，主动核对当前选中对象。
        GameObject active = Selection.activeGameObject;
        if (selectedDot == null || active != selectedDot.gameObject)
        {
            CaptureSelectedDot();
            return;
        }
        // 兼容 Scene 拖动以及没有产生 Undo 后处理回调的 Inspector 编辑。
        if (selectedDot.anchoredPosition == selectedPosition && selectedDot.anchorMin == selectedAnchorMin &&
            selectedDot.anchorMax == selectedAnchorMax && selectedDot.pivot == selectedPivot)
            return;

        RememberSelectedPosition();
        Queue(selectedDot);
    }

    private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
    {
        if (applying || EditorApplication.isPlayingOrWillChangePlaymode)
            return modifications;

        foreach (UndoPropertyModification modification in modifications)
        {
            PropertyModification current = modification.currentValue;
            RectTransform rect = current.target as RectTransform;
            if (IsPositionProperty(current.propertyPath) && IsSceneRedDot(rect))
                Queue(rect);
        }

        return modifications;
    }

    private static bool IsPositionProperty(string path)
    {
        if (path == null)
            return false;

        return IsProperty(path, "m_AnchoredPosition") || IsProperty(path, "m_LocalPosition") ||
               IsProperty(path, "m_AnchorMin") || IsProperty(path, "m_AnchorMax") ||
               IsProperty(path, "m_Pivot");
    }

    private static bool IsProperty(string path, string property)
    {
        return path == property || path.StartsWith(property + ".", StringComparison.Ordinal);
    }

    private static RectTransform GetRedDot(DouyinAtomicButtonBase button)
    {
        var serialized = new SerializedObject(button);
        GameObject dot = serialized.FindProperty("dailyRedDot").objectReferenceValue as GameObject;
        return dot == null ? null : dot.transform as RectTransform;
    }

    private static bool IsSceneRedDot(RectTransform rect)
    {
        if (rect == null || rect.parent == null || EditorUtility.IsPersistent(rect) ||
            !rect.gameObject.scene.IsValid() || EditorSceneManager.IsPreviewScene(rect.gameObject.scene))
            return false;

        DouyinAtomicButtonBase button = rect.parent.GetComponent<DouyinAtomicButtonBase>();
        return button != null && GetRedDot(button) == rect;
    }

    private static void Queue(RectTransform source)
    {
        pendingSource = source;
        if (scheduled)
            return;

        pendingUndoGroup = Undo.GetCurrentGroup();
        scheduled = true;
        // 下一次 Editor update 统一处理；不依赖后台可能暂停的 Inspector delayCall。
    }

    private static void Flush()
    {
        scheduled = false;
        RectTransform source = pendingSource;
        pendingSource = null;
        if (EditorApplication.isPlayingOrWillChangePlaymode || !IsSceneRedDot(source))
            return;

        Vector2 anchorMin = source.anchorMin;
        Vector2 anchorMax = source.anchorMax;
        Vector2 pivot = source.pivot;
        Vector2 position = source.anchoredPosition;
        applying = true;
        try
        {
            foreach (DouyinAtomicButtonBase button in Resources.FindObjectsOfTypeAll<DouyinAtomicButtonBase>())
            {
                // 只修改同一场景的实例，包含未激活的按钮；不写入按钮预制体资产。
                if (button.gameObject.scene != source.gameObject.scene || EditorUtility.IsPersistent(button))
                    continue;

                RectTransform target = GetRedDot(button);
                if (target == null || target == source ||
                    (target.anchorMin == anchorMin && target.anchorMax == anchorMax &&
                     target.pivot == pivot && target.anchoredPosition == position))
                    continue;

                Undo.RecordObject(target, "统一每日红点位置");
                target.anchorMin = anchorMin;
                target.anchorMax = anchorMax;
                target.pivot = pivot;
                target.anchoredPosition = position;
                // 仅记录场景内的实例覆盖，不调用 Apply，不改变每日显隐或单独尺寸。
                if (PrefabUtility.IsPartOfPrefabInstance(target))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            }

            Undo.FlushUndoRecordObjects();
            Undo.CollapseUndoOperations(pendingUndoGroup);
        }
        finally
        {
            applying = false;
        }
    }

    [MenuItem("Tools/抖音原子系统/同步所选红点位置到同场景按钮")]
    private static void SyncSelectedPosition()
    {
        RectTransform source = Selection.activeTransform as RectTransform;
        if (!EditorApplication.isPlayingOrWillChangePlaymode && IsSceneRedDot(source))
            Queue(source);
    }

    [MenuItem("Tools/抖音原子系统/同步所选红点位置到同场景按钮", true)]
    private static bool CanSyncSelectedPosition()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
               IsSceneRedDot(Selection.activeTransform as RectTransform);
    }
}
