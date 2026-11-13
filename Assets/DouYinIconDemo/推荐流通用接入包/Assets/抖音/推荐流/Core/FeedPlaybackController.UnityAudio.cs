using System;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Douyin.Feed
{
    public sealed partial class FeedPlaybackController
    {
        /// <summary>适配同一配音实例；抖音容器使用抖音播放器，Editor 与浏览器使用 Unity 音源。</summary>
        public sealed class UnityAudio : IAudio
        {
            private static UnityAudio owner;
            private readonly AudioSource source;
            private readonly bool container;
            private bool native, bound, disposed, confirmed;
#if UNITY_WEBGL && !UNITY_EDITOR
            private FeedDouyinAudio douyinAudio;
            private bool bridgeUsed;
#endif
            private double stoppedAt = -1;
            private double duration;
            public string Key { get; }
#if UNITY_WEBGL && !UNITY_EDITOR
            [DllImport("__Internal")] private static extern void FeedVoice_Remember();
            [DllImport("__Internal")] private static extern void FeedVoice_ProvideEncoded(byte[] bytes, int length);
            [DllImport("__Internal")] private static extern int FeedVoice_Bind(float duration, int container);
            [DllImport("__Internal")] private static extern void FeedVoice_Arm();
            [DllImport("__Internal")] private static extern double FeedVoice_Position();
            [DllImport("__Internal")] private static extern void FeedVoice_Release();
            [DllImport("__Internal")] private static extern int FeedVoice_Native();
            [DllImport("__Internal")] private static extern void FeedVoice_Play();
            [DllImport("__Internal")] private static extern void FeedVoice_Pause();
            [DllImport("__Internal")] private static extern void FeedVoice_Stop();
            [DllImport("__Internal")] private static extern void FeedVoice_Volume(float volume);
            [DllImport("__Internal")] private static extern int FeedVoice_Status();
            [DllImport("__Internal")] private static extern double FeedVoice_Duration();
#endif
            public UnityAudio(AudioSource source, string key, bool inContainer)
            {
                if (source == null) throw new ArgumentNullException(nameof(source));
                if (owner != null && !owner.disposed) throw new InvalidOperationException("已有推荐流配音实例正在使用音频接口。");
                owner = this;
                this.source = source;
                container = inContainer;
                Key = key;
                source.playOnAwake = false;
                source.loop = false;
#if UNITY_WEBGL && !UNITY_EDITOR
                try { if (!container) { bridgeUsed = true; FeedVoice_Remember(); } } catch { owner = null; disposed = true; throw; }
#endif
            }
            public void Bind(AudioClip clip, double cueDuration)
            {
                CheckOwner();
                if (bound || clip == null || cueDuration <= 0) throw new InvalidOperationException("配音资源无法绑定。");
                source.clip = clip;
                source.pitch = 1;
                duration = cueDuration;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (container)
                {
                    BindEncoded(EncodeClip(clip), cueDuration);
                    return;
                }
                int result = FeedVoice_Bind((float)cueDuration, container ? 1 : 0);
                native = FeedVoice_Native() != 0;
                if (container && (result == 0 || !native)) throw new InvalidOperationException("配音播放器准备失败，请重试。");
#endif
                bound = true;
            }
            /// <summary>将本次下载的音频直接交给抖音原生播放器。</summary>
            public void BindEncoded(byte[] bytes, double cueDuration)
            {
                CheckOwner();
                if (bound || bytes == null || bytes.Length == 0 || cueDuration <= 0) throw new InvalidOperationException("配音数据无法绑定。");
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!container) throw new InvalidOperationException("浏览器配音需要绑定 AudioClip。");
                if (TTSDK.TTAudioManager.CanUseNativeAudio())
                {
                    douyinAudio = new FeedDouyinAudio(bytes, cueDuration);
                    duration = cueDuration;
                    bound = true;
                    return;
                }
                bridgeUsed = true;
                FeedVoice_ProvideEncoded(bytes, bytes.Length);
                int result = FeedVoice_Bind((float)cueDuration, 1);
                native = FeedVoice_Native() != 0;
                if (result == 0 || !native) throw new InvalidOperationException("抖音配音播放器准备失败。");
                duration = cueDuration;
                bound = true;
#else
                throw new PlatformNotSupportedException("原生音频数据接口仅用于抖音 WebGL 容器。");
#endif
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            private static byte[] EncodeClip(AudioClip clip)
            {
                var samples = new float[clip.samples * clip.channels];
                if (!clip.GetData(samples, 0)) throw new InvalidOperationException("本地配音需要使用 Decompress On Load，或通过 BindEncoded 绑定下载数据。");
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream))
                {
                    int bytes = samples.Length * 2;
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
                    writer.Write((short)1); writer.Write((short)clip.channels); writer.Write(clip.frequency);
                    writer.Write(clip.frequency * clip.channels * 2); writer.Write((short)(clip.channels * 2)); writer.Write((short)16);
                    writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(bytes);
                    foreach (float sample in samples) writer.Write((short)(Mathf.Clamp(sample, -1, 1) * 32767));
                    return stream.ToArray();
                }
            }
#endif
            public void Play(bool resume)
            {
                CheckOwner();
                if (!bound) throw new InvalidOperationException("配音尚未绑定。");
                confirmed = false;
                stoppedAt = -1;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (douyinAudio != null) { douyinAudio.Play(resume); return; }
                FeedVoice_Arm();
                if (native) { FeedVoice_Play(); return; }
#endif
                if (resume) source.UnPause(); else source.Play();
            }
            public void Pause()
            {
                if (disposed || owner != this) return;
                confirmed = false;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (douyinAudio != null) { douyinAudio.Pause(); return; }
                if (native) { FeedVoice_Pause(); return; }
#endif
                if (source != null) source.Pause();
            }
            public void Stop()
            {
                if (disposed || owner != this) return;
                confirmed = false;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (douyinAudio != null) douyinAudio.Stop();
                else if (native) FeedVoice_Stop();
#endif
                if (source != null) source.Stop();
            }
            public void SetMuted(bool muted)
            {
                CheckOwner();
                source.mute = muted;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (douyinAudio != null) douyinAudio.SetVolume(muted ? 0 : source.volume * AudioListener.volume);
                else if (native) FeedVoice_Volume(muted ? 0 : source.volume * AudioListener.volume);
#endif
            }
            public AudioObservation Observe(double now)
            {
                CheckOwner();
                var result = new AudioObservation { Position = -1, Duration = duration };
#if UNITY_WEBGL && !UNITY_EDITOR
                if (douyinAudio != null) return douyinAudio.Observe(now);
                result.Position = FeedVoice_Position();
                if (native)
                {
                    int status = FeedVoice_Status();
                    result.Confirmed = status == 1 && result.Position >= 0;
                    result.Paused = status == 2;
                    result.Buffering = status == 3;
                    result.Ended = status == 4;
                    result.Failed = status < 0;
                    result.Duration = FeedVoice_Duration();
                    return result;
                }
                result.Confirmed = result.Position >= 0;
                if (result.Position < 0 && !container && source.isPlaying && source.timeSamples > 0)
                { result.Position = (double)source.timeSamples / source.clip.frequency; result.Confirmed = true; }
#else
                result.Confirmed = source.isPlaying;
                if (source.isPlaying)
                    result.Position = source.clip.frequency > 0 ? (double)source.timeSamples / source.clip.frequency : source.time;
#endif
                if (source.isPlaying) stoppedAt = -1;
                else if (confirmed)
                {
                    if (stoppedAt < 0) stoppedAt = now;
                    result.Ended = now - stoppedAt >= .25;
                }
                if (source.clip != null && source.clip.length > 0) result.Duration = source.clip.length;
                confirmed |= result.Confirmed;
                return result;
            }
            private void CheckOwner()
            {
                if (disposed || owner != this) throw new ObjectDisposedException(nameof(UnityAudio));
                if (source == null) throw new InvalidOperationException("配音 AudioSource 已释放。");
            }
            public void Dispose()
            {
                if (disposed) return;
                try { Stop(); }
                finally
                {
                    disposed = true;
                    if (owner == this)
                    {
                        try
                        {
#if UNITY_WEBGL && !UNITY_EDITOR
                            if (douyinAudio != null) { douyinAudio.Dispose(); douyinAudio = null; }
                            else if (bridgeUsed) FeedVoice_Release();
#endif
                        }
                        finally
                        {
                            if (source != null) source.clip = null;
                            owner = null;
                        }
                    }
                }
            }
        }
    }
}