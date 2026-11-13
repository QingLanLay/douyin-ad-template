using System;
using UnityEditor;
using UnityEngine;

/// <summary>推荐流宿主遮挡示意；按参考截图比例绘制，仅存在于调试窗口，不进入 Player。</summary>
internal static class FeedPreviewOverlay
{
    private static Font previewFont;
    private static bool fontSearched;
    internal static Rect Fit(Rect area, float aspect)
    {
        float width = Mathf.Min(area.width, area.height * aspect);
        float height = width / aspect;
        return new Rect(area.center.x - width * .5f, area.center.y - height * .5f, width, height);
    }

    internal static void Draw(Rect area, Texture frame, bool portrait, bool occlusion, bool lightPortraitOverlay, bool guides,
        float coveredFraction, FeedRecommendationManager manager, FeedContentConfig item, Action next)
    {
        bool fullGame = manager != null && manager.IsInFullGame;
        // 抖音 Feed 宿主始终为竖屏；横屏游戏在卡片中等比居中、上下留黑。
        float aspect = fullGame && !portrait ? 2556f / 1179f : 1179f / 2556f;
        Rect phone = Fit(new Rect(area.x + 8, area.y + 8, Mathf.Max(1, area.width - 16), Mathf.Max(1, area.height - 16)), aspect);
        EditorGUI.DrawRect(phone, Color.black);
        float scale = fullGame && !portrait ? phone.height / 390f : phone.width / 390f;
        if (frame != null)
        {
            Rect game = phone;
            if (!fullGame && !portrait) game = Fit(phone, 2556f / 1179f);
            // 与 GameView 本身保持一致：顶部为 UV 原点的图形后端需要翻转合成 RT。
            // 只翻转游戏画面，不影响宿主遮挡层，也不修改相机、Canvas 或源纹理。
            game = Fit(game, (float)frame.width / frame.height);
            Rect uv = frame is RenderTexture && SystemInfo.graphicsUVStartsAtTop
                ? new Rect(0f, 1f, 1f, -1f)
                : new Rect(0f, 0f, 1f, 1f);
            GUI.DrawTextureWithTexCoords(game, frame, uv, false);
        }
        else Label(phone, .12f, .4f, .76f, .15f, "等待 GameView 实时画面\n请启动预览", scale, 15, TextAnchor.MiddleCenter);

        if (!occlusion) return;
        if (fullGame)
        {
            Box(phone, .71f, .025f, .26f, .065f, new Color(.1f, .1f, .1f, .55f));
            Label(phone, .71f, .025f, .26f, .065f, "...   ○", scale, 17, TextAnchor.MiddleCenter);
            if (manager != null && !manager.IsAppVisible)
            {
                EditorGUI.DrawRect(phone, new Color(0, 0, 0, .65f));
                Label(phone, .1f, .35f, .8f, .25f, "完整游戏已切后台", scale, 17, TextAnchor.MiddleCenter, true);
            }
            return;
        }
        bool light = portrait && lightPortraitOverlay;
        Color veil = light ? new Color(.67f, .66f, .70f) : Color.black;
        Gradient(phone, 0f, .18f, .63f, 0f, light ? Color.white : Color.black);
        Label(phone, .05f, .012f, .9f, .036f, "16:08                                     5G  84%", scale, 12, TextAnchor.MiddleLeft);
        Label(phone, .04f, .06f, .15f, .05f, "＜", scale, 25, TextAnchor.MiddleLeft);
        Label(phone, .86f, .06f, .1f, .05f, "搜索", scale, 13, TextAnchor.MiddleCenter);
        float begin = 1f - Mathf.Clamp(coveredFraction, .25f, .55f);
        Gradient(phone, begin, .94f - begin, 0f, .96f, veil);
        Label(phone, .08f, .68f, .84f, .045f, ">> 点击进入游戏 <<", scale, 13, TextAnchor.MiddleCenter);
        string title = "小游戏直玩预览";
        string description = item == null || string.IsNullOrWhiteSpace(item.description) ? "推荐流获客 · 遮挡比例可调" : item.description;
        Label(phone, .05f, .733f, .9f, .04f, title, scale, 17, TextAnchor.MiddleLeft, true);
        Label(phone, .05f, .778f, .9f, .035f, description, scale, 13, TextAnchor.MiddleLeft);
        Rect ignore = Relative(phone, .04f, .827f, .45f, .058f);
        Rect enter = Relative(phone, .52f, .827f, .44f, .058f);
        if (Button(ignore, "不感兴趣 / 下一条", scale, false)) next?.Invoke();
        if (Button(enter, "立即去玩", scale, true) && manager != null && manager.IsDirectPlay) manager.DebugSimulateEnter();
        Label(phone, .13f, .91f, .74f, .03f, "⌃ 上滑继续视频", scale, 12, TextAnchor.MiddleCenter);
        Box(phone, 0, .944f, 1, .056f, new Color(.03f, .03f, .03f, .95f));
        Label(phone, .04f, .95f, .69f, .04f, "小游戏 · 推荐流宿主底栏", scale, 11, TextAnchor.MiddleLeft);
        if (Button(Relative(phone, .75f, .95f, .22f, .04f), "进入游戏", scale * .75f, false) && manager != null && manager.IsDirectPlay) manager.DebugSimulateEnter();
        if (guides)
        {
            Line(phone, begin, new Color(1f, .62f, .12f));
            Line(phone, .827f, Color.red);
            Line(phone, .944f, Color.red);
        }
        if (manager != null && manager.IsDirectPlay && !manager.IsAppVisible)
        {
            EditorGUI.DrawRect(phone, new Color(0, 0, 0, .64f));
            Label(phone, .1f, .37f, .8f, .12f, "卡片隐藏 / 后台\n体验和声音暂停", scale, 19, TextAnchor.MiddleCenter, true);
            if (Button(Relative(phone, .2f, .52f, .6f, .06f), "模拟 OnShow 恢复", scale, true)) manager.DebugSimulateShow();
        }
    }

