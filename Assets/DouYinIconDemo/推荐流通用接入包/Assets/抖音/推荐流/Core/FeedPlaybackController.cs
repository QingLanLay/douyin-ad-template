using System;
using System.Collections;
using System.Collections.Generic;

namespace Douyin.Feed
{
    /// <summary>统一管理推荐流准备、首画面、配音字幕、平台上报与会话释放。</summary>
    public sealed partial class FeedPlaybackController : IDisposable
    {
        public enum PlayerState { Idle, WaitingForHost, Preparing, AwaitingPresentation, Hidden, Starting, Playing, Paused, Buffering, Completed, Fallback, FullGame, Failed, Disposed }
        public enum SceneReportState { None, Sending, RetryWaiting, Succeeded, Failed }

        /// <summary>项目提供资源、现有界面与正式游戏入口，播放时序由控制器管理。</summary>
        public interface IHost
        {
            bool Available { get; }
            bool Current(int version);
            bool AudioSuspended { get; }
            bool Muted { get; }
            IEnumerator Prepare(Preparation preparation);
            void PresentContent();
            void PresentFallback(string message, Action retry);
            bool FlushPresentation(bool fallback);
            void ReleaseContent();
            void SetCardVisible(bool visible);
            void SetSubtitle(string text, bool visible);
            void SetDucking(bool enabled);
            void PauseGame();
            void ResumeCardAudio();
            void EnterGame();
            void ExitGame();
            void SetFullGameVisible(bool visible);
            void ReleasePause();
            void ReportSceneReady(int version, bool fallback, Action<bool, string> completed);
            void Diagnostic(string message);
        }

        public interface IAudio : IDisposable
        {
            string Key { get; }
            void Play(bool resume);
            void Pause();
            void Stop();
            void SetMuted(bool muted);
            AudioObservation Observe(double now);
        }

        public struct AudioObservation
        {
            public bool Confirmed, Paused, Buffering, Ended, Failed;
            public double Position, Duration;
            public string Error;
        }

        public sealed class Media
        {
            public string Key;
            public bool Silent;
            public FeedCueSheet Sheet;
            public IAudio Audio;
        }

        /// <summary>一次准备操作的上下文；取消后所有完成结果自动失效。</summary>
        public sealed class Preparation
        {
            private readonly FeedPlaybackController owner;
            private readonly int generation;
            private readonly string contentKey;
            private readonly int launchVersion;
            internal Preparation(FeedPlaybackController owner, int generation) { this.owner = owner; this.generation = generation; contentKey = owner.key; launchVersion = owner.version; }
            public string Key => contentKey;
            public int Version => launchVersion;
            public bool IsCurrent => owner.Active && owner.operation == generation && owner.host.Current(owner.version);
            public void Complete(Media media)
            {
                if (!IsCurrent) { media?.Audio?.Dispose(); return; }
                try
                {
                    if (owner.media != null) throw new InvalidOperationException("准备结果只能提交一次。");
                    ValidateMedia(media, Key);
                    owner.media = Snapshot(media);
                }
                catch { media?.Audio?.Dispose(); throw; }
            }
            public void Fail(string message)
            {
                if (IsCurrent) owner.pendingError = string.IsNullOrWhiteSpace(message) ? "推荐流内容准备失败，请重试。" : message;
            }
        }

        private readonly IHost host;
        private readonly Func<double> clock;
        private readonly Stack<IEnumerator> routines = new Stack<IEnumerator>();
        private readonly FeedVoiceTimeline timeline = new FeedVoiceTimeline();
        private Media media;
        private string key, pendingError;
        private int version, operation, sessionEpoch, frame, presentationFrame, reportAttempt;
        private bool active, disposed, settled, fallback, visible, started, suspended, hostPaused;
        private bool fullGame, fullGameVisible, enterDispatched, hasSubmitted, everDisplayable, resumeAfterPreparation;
        private double now, prepareDeadline, reportDeadline, retryAt, unexpectedPauseAt = -1, progressAt, lastHead = -1;
        private int cueIndex = -1;

