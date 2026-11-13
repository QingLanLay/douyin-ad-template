#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Douyin.Feed;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>在目标编辑器中检查预制体、实际音源与播放生命周期。</summary>
[InitializeOnLoad]
public static class FeedPlaybackPackageValidation
{
    [Serializable] private sealed class Result
    {
        public bool passed;
        public string unityVersion;
        public string[] checks;
        public string error;
    }
    private static readonly List<string> checks = new List<string>();
    private static FeedPlaybackController player;
    private static FeedPlaybackView view;
    private static AudioClip clip;
    private static TextAsset cues;
    private static FeedRecommendationManager manager;
    private static FeedRecommendationConfig config;
    private static double started, phaseAt, hiddenPosition;
    private static int phase;
    private static string output;

    static FeedPlaybackPackageValidation()
    {
        if (!SessionState.GetBool("FeedPlaybackValidation.Active", false)) return;
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.artifacts/FeedDouyinAudio20261112"));
        started = EditorApplication.timeSinceStartup;
        EditorApplication.playModeStateChanged += OnPlayMode;
        EditorApplication.update += Update;
        EditorApplication.delayCall += () => { if (EditorApplication.isPlaying && phase == 0) OnPlayMode(PlayModeStateChange.EnteredPlayMode); };
    }
    public static void CompileWebGLAndRun()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.artifacts/FeedDouyinAudio20261112"));
        Directory.CreateDirectory(output);
        var settings = new UnityEditor.Build.Player.ScriptCompilationSettings
        {
            target = BuildTarget.WebGL,
            group = BuildTargetGroup.WebGL,
            options = UnityEditor.Build.Player.ScriptCompilationOptions.DevelopmentBuild
        };
        var result = UnityEditor.Build.Player.PlayerBuildInterface.CompilePlayerScripts(settings, Path.Combine(output, "WebGLScriptAssemblies"));
        if (result.assemblies == null || result.assemblies.Count == 0)
            throw new InvalidOperationException("WebGL 播放器脚本编译失败。");
        File.WriteAllText(Path.Combine(output, "webgl-script-compilation.json"),
            JsonUtility.ToJson(new Result { passed = true, unityVersion = Application.unityVersion, checks = new List<string>(result.assemblies).ToArray() }, true));
        Run();
    }
    public static void Run()
    {
        output = Path.GetFullPath(Path.Combine(Application.dataPath, "../.artifacts/FeedDouyinAudio20261112"));
        Directory.CreateDirectory(output);
        try
        {
            FeedPlaybackAssets.Prepare();
            checks.Add("播放器预制体引用、嵌套字幕、字体、材质、描边和阴影");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
            started = EditorApplication.timeSinceStartup;
            SessionState.SetBool("FeedPlaybackValidation.Active", true);
            EditorApplication.isPlaying = true;
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void OnPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || phase != 0) return;
        try
        {
            var prefab = Resources.Load<FeedPlaybackView>("FeedPlayback");
            view = UnityEngine.Object.Instantiate(prefab);
            clip = AudioClip.Create("FeedPlaybackValidation", 44100 * 4, 1, 44100, false);
            var samples = new float[44100 * 4];
            for (int i = 0; i < samples.Length; i++) samples[i] = Mathf.Sin(i * .031f) * .03f;
            clip.SetData(samples, 0);
            cues = new TextAsset("{\"key\":\"validation\",\"duration\":4,\"cues\":[{\"start\":0,\"end\":2,\"text\":\"推荐流字幕同步播放\"},{\"start\":2,\"end\":4,\"text\":\"配音与字幕统一管理\"}]}");
            view.ConfigureNarrations(new[] { new FeedPlaybackView.Narration { key = "validation", clip = clip, cues = cues } });
            player = new FeedPlaybackController(new Host(), () => Time.realtimeSinceStartupAsDouble);
            player.Begin(1, "validation", Time.realtimeSinceStartupAsDouble);
            phase = 1;
            phaseAt = EditorApplication.timeSinceStartup;
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void Update()
    {
        try
        {
            double now = EditorApplication.timeSinceStartup;
            if (now - started > 50) throw new TimeoutException("编辑器播放验证超时。");
            if (player == null) return;
            player.Tick(Time.realtimeSinceStartupAsDouble);
            if (player.HasPreparationError) throw new InvalidOperationException(player.Error);
            if (phase == 1 && player.IsPrepared)
            {
                Require(!view.Subtitle.enabled && !view.VoiceSource.isPlaying, "隐藏预加载保持配音与字幕暂停");
                player.Show(); phase = 2; phaseAt = now;
            }
            else if (phase == 2 && player.Position > .2 && view.Subtitle.enabled)
            {
                Require(view.Subtitle.font.HasCharacters(view.Subtitle.text), "字幕所需中文字形完整");
                Require(view.VoiceSource.isPlaying && view.Subtitle.text == "推荐流字幕同步播放", "实际 AudioSource 起播后显示对应字幕");
                Capture();
                hiddenPosition = player.Position;
                player.Hide(); phase = 3; phaseAt = now;
            }
            else if (phase == 3 && now - phaseAt > .3)
            {
                Require(!view.Subtitle.enabled && !view.VoiceSource.isPlaying && Math.Abs(player.Position - hiddenPosition) < .03, "隐藏期间音源与字幕位置保持不变");
                player.Show(); phase = 4; phaseAt = now;
            }
            else if (phase == 4 && player.Position > hiddenPosition + .2 && view.Subtitle.enabled)
            {
                Require(view.VoiceSource.isPlaying, "再次显示从同一音源位置继续");
                player.EnterGame();
                Require(!view.Subtitle.enabled && !view.VoiceSource.isPlaying && player.State == FeedPlaybackController.PlayerState.FullGame, "进入完整游戏停止配音字幕");
                player.EndSession(); player.Dispose();
                Require(Time.timeScale == 1 && !AudioListener.pause, "会话结束恢复本组件持有的暂停状态");
                FeedRecommendationProjectAdapter.Register();
                manager = FeedRecommendationManager.Instance;
                config = Resources.Load<FeedRecommendationConfig>("FeedRecommendationConfig");
                view.ConfigureNarrations(Array.Empty<FeedPlaybackView.Narration>(), true);
                manager.DebugPreviewContent(config, config.Contents[0], false, false, true);
                manager.DebugSimulateShow();
                Require(!manager.IsCardVisible, "准备期间保存显示请求并等待首页完成");
                phase = 5; phaseAt = now;
            }
            else if (phase == 5 && manager.IsSceneReady && manager.Playback.IsPrepared)
            {
                Require(manager.IsCardVisible && manager.Playback.State == FeedPlaybackController.PlayerState.Completed, "单次提前显示请求在首页完成后转发给播放器");
                manager.DebugSimulateHide(); manager.DebugSimulateShow();
                manager.DebugSimulateEnter();
                Require(manager.Playback.State == FeedPlaybackController.PlayerState.FullGame && Time.timeScale == 1, "模板FeedEnter沿统一播放器进入完整游戏");
                manager.DebugSimulateExit(); manager.DebugEndPreview();
                Require(manager.Playback.State == FeedPlaybackController.PlayerState.Idle && Time.timeScale == 1 && !AudioListener.pause, "模板会话结束释放自身暂停");
                view.ConfigureNarrations(Array.Empty<FeedPlaybackView.Narration>(), false);
                manager.DebugPreviewContent(config, config.Contents[0], false, false, true);
                phase = 6; phaseAt = now;
            }
            else if (phase == 6 && manager.Playback.IsPrepared && manager.Playback.HasPreparationError)
            {
                Require(manager.IsSceneReady && manager.Playback.State == FeedPlaybackController.PlayerState.Fallback, "缺少必需配音时呈现错误页面并提交兜底7001");
                manager.DebugEndPreview();
                view.ConfigureNarrations(Array.Empty<FeedPlaybackView.Narration>(), true);
                foreach (var item in config.Contents)
                    if (item.channel == FeedUserChannel.ReturnVisit) { manager.DebugPreviewContent(config, item, false, false, true); break; }
                manager.DebugSimulateEnter();
                phase = 7; phaseAt = now;
            }
            else if (phase == 7 && manager.IsSceneReady && manager.Playback.State == FeedPlaybackController.PlayerState.FullGame)
            {
                Require(manager.CurrentContext.Channel == FeedUserChannel.ReturnVisit && !view.Subtitle.enabled, "复访首页准备完成后处理提前FeedEnter");
                manager.DebugEndPreview();
                manager.DebugPreviewContent(config, config.Contents[0], false, false, true);
                manager.DebugSimulateShow(); manager.DebugSimulateHide();
                phase = 8; phaseAt = now;
            }
            else if (phase == 8 && manager.IsSceneReady && manager.Playback.IsPrepared)
            {
                Require(!manager.IsCardVisible && !manager.Playback.IsVisible && manager.Playback.State == FeedPlaybackController.PlayerState.Hidden,
                    "准备期间的隐藏事件取消提前显示请求");
                manager.DebugEndPreview();
                Finish(null);
            }
        }
        catch (Exception exception) { Finish(exception); }
    }
    private static void Require(bool success, string label)
    {
        if (!success) throw new InvalidOperationException(label);
        checks.Add(label);
    }
    private static void Capture()
    {
        var cameraObject = new GameObject("FeedValidationCamera", typeof(Camera));
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.16f, .21f, .29f);
        var target = new RenderTexture(540, 960, 24);
        var canvas = view.GetComponentInChildren<Canvas>();
        var mode = canvas.renderMode;
        try
        {
            target.Create(); camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            Canvas.ForceUpdateCanvases(); view.Subtitle.ForceMeshUpdate(); camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(540, 960, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 540, 960), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(output, "subtitle-preview-" + Application.unityVersion + ".png"), image.EncodeToPNG());
            UnityEngine.Object.Destroy(image);
            checks.Add("字幕画面已渲染并保存");
        }
        finally
        {
            canvas.renderMode = mode; canvas.worldCamera = null;
            RenderTexture.active = null; camera.targetTexture = null;
            target.Release(); UnityEngine.Object.Destroy(target); UnityEngine.Object.Destroy(cameraObject);
        }
    }
    private static void Finish(Exception error)
    {
        SessionState.EraseBool("FeedPlaybackValidation.Active");
        EditorApplication.update -= Update;
        EditorApplication.playModeStateChanged -= OnPlayMode;
        try { player?.Dispose(); } catch (Exception cleanup) { if (error == null) error = cleanup; }
        var result = new Result { passed = error == null, unityVersion = Application.unityVersion, checks = checks.ToArray(), error = error?.ToString() };
        File.WriteAllText(Path.Combine(output, "editor-playback-" + Application.unityVersion + ".json"), JsonUtility.ToJson(result, true));
        if (error != null) Debug.LogException(error);
        else Debug.Log("[推荐流播放验证] " + checks.Count + " 项通过。");
        EditorApplication.Exit(error == null ? 0 : 1);
    }
    private sealed class Host : FeedPlaybackController.IHost
    {
        public bool Available => view != null;
        public bool Current(int version) => true;
        public bool AudioSuspended => AudioListener.pause;
        public bool Muted => false;
        public IEnumerator Prepare(FeedPlaybackController.Preparation request) => view.Prepare(request);
        public void PresentContent() => view.PresentContent();
        public void PresentFallback(string message, Action retry) => view.PresentFallback(message, retry);
        public bool FlushPresentation(bool fallback) => view.FlushPresentation(fallback);
        public void ReleaseContent() { if (view != null) view.ReleaseContent(); }
        public void SetCardVisible(bool visible) { }
        public void SetSubtitle(string text, bool visible) => view.SetSubtitle(text, visible);
        public void SetDucking(bool enabled) => view.SetDucking(enabled);
        public void PauseGame() { Time.timeScale = 0; AudioListener.pause = true; }
        public void ResumeCardAudio() => AudioListener.pause = false;
        public void EnterGame() => ReleasePause();
        public void ExitGame() => PauseGame();
        public void SetFullGameVisible(bool visible) { }
        public void ReleasePause() { Time.timeScale = 1; AudioListener.pause = false; }
        public void ReportSceneReady(int version, bool fallback, Action<bool, string> completed) => completed(true, null);
        public void Diagnostic(string message) => Debug.Log("[播放验证] " + message);
    }
}
#endif