    private static Rect Relative(Rect p, float x, float y, float w, float h) => new Rect(p.x + x * p.width, p.y + y * p.height, w * p.width, h * p.height);
    private static void Box(Rect p, float x, float y, float w, float h, Color color) => EditorGUI.DrawRect(Relative(p, x, y, w, h), color);
    private static void Line(Rect p, float y, Color color) => EditorGUI.DrawRect(Relative(p, 0, y, 1, 1f / Mathf.Max(1f, p.height)), color);
    private static void Gradient(Rect p, float y, float height, float from, float to, Color color)
    {
        for (int i = 0; i < 40; i++) Box(p, 0, y + height * i / 40f, 1, height / 40f + .001f, new Color(color.r, color.g, color.b, Mathf.Lerp(from, to, i / 39f)));
    }
    private static GUIStyle TextStyle(float scale, int size, TextAnchor align, bool bold = false)
    {
        if (!fontSearched)
        {
            fontSearched = true;
            string[] fonts = AssetDatabase.FindAssets("DouyinSansBold t:Font");
            if (fonts.Length > 0) previewFont = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(fonts[0]));
        }
        var style = new GUIStyle(EditorStyles.label) { fontSize = Mathf.Max(8, Mathf.RoundToInt(size * scale)), alignment = align, fontStyle = bold ? FontStyle.Bold : FontStyle.Normal, wordWrap = true, clipping = TextClipping.Clip };
        if (previewFont != null) style.font = previewFont;
        style.normal.textColor = Color.white;
        return style;
    }
    private static void Label(Rect p, float x, float y, float w, float h, string text, float scale, int size, TextAnchor align, bool bold = false)
    {
        GUI.Label(Relative(p, x, y, w, h), text, TextStyle(scale, size, align, bold));
    }
    private static bool Button(Rect rect, string text, float scale, bool light)
    {
        EditorGUI.DrawRect(rect, light ? Color.white : new Color(.2f, .2f, .2f, .96f));
        GUIStyle style = TextStyle(scale, 14, TextAnchor.MiddleCenter, true);
        style.normal.textColor = light ? Color.black : Color.white;
        return GUI.Button(rect, text, style);
    }
}