        public FeedPlaybackController(IHost host, Func<double> clock = null)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            this.clock = clock;
        }
        public double PreparationTimeout { get; set; } = 90;
        public double ReportTimeout { get; set; } = 8;
        public int MaximumReportAttempts { get; set; } = 3;
        public PlayerState State { get; private set; }
        public SceneReportState ReportState { get; private set; }
        public bool Active => active && !disposed;
        public bool IsPrepared => settled;
        public bool IsVisible => visible;
        public bool VoiceStarted => started;
        public bool HasPreparationError => Error != null;
        public string Error { get; private set; }
        public double Position => timeline.Position;
        public FeedVoicePhase VoicePhase => timeline.Phase;
        public int Version => version;
        public string ContentKey => key;

        public void Begin(int launchVersion, string contentKey, double time)
        {
            if (disposed) throw new ObjectDisposedException(nameof(FeedPlaybackController));
            EndSession();
            version = launchVersion;
            key = contentKey;
            now = time;
            active = true;
            BeginPreparation(false);
        }

        public void Retry()
        {
            SyncTime();
            if (!Active || !host.Current(version)) return;
            bool resume = everDisplayable && (visible || resumeAfterPreparation) && !fullGame;
            if (ReportState == SceneReportState.Failed)
            {
                ReportState = SceneReportState.None;
                reportAttempt = 0;
                hasSubmitted = false;
            }
            BeginPreparation(resume);
        }

        private void BeginPreparation(bool resume)
        {
            operation++;
            ReleaseContent();
            settled = fallback = visible = started = suspended = false;
            cueIndex = -1;
            unexpectedPauseAt = -1;
            pendingError = Error = null;
            resumeAfterPreparation = resume;
            if (fullGame) enterDispatched = false;
            timeline.Reset(0, now);
            prepareDeadline = now + Math.Max(1, PreparationTimeout);
            State = PlayerState.WaitingForHost;
        }

        /// <summary>每帧使用单调、不受 TimeScale 影响的时间调用。</summary>
        public void Tick(double time)
        {
            if (!Active || State == PlayerState.Failed) return;
            now = Math.Max(now, time);
            frame++;
            if (!host.Current(version)) { EndSession(); return; }
            try
            {
                if (!settled)
                {
                    if (now >= prepareDeadline && State != PlayerState.AwaitingPresentation)
                        FailPreparation("推荐流准备超时，请检查网络后重试。");
                    if (State == PlayerState.WaitingForHost && host.Available)
                    {
                        host.PauseGame();
                        hostPaused = true;
                        State = PlayerState.Preparing;
                        routines.Push(host.Prepare(new Preparation(this, operation)) ?? throw new InvalidOperationException("缺少推荐流准备任务。"));
                    }
                    if (State == PlayerState.Preparing) AdvancePreparation();
                    if (State == PlayerState.AwaitingPresentation && frame > presentationFrame)
                    {
                        if (host.FlushPresentation(fallback)) CompletePresentation();
                        else if (now >= prepareDeadline) throw new InvalidOperationException("推荐流界面尚未准备完成，请重试。");
                    }
                }
                if (!Active) return;
                if (settled && fallback && ReportState != SceneReportState.Failed && host.Available && !host.FlushPresentation(true)) Retry();
                AdvanceReporting();
                if (settled && fullGame && !enterDispatched && host.Available) DispatchEntry();
                if (settled && visible && !fallback && !fullGame) ObserveAudio();
            }
            catch (Exception exception) { FailPreparation("推荐流内容暂时无法显示，请重试。", exception); }
        }

        private void AdvancePreparation()
        {
            int generation = operation;
            for (int work = 0; work < 128 && routines.Count > 0; work++)
            {
                var routine = routines.Peek();
                bool moved = routine.MoveNext();
                if (!Active || operation != generation) return;
                if (pendingError != null) { FailPreparation(pendingError); return; }
                if (!moved)
                {
                    routines.Pop();
                    (routine as IDisposable)?.Dispose();
                    continue;
                }
                object yielded = routine.Current;
                if (yielded is IEnumerator nested) { routines.Push(nested); continue; }
                if (yielded != null) throw new InvalidOperationException("准备任务应使用逐帧等待或嵌套 IEnumerator。");
                return;
            }
            if (routines.Count != 0) return;
            ValidateMedia(media, key);
            timeline.Reset(media.Silent ? 0 : media.Sheet.duration, now);
            host.PresentContent();
            AwaitPresentation(false);
        }

