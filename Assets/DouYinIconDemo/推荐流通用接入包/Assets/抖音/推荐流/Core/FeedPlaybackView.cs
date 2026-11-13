using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace Douyin.Feed
{
    /// <summary>保存字幕预制体、配音资源、错误页面与背景音乐引用。</summary>
    public sealed class FeedPlaybackView : MonoBehaviour
    {
        [Serializable]
        public sealed class Narration
        {
            public string key = "home";
            public AudioClip clip;
            public string audioUrl;
            public TextAsset cues;
        }
        public static FeedPlaybackView Existing { get; private set; }
        [SerializeField] private TextMeshProUGUI subtitle;
        [SerializeField] private AudioSource voiceSource, bgmSource;
        [SerializeField] private GameObject fallbackPanel;
        [SerializeField] private TextMeshProUGUI fallbackText;
        [SerializeField] private Button retryButton;
        [SerializeField] private Narration[] narrations = Array.Empty<Narration>();
        [SerializeField] private bool allowSilentPreview = true;
        [SerializeField] private bool voiceMuted;
        [Range(0, 1), SerializeField] private float bgmDucking = .2f;
        private FeedPlaybackController.UnityAudio voiceAudio;
        private AudioClip downloadedClip, duckClip;
        private Action retry;
        private bool ducked;
        private float originalVolume, appliedVolume;
        public bool Muted => voiceMuted || AudioListener.volume <= 0;
        public TextMeshProUGUI Subtitle => subtitle;
        public AudioSource VoiceSource => voiceSource;
        public bool PresentationReady { get; set; } = true;

        private void Awake()
        {
            if (Existing != null && Existing != this) { Destroy(gameObject); return; }
            Existing = this;
            DontDestroyOnLoad(gameObject);
            voiceSource.playOnAwake = false;
            voiceSource.loop = false;
            voiceSource.ignoreListenerPause = true;
            subtitle.maxVisibleLines = 2;
            SetSubtitle(null, false);
            fallbackPanel.SetActive(false);
            retryButton.onClick.AddListener(Retry);
        }
        public void ConfigureNarrations(Narration[] entries, bool allowSilent = false)
        {
            narrations = entries ?? Array.Empty<Narration>();
            allowSilentPreview = allowSilent;
        }
        public IEnumerator Prepare(FeedPlaybackController.Preparation request)
        {
            Narration entry = Array.Find(narrations, item => item != null && item.key == request.Key);
            if (entry == null)
            {
                if (!allowSilentPreview) { request.Fail("当前入口没有配音字幕资源。"); yield break; }
                request.Complete(new FeedPlaybackController.Media { Key = request.Key, Silent = true });
                yield break;
            }
            if (entry.cues == null) { request.Fail("当前配音缺少字幕时间表。"); yield break; }
            FeedCueSheet sheet = JsonUtility.FromJson<FeedCueSheet>(entry.cues.text);
            if (sheet == null || sheet.key != request.Key) { request.Fail("配音与字幕标识不匹配。"); yield break; }
            voiceAudio = new FeedPlaybackController.UnityAudio(voiceSource, request.Key, TTSDK.TT.InContainerEnv);
            if (entry.clip != null) voiceAudio.Bind(entry.clip, sheet.duration);
            else
            {
                if (string.IsNullOrWhiteSpace(entry.audioUrl)) { request.Fail("当前配音缺少音频文件。"); yield break; }
                string url = entry.audioUrl;
                if (!Uri.TryCreate(url, UriKind.Absolute, out _)) url = Application.streamingAssetsPath.TrimEnd('/') + "/" + url.TrimStart('/');
#if UNITY_WEBGL && !UNITY_EDITOR
                if (TTSDK.TT.InContainerEnv)
                {
                    using (var download = UnityWebRequest.Get(url))
                    {
                        download.timeout = 30;
                        download.SendWebRequest();
                        while (!download.isDone && request.IsCurrent) yield return null;
                        if (!request.IsCurrent) { download.Abort(); yield break; }
                        if (download.result != UnityWebRequest.Result.Success) { request.Fail(download.error); yield break; }
                        voiceAudio.BindEncoded(download.downloadHandler.data, sheet.duration);
                    }
                }
                else
#endif
                {
                    AudioType type = url.Split('?')[0].EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioType.MPEG :
                        url.Split('?')[0].EndsWith(".ogg", StringComparison.OrdinalIgnoreCase) ? AudioType.OGGVORBIS : AudioType.WAV;
                    using (var download = UnityWebRequestMultimedia.GetAudioClip(url, type))
                    {
                        download.timeout = 30;
                        download.SendWebRequest();
                        while (!download.isDone && request.IsCurrent) yield return null;
                        if (!request.IsCurrent) { download.Abort(); yield break; }
                        if (download.result != UnityWebRequest.Result.Success) { request.Fail(download.error); yield break; }
                        downloadedClip = DownloadHandlerAudioClip.GetContent(download);
                        voiceAudio.Bind(downloadedClip, sheet.duration);
                    }
                }
            }
            request.Complete(new FeedPlaybackController.Media { Key = request.Key, Sheet = sheet, Audio = voiceAudio });
        }
        public void PresentContent() { fallbackPanel.SetActive(false); retry = null; }
        public void PresentFallback(string message, Action retryAction)
        {
            SetSubtitle(null, false);
            fallbackText.text = message;
            retry = retryAction;
            fallbackPanel.SetActive(true);
        }
        public bool FlushPresentation(bool fallback)
        {
            if (!isActiveAndEnabled || !PresentationReady) return false;
            Canvas.ForceUpdateCanvases();
            return !fallback || fallbackPanel.activeInHierarchy;
        }
        public void SetSubtitle(string text, bool visible)
        {
            subtitle.text = text ?? string.Empty;
            subtitle.enabled = visible && !string.IsNullOrEmpty(text);
        }
        public void SetDucking(bool enabled)
        {
            if (enabled && !ducked && bgmSource != null)
            {
                originalVolume = bgmSource.volume;
                appliedVolume = originalVolume * bgmDucking;
                duckClip = bgmSource.clip;
                bgmSource.volume = appliedVolume;
                ducked = true;
            }
            else if (!enabled && ducked)
            {
                if (bgmSource != null && bgmSource.clip == duckClip && Mathf.Approximately(bgmSource.volume, appliedVolume)) bgmSource.volume = originalVolume;
                ducked = false;
            }
        }
        public void ReleaseContent()
        {
            SetSubtitle(null, false);
            SetDucking(false);
            voiceAudio?.Dispose(); voiceAudio = null;
            if (downloadedClip != null) Destroy(downloadedClip);
            downloadedClip = null;
            fallbackPanel.SetActive(false);
            retry = null;
        }
        private void Retry() => retry?.Invoke();
        private void OnDisable()
        {
            if (Existing != this) return;
            FeedRecommendationManager.ExistingInstance?.Playback?.EndSession();
            ReleaseContent();
        }
        private void OnDestroy()
        {
            if (Existing != this) return;
            retryButton.onClick.RemoveListener(Retry);
            Existing = null;
        }
    }
}

