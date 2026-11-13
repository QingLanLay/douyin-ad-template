#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using TTSDK;
using TTSDK.UNBridgeLib;
using TTSDK.UNBridgeLib.LitJson;

namespace Douyin.Feed
{
    /// <summary>通过抖音 SDK 播放配音，并异步读取当前音源的实际播放位置。</summary>
    internal sealed class FeedDouyinAudio : IDisposable
    {
        private static readonly List<FeedDouyinAudio> retired = new List<FeedDouyinAudio>();
        private readonly TTAudioManager.InnerAudioContext audio;
        private readonly TTFileSystemManager files;
        private readonly string path;
        private readonly double cueDuration;
        private bool disposed, released, visible, requested, issued, confirmed, paused, buffering, ended, readyReplay;
        private bool positionPending, pausePending, positionCurrent;
        private int revision;
        private double position = -1, requestedPosition, waitingPosition, nextRead;
        private double volume = 1;
        private string error;

        public FeedDouyinAudio(byte[] bytes, double duration)
        {
            if (bytes == null || bytes.Length < 4 || duration <= 0)
                throw new ArgumentException("抖音配音文件数据无效。");
            if (!TTAudioManager.CanUseNativeAudio())
                throw new InvalidOperationException("当前抖音版本不支持 Unity 原生音频，请更新抖音后重试。");
            CleanupRetired();
            cueDuration = duration;
            files = TT.GetFileSystemManager();
            string extension = Extension(bytes);
            var info = TT.GetSystemInfo();
            bool desktop = info != null && (string.Equals(info.platform, "devtools", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.platform, "windows", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(info.platform, "mac", StringComparison.OrdinalIgnoreCase));
            // SDK 文件写入会转换桌面路径，音频服务使用相同的实际文件协议。
            string directory = desktop ? "ttfile://user" : TTFileSystemManager.USER_DATA_PATH;
            path = directory + "/feed-voice-" + Guid.NewGuid().ToString("N") + extension;
            string writeError = files.WriteFileSync(path, bytes);
            if (!string.IsNullOrEmpty(writeError)) throw new InvalidOperationException("配音文件写入失败：" + writeError);
            try
            {
                audio = TTAudioManager.CreateInnerAudioContext(new TTAudioManager.InnerAudioContextOptions
                { src = path, autoPlay = false, loop = false, volume = 1, playbackRate = 1, needDownload = false });
                if (audio == null) throw new InvalidOperationException("抖音音频实例创建失败。");
                BindCallbacks();
            }
            catch { files.UnlinkSync(path); throw; }
        }

        public void Play(bool resume)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FeedDouyinAudio));
            if (ended || error != null || (visible && requested)) return;
            revision++;
            visible = requested = true;
            issued = confirmed = paused = buffering = readyReplay = false;
            positionPending = pausePending = positionCurrent = false;
            BindCallbacks();
            requestedPosition = Math.Max(0, position);
            nextRead = 0;
            TryPlay();
        }

        private void TryPlay()
        {
            if (disposed || !visible || !requested || issued || ended || error != null || audio.AudioId < 0) return;
            issued = true;
            audio.Volume = volume;
            audio.Play();
        }

        public void Pause()
        {
            if (disposed) return;
            revision++;
            visible = requested = issued = confirmed = buffering = false;
            positionPending = pausePending = false;
            paused = true;
            if (audio.AudioId >= 0 && !ended && error == null) audio.Pause();
        }

        public void Stop()
        {
            if (disposed) return;
            revision++;
            visible = requested = issued = confirmed = buffering = false;
            positionPending = pausePending = false;
            ended = true;
            if (audio.AudioId >= 0) audio.Stop();
        }

        public void SetVolume(double value)
        {
            double next = Math.Max(0, Math.Min(1, value));
            if (volume == next) return;
            volume = next;
            if (!disposed && audio.AudioId >= 0) audio.Volume = volume;
        }

        public FeedPlaybackController.AudioObservation Observe(double now)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FeedDouyinAudio));
            CleanupRetired();
            TryPlay();
            if (!disposed && visible && issued && !ended && error == null && now >= nextRead)
            {
                nextRead = now + .1;
                ReadPosition();
                ReadPausedState();
            }
            double nativeDuration = audio.Duration;
            return new FeedPlaybackController.AudioObservation
            {
                Position = position,
                Duration = nativeDuration > 0 && !double.IsNaN(nativeDuration) && !double.IsInfinity(nativeDuration) ? nativeDuration : cueDuration,
                Confirmed = visible && issued && confirmed && !paused && !buffering && !ended && error == null && positionCurrent && position >= 0,
                Paused = visible && paused && !ended && error == null,
                Buffering = visible && buffering,
                Ended = ended,
                Failed = error != null,
                Error = error
            };
        }

        private void ReadPosition()
        {
            if (positionPending) return;
            positionPending = true;
            int request = revision;
            ReadProperty("currentTime", result =>
            {
                if (disposed || request != revision || !visible || ended || error != null) return;
                positionPending = false;
                double value = Number(result);
                if (value < 0 || double.IsNaN(value) || double.IsInfinity(value))
                { OnError("音频播放位置无效。"); return; }
                position = value;
                positionCurrent = true;
                if (!paused && value > requestedPosition + .01) confirmed = true;
                if (buffering && value > waitingPosition + .01) { buffering = false; confirmed = true; }
            });
        }

        private void ReadPausedState()
        {
            if (pausePending) return;
            pausePending = true;
            int request = revision;
            ReadProperty("paused", result =>
            {
                if (disposed || request != revision || !visible || ended || error != null) return;
                pausePending = false;
                if (!result.IsBoolean) { OnError("音频暂停状态无效。"); return; }
                bool value = (bool)result;
                // 起播事件之前可能仍读到音源准备时的暂停状态，继续等待起播确认。
                if (value && !confirmed) return;
                paused = value;
                if (value) confirmed = false;
            });
        }

        // 原生服务支持标量及包含音源标识的属性对象。
        private void ReadProperty(string property, Action<JsonData> receive)
        {
            int request = revision;
            var parameters = new JsonData();
            parameters["id"] = audio.AudioId;
            parameters["property"] = property;
            UNBridge.Call("readInnerAudioContextProperty", parameters, new BridgeCallBack
            {
                OnSuccess = result =>
                {
                    if (disposed || request != revision || !visible || ended || error != null) return;
                    if (result != null && result.IsObject)
                    {
                        if (result.ContainsKey("id") && Number(result["id"]) != audio.AudioId)
                        { OnError("音频属性所属音源无效：" + property); return; }
                        result = result.ContainsKey("value") ? result["value"] : null;
                    }
                    if (result == null) { OnError("音频属性返回为空：" + property); return; }
                    receive(result);
                },
                OnFailed = (code, message) =>
                {
                    if (!disposed && request == revision && visible && !ended)
                        OnError("音频属性读取失败：" + property + " " + code + " " + message);
                },
                OnTimeout = () =>
                {
                    if (!disposed && request == revision && visible && !ended)
                        OnError("音频属性读取超时：" + property);
                }
            });
        }

        private static double Number(JsonData value)
        {
            if (value == null) return double.NaN;
            return value.IsDouble ? (double)value : value.IsInt ? (int)value :
                value.IsLong ? (long)value : double.NaN;
        }

        private static string Extension(byte[] bytes)
        {
            if (bytes[0] == 82 && bytes[1] == 73) return ".wav";
            if (bytes.Length >= 8 && bytes[4] == 102 && bytes[5] == 116 && bytes[6] == 121 && bytes[7] == 112) return ".m4a";
            if (bytes[0] == 255 && (bytes[1] & 246) == 240) return ".aac";
            if (bytes[0] == 79 && bytes[1] == 103)
                throw new InvalidOperationException("抖音 SDK 配音请使用 WAV、MP3、M4A 或 AAC 文件。");
            return ".mp3";
        }

        private void BindCallbacks()
        {
            int current = revision;
            audio.onCanPlay = () => Dispatch(current, OnCanPlay);
            audio.onPlay = () =>
            {
                if (!disposed && current != revision && !visible && audio.AudioId >= 0)
                    audio.Pause();
                else Dispatch(current, OnPlay);
            };
            audio.onPause = () => Dispatch(current, OnPause);
            audio.onStop = () => Dispatch(current, OnStop);
            audio.onEnded = () => Dispatch(current, OnEnded);
            audio.onWaiting = () => Dispatch(current, OnWaiting);
            audio.onError = message => Dispatch(current, () => OnError(message));
        }

        private void Dispatch(int current, Action callback)
        {
            if (disposed) { ReleaseIfReady(); return; }
            if (current != revision) return;
            callback();
        }

        private void OnCanPlay()
        {
            if (disposed) { ReleaseIfReady(); return; }
            if (visible && requested && issued && !confirmed && !readyReplay)
            {
                readyReplay = true;
                issued = false;
            }
            TryPlay();
        }
        private void OnPlay()
        {
            if (disposed) { ReleaseIfReady(); return; }
            if (!visible || !requested || !issued || ended || error != null)
            { if (audio.AudioId >= 0) audio.Pause(); return; }
            confirmed = true;
            paused = buffering = false;
            nextRead = 0;
        }
        private void OnPause() { if (!disposed && !ended && error == null) { confirmed = false; paused = true; } }
        private void OnStop() { if (!disposed && error == null) { confirmed = false; ended = true; } }
        private void OnEnded() { if (!disposed && error == null) { confirmed = paused = buffering = false; ended = true; } }
        private void OnWaiting()
        {
            if (!disposed && visible && !ended && error == null) { buffering = true; waitingPosition = Math.Max(0, position); }
        }
        private void OnError(string message)
        {
            if (disposed) { ReleaseIfReady(); return; }
            error = "抖音原生音频：" + message;
            confirmed = paused = buffering = false;
            if (audio.AudioId >= 0) audio.Pause();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            visible = requested = confirmed = false;
            revision++;
            if (!ReleaseIfReady()) retired.Add(this);
        }

        private bool ReleaseIfReady()
        {
            if (released) return true;
            if (audio.AudioId < 0) return false;
            released = true;
            audio.onCanPlay = audio.onPlay = audio.onPause = audio.onStop = audio.onEnded = audio.onWaiting = null;
            audio.onError = null;
            try { audio.Destroy(); }
            finally { files.UnlinkSync(path); retired.Remove(this); }
            return true;
        }

        /// <summary>释放在创建回包之前已取消的音源，保留其所属文件直到原生音源销毁。</summary>
        internal static void CleanupRetired()
        {
            for (int i = retired.Count - 1; i >= 0; i--)
                retired[i].ReleaseIfReady();
        }
    }
}
#endif