        private void AwaitPresentation(bool isFallback)
        {
            fallback = isFallback;
            State = PlayerState.AwaitingPresentation;
            presentationFrame = frame;
            prepareDeadline = now + Math.Max(1, PreparationTimeout);
        }

        private void CompletePresentation()
        {
            settled = everDisplayable = true;
            State = fallback ? PlayerState.Fallback : PlayerState.Hidden;
            host.Diagnostic(fallback ? "FALLBACK_RENDERED" : "GAMEPLAY_RENDERED");
            int generation = operation;
            if (!hasSubmitted) SendSceneReport();
            if (!Active || generation != operation || !settled) return;
            if (fullGame) DispatchEntry();
            else if (resumeAfterPreparation) Show();
            resumeAfterPreparation = false;
        }

        private void FailPreparation(string message, Exception exception = null)
        {
            if (!Active) return;
            if (fallback && State == PlayerState.AwaitingPresentation)
            {
                Error = message;
                State = PlayerState.Failed;
                Safe(host.ReleasePause);
                hostPaused = false;
                Safe(() => host.Diagnostic(message + (exception == null ? "" : " " + exception.Message)));
                return;
            }
            resumeAfterPreparation |= visible;
            Error = message;
            operation++;
            ReleaseContent();
            settled = visible = started = suspended = false;
            timeline.Fail();
            Safe(() => host.Diagnostic(message + (exception == null ? "" : " " + exception.Message)));
            try
            {
                host.PresentFallback(message, Retry);
                AwaitPresentation(true);
            }
            catch (Exception failure)
            {
                State = PlayerState.Failed;
                Safe(() => host.ReleasePause());
                hostPaused = false;
                Safe(() => host.Diagnostic("推荐流错误页面无法显示：" + failure.Message));
            }
        }

        private void SendSceneReport()
        {
            if (!settled || !Active || ReportState == SceneReportState.Succeeded) return;
            hasSubmitted = true;
            ReportState = SceneReportState.Sending;
            int epoch = sessionEpoch, attempt = ++reportAttempt;
            reportDeadline = now + Math.Max(1, ReportTimeout);
            try
            {
                host.ReportSceneReady(version, fallback, (success, message) =>
                {
                    if (!Active || sessionEpoch != epoch || reportAttempt != attempt || ReportState != SceneReportState.Sending) return;
                    if (success) { ReportState = SceneReportState.Succeeded; return; }
                    ReportFailed(message);
                });
            }
            catch (Exception exception) { ReportFailed(exception.Message); }
        }

        private void AdvanceReporting()
        {
            if (ReportState == SceneReportState.Sending && now >= reportDeadline) ReportFailed("场景就绪上报等待超时。");
            if (settled && ReportState == SceneReportState.RetryWaiting && now >= retryAt) SendSceneReport();
        }

        private void ReportFailed(string message)
        {
            if (reportAttempt < Math.Max(1, MaximumReportAttempts))
            {
                ReportState = SceneReportState.RetryWaiting;
                retryAt = now + reportAttempt;
                Safe(() => host.Diagnostic("场景就绪上报将重试：" + message));
                return;
            }
            ReportState = SceneReportState.Failed;
            if (fullGame && enterDispatched) { Safe(() => host.Diagnostic("场景就绪上报失败：" + message)); return; }
            FailPreparation("场景就绪上报失败，请检查网络后重试。");
            Safe(() => host.Diagnostic("场景就绪上报失败：" + message));
        }

        public void Show()
        {
            SyncTime();
            if (!Active || fullGame || ReportState == SceneReportState.Failed) return;
            if (!settled || !hasSubmitted) { resumeAfterPreparation = true; return; }
            if (visible) return;
            visible = true;
            if (fallback) { State = PlayerState.Fallback; return; }
            try
            {
                host.ResumeCardAudio();
                host.SetCardVisible(true);
                if (media.Silent || timeline.Phase == FeedVoicePhase.Completed) { State = PlayerState.Completed; return; }
                if (timeline.HasFailed) return;
                timeline.Show(now);
                progressAt = now; lastHead = -1; unexpectedPauseAt = -1;
                media.Audio.SetMuted(host.Muted);
                media.Audio.Play(started);
                started = true;
                suspended = false;
                State = PlayerState.Starting;
                host.Diagnostic("VOICE_PLAY");
            }
            catch (Exception exception) { FailPreparation("配音播放器无法启动，请重试。", exception); }
        }

