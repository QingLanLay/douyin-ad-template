using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>仅编辑器：临时选择 GameView 分辨率、读取其实际合成画面，不修改 PlayerSettings 或业务 Canvas。</summary>
[Serializable]
internal sealed class FeedPreviewGameView
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [SerializeField] private int previousSize = -1;
    [SerializeField] private int appliedSize = -1;
    [NonSerialized] private EditorWindow window;
    [NonSerialized] private object group;
    [NonSerialized] private PropertyInfo sizeIndex;
    [NonSerialized] private MethodInfo renderView;
    [NonSerialized] private FieldInfo renderTexture;
    internal string Error { get; private set; }

    private bool Bind()
    {
        if (window != null && group != null) return true;
        try
        {
            Assembly editor = typeof(EditorWindow).Assembly;
            Type gameType = editor.GetType("UnityEditor.GameView", true);
            UnityEngine.Object[] existing = Resources.FindObjectsOfTypeAll(gameType);
            window = existing.Length > 0 ? (EditorWindow)existing[0] : EditorWindow.GetWindow(gameType, false, "Game", false);
            sizeIndex = gameType.GetProperty("selectedSizeIndex", AnyInstance);
            Type sizesType = editor.GetType("UnityEditor.GameViewSizes", true);
            object sizes = sizesType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null);
            group = sizesType.GetProperty("currentGroup", AnyInstance).GetValue(sizes);
            Type baseType = gameType;
            while (baseType != null && renderTexture == null)
            {
                renderTexture = baseType.GetField("m_RenderTexture", AnyInstance);
                renderView = baseType.GetMethod("RenderView", AnyInstance, null, new[] { typeof(Vector2), typeof(bool) }, null) ?? renderView;
                baseType = baseType.BaseType;
            }
            if (sizeIndex == null || renderTexture == null) throw new MissingMemberException("GameView 画面或分辨率接口不可用");
            Error = null;
            return true;
        }
        catch (Exception ex)
        {
            Error = "GameView 适配失败：" + ex.GetBaseException().Message + "。可手动在 Game 窗口设置横竖屏。";
            return false;
        }
    }

    internal bool Apply(bool portrait)
    {
        if (!Bind()) return false;
        try
        {
            int width = portrait ? 1179 : 2556;
            int height = portrait ? 2556 : 1179;
            Type type = group.GetType();
            int count = (int)type.GetMethod("GetTotalCount", AnyInstance).Invoke(group, null);
            MethodInfo getSize = type.GetMethod("GetGameViewSize", AnyInstance);
            int index = -1;
            for (int i = 0; i < count; i++)
            {
                object item = getSize.Invoke(group, new object[] { i });
                Type itemType = item.GetType();
                if ((int)itemType.GetProperty("width", AnyInstance).GetValue(item) == width &&
                    (int)itemType.GetProperty("height", AnyInstance).GetValue(item) == height)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                Assembly assembly = typeof(EditorWindow).Assembly;
                Type itemType = assembly.GetType("UnityEditor.GameViewSize", true);
                Type enumType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                object fixedResolution = Enum.Parse(enumType, "FixedResolution");
                object item = Activator.CreateInstance(itemType, AnyInstance, null,
                    new[] { fixedResolution, (object)width, height, "直玩预览 " + (portrait ? "竖屏" : "横屏") }, null);
                type.GetMethod("AddCustomSize", AnyInstance).Invoke(group, new[] { item });
                index = count;
            }
            if (previousSize < 0) previousSize = (int)sizeIndex.GetValue(window);
            SelectSize(index);
            appliedSize = index;
            return true;
        }
        catch (Exception ex)
        {
            Error = "无法切换 GameView 尺寸：" + ex.GetBaseException().Message;
            return false;
        }
    }

    private void SelectSize(int index)
    {
        MethodInfo callback = window.GetType().GetMethod("SizeSelectionCallback", AnyInstance);
        if (callback != null) callback.Invoke(window, new object[] { index, null });
        else sizeIndex.SetValue(window, index);
        window.Repaint();
    }

    internal Texture ReadFrame()
    {
        if (!Bind()) return null;
        try
        {
            // 读取 GameView 的合成 RenderTexture，包含 Overlay Canvas，不用系统截图或鼠标键盘。
            if (Event.current != null && Event.current.type == EventType.Repaint && renderView != null && EditorWindow.focusedWindow != window)
            {
                RenderTexture active = RenderTexture.active;
                try { renderView.Invoke(window, new object[] { Vector2.zero, false }); }
                finally { RenderTexture.active = active; }
            }
            return renderTexture.GetValue(window) as Texture;
        }
        catch (Exception ex)
        {
            Error = "实时画面暂不可用：" + ex.GetBaseException().Message;
            return null;
        }
    }

    internal void Restore()
    {
        if (previousSize < 0) return;
        try
        {
            if (Bind() && (int)sizeIndex.GetValue(window) == appliedSize) SelectSize(previousSize);
        }
        catch (Exception) { /* 不覆盖用户手动更改的 GameView。 */ }
        previousSize = appliedSize = -1;
    }
}