        public void Hide()
        {
            SyncTime();
            if (!Active) return;
            visible = resumeAfterPreparation = false;
            if (fullGame) { SetFullGameVisible(false); return; }
            Safe(() => { if (started && !timeline.HasFailed && timeline.Phase != FeedVoicePhase.Completed) media?.Audio?.Pause(); });
            timeline.Hide(now);
            Safe(() => host.SetCardVisible(false));
            Safe(() => host.SetSubtitle(null, false));
            Safe(() => host.SetDucking(false));
            if (host.Available) { Safe(host.PauseGame); hostPaused = true; }
            if (settled && !fallback) State = PlayerState.Paused;
        }

        private void ObserveAudio()
        {
            if (media == null || media.Silent || !started || timeline.Phase == FeedVoicePhase.Completed || timeline.HasFailed) return;
            media.Audio.SetMuted(host.Muted);
            if (host.AudioSuspended)
            {
                if (!suspended) { suspended = true; media.Audio.Pause(); timeline.Hide(now); }
                host.SetSubtitle(null, false);
                host.SetDucking(false);
                State = PlayerState.Paused;
                return;
            }
            if (suspended) { suspended = false; progressAt = now; timeline.Show(now); media.Audio.Play(true); }
            AudioObservation observation = media.Audio.Observe(now);
            if (observation.Paused)
            {
                progressAt = now;
                if (unexpectedPauseAt < 0) unexpectedPauseAt = now;
                if (now - unexpectedPauseAt >= 30) { FailPreparation("配音暂停等待超时，请重试。"); return; }
                timeline.Hide(now);
                host.SetSubtitle(null, false);
                host.SetDucking(false);
                State = PlayerState.Paused;
                return;
            }
            unexpectedPauseAt = -1;
            if (observation.Confirmed && !timeline.Visible) timeline.Show(now);
            if (observation.Duration > 0 && Math.Abs(observation.Duration - media.Sheet.duration) > Math.Max(.25, media.Sheet.duration * .02))
                observation.Failed = true;
            if (observation.Buffering) progressAt = now;
            if (observation.Confirmed && observation.Position > 0 && Math.Abs(observation.Position - lastHead) > .01)
            { lastHead = observation.Position; progressAt = now; }
            if (observation.Confirmed && now - progressAt >= 30) observation.Failed = true;
            timeline.Observe(now, observation.Confirmed, observation.Position, observation.Duration,
                observation.Buffering, observation.Ended, observation.Failed);
            if (timeline.HasFailed) { FailPreparation(string.IsNullOrEmpty(observation.Error) ? "配音播放失败或等待超时，请重试。" : observation.Error); return; }
            State = timeline.Phase == FeedVoicePhase.Playing ? PlayerState.Playing :
                timeline.Phase == FeedVoicePhase.Buffering ? PlayerState.Buffering :
                timeline.Phase == FeedVoicePhase.Completed ? PlayerState.Completed : PlayerState.Starting;
            host.SetDucking(timeline.Phase == FeedVoicePhase.Playing);
            int next = timeline.CueIndex(media.Sheet);
            if (next != cueIndex)
            {
                cueIndex = next;
                if (next >= 0) host.Diagnostic(next == 0 ? "FIRST_CUE_SHOW" : "CUE " + next);
            }
            host.SetSubtitle(next < 0 ? null : media.Sheet.cues[next].text, next >= 0);
        }

        public void EnterGame()
        {
            SyncTime();
            if (!Active || fullGame) return;
            fullGame = fullGameVisible = true;
            visible = resumeAfterPreparation = false;
            timeline.Stop();
            Safe(() => media?.Audio?.Stop());
            Safe(() => host.SetSubtitle(null, false));
            Safe(() => host.SetDucking(false));
            Safe(() => host.SetCardVisible(false));
            if (settled) DispatchEntry();
        }
        private void DispatchEntry()
        {
            if (enterDispatched || !host.Available) return;
            enterDispatched = true;
            timeline.Stop();
            media?.Audio?.Stop();
            host.SetSubtitle(null, false);
            host.SetDucking(false);
            State = PlayerState.FullGame;
            host.EnterGame();
            if (!fullGameVisible) host.SetFullGameVisible(false);
        }
        public void ExitGame()
        {
            SyncTime();
            if (!Active || !fullGame) return;
            fullGame = fullGameVisible = enterDispatched = visible = resumeAfterPreparation = false;
            Safe(host.ExitGame);
            if (settled) State = fallback ? PlayerState.Fallback : PlayerState.Hidden;
        }
        public void SetFullGameVisible(bool isVisible)
        {
            SyncTime();
            if (!Active || !fullGame) return;
            fullGameVisible = isVisible;
            if (enterDispatched) Safe(() => host.SetFullGameVisible(isVisible));
        }

        private void ReleaseContent()
        {
            while (routines.Count > 0) { var routine = routines.Pop(); Safe(() => (routine as IDisposable)?.Dispose()); }
            var old = media;
            media = null;
            Safe(() => old?.Audio?.Stop());
            Safe(() => old?.Audio?.Dispose());
            Safe(() => host.SetSubtitle(null, false));
            Safe(() => host.SetDucking(false));
            Safe(host.ReleaseContent);
        }
        public void EndSession()
        {
            active = false;
            sessionEpoch++;
            operation++;
            ReleaseContent();
            if (hostPaused) Safe(host.ReleasePause);
            hostPaused = false;
            settled = fallback = visible = started = suspended = fullGame = fullGameVisible = enterDispatched = false;
            hasSubmitted = everDisplayable = resumeAfterPreparation = false;
            reportAttempt = 0;
            pendingError = Error = null;
            timeline.Reset(0, now);
            ReportState = SceneReportState.None;
            State = disposed ? PlayerState.Disposed : PlayerState.Idle;
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            EndSession();
        }
        private void SyncTime() { if (clock != null) now = Math.Max(now, clock()); }
        private void Safe(Action action)
        {
            try { action(); }
            catch (Exception exception) { try { host.Diagnostic(exception.Message); } catch { } }
        }
        private static Media Snapshot(Media source)
        {
            if (source.Silent) return new Media { Key = source.Key, Silent = true };
            var sheet = source.Sheet;
            var cues = new FeedCue[sheet.cues.Length];
            for (int i = 0; i < cues.Length; i++)
                cues[i] = new FeedCue { start = sheet.cues[i].start, end = sheet.cues[i].end, text = sheet.cues[i].text };
            return new Media
            {
                Key = source.Key,
                Audio = source.Audio,
                Sheet = new FeedCueSheet { key = sheet.key, duration = sheet.duration, samples = sheet.samples, sampleRate = sheet.sampleRate, cues = cues }
            };
        }
        private static void ValidateMedia(Media media, string expectedKey)
        {
            if (media != null && media.Silent)
            {
                if (!string.IsNullOrWhiteSpace(expectedKey) && media.Key == expectedKey && media.Audio == null && media.Sheet == null) return;
                throw new InvalidOperationException("无配音页面不能绑定音频或字幕资源。");
            }
            if (media == null || media.Audio == null || media.Sheet == null || string.IsNullOrWhiteSpace(expectedKey) ||
                !string.Equals(media.Key, expectedKey, StringComparison.Ordinal) ||
                !string.Equals(media.Audio.Key, expectedKey, StringComparison.Ordinal) ||
                !string.Equals(media.Sheet.key, expectedKey, StringComparison.Ordinal))
                throw new InvalidOperationException("当前入口的配音与字幕标识不匹配。");
            var sheet = media.Sheet;
            if (float.IsNaN(sheet.duration) || float.IsInfinity(sheet.duration) || sheet.duration <= 0 || sheet.cues == null || sheet.cues.Length == 0)
                throw new InvalidOperationException("配音字幕时长或分段无效。");
            double last = 0;
            foreach (var cue in sheet.cues)
            {
                if (cue == null || string.IsNullOrWhiteSpace(cue.text) || float.IsNaN(cue.start) || float.IsNaN(cue.end) ||
                    float.IsInfinity(cue.start) || float.IsInfinity(cue.end) || cue.end <= cue.start || Math.Abs(cue.start - last) > .02)
                    throw new InvalidOperationException("配音字幕分段无效。");
                last = cue.end;
            }
            if (Math.Abs(last - sheet.duration) > .02) throw new InvalidOperationException("字幕结束时间与配音总时长不匹配。");
        }
    }
}