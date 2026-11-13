using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Douyin.Feed;
using Player = Douyin.Feed.FeedPlaybackController;

internal static class FeedPlaybackControllerTests
{
    private sealed class Audio : Player.IAudio
    {
        public string Key { get; set; } = "demo_01";
        public int Plays, Pauses, Stops, Disposals;
        public bool Muted;
        public Player.AudioObservation Reading = new Player.AudioObservation { Position = 0, Duration = 11 };
        public void Play(bool resume) { Plays++; Reading.Paused = false; }
        public void Pause() { Pauses++; Reading.Paused = true; Reading.Confirmed = false; }
        public void Stop() { Stops++; Reading.Confirmed = false; }
        public void SetMuted(bool value) { Muted = value; }
        public Player.AudioObservation Observe(double now) => Reading;
        public void Dispose() { Disposals++; }
    }
    private sealed class Host : Player.IHost
    {
        public bool Available { get; set; } = true;
        public bool IsCurrent = true;
        public bool AudioSuspended { get; set; }
        public bool Muted { get; set; }
        public bool PresentationReady = true, ThrowFallback, ThrowFlush, AutoReport = true, ReportSuccess = true;
        public string Mode;
        public int LoadFrames, Reports, Fallbacks, Loads, Releases, Enters, Exits, PauseReleases, DisposedIterators;
        public bool SubtitleVisible, Ducking, CardVisible, GamePaused, GameVisible = true;
        public string Subtitle;
        public Audio Audio;
        public Action Retry;
        public Player.Preparation Preparation;
        public Player.Media Submitted;
        public readonly List<Action<bool,string>> ReportCallbacks = new List<Action<bool,string>>();
        public bool Current(int version) => IsCurrent;
        public IEnumerator Prepare(Player.Preparation preparation)
        {
            Preparation = preparation; Loads++;
            try
            {
                if (Mode == "throw") throw new InvalidOperationException("测试初始化异常");
                if (Mode == "nested") { yield return Nested(); yield break; }
                if (Mode == "yield") { yield return new object(); yield break; }
                if (Mode == "fail") { preparation.Fail("内容不可用，请重试。"); yield break; }
                if (Mode == "forever") while (true) yield return null;
                for (int i=0;i<LoadFrames;i++) yield return null;
                if (Mode == "silent") { preparation.Complete(new Player.Media { Key = preparation.Key, Silent = true }); yield break; }
                Audio = new Audio { Key = Mode == "audio-key" ? "demo_02" : preparation.Key };
                var sheet = Sheet(preparation.Key);
                if (Mode == "cue-key") sheet.key = "demo_02";
                if (Mode == "cue-time") sheet.cues[1].start = 4;
                if (Mode == "cue-nan") sheet.duration = float.NaN;
                Submitted = new Player.Media { Key = preparation.Key, Sheet = sheet, Audio = Audio };
                preparation.Complete(Submitted);
            }
            finally { DisposedIterators++; }
        }
        private IEnumerator Nested() { yield return null; throw new InvalidOperationException("测试嵌套异常"); }
        public void PresentContent() { }
        public void PresentFallback(string message, Action retry) { Fallbacks++; Retry = retry; if(ThrowFallback)throw new Exception("没有错误页面"); }
        public bool FlushPresentation(bool fallback) { if(ThrowFlush)throw new Exception("界面异常");return PresentationReady; }
        public void ReleaseContent() { Releases++; }
        public void SetCardVisible(bool value) { CardVisible = value; }
        public void SetSubtitle(string text,bool value) { Subtitle = text; SubtitleVisible = value; }
        public void SetDucking(bool value) { Ducking = value; }
        public void PauseGame() { GamePaused = true; }
        public void ResumeCardAudio() { GamePaused = false; }
        public void EnterGame() { Enters++; }
        public void ExitGame() { Exits++; }
        public void SetFullGameVisible(bool value) { GameVisible = value; }
        public void ReleasePause() { PauseReleases++; GamePaused = false; }
        public void ReportSceneReady(int version,bool fallback,Action<bool,string> completed)
        { Reports++; ReportCallbacks.Add(completed);if(AutoReport)completed(ReportSuccess,ReportSuccess?null:"平台暂时不可用"); }
        public void Diagnostic(string value) { }
    }
    private sealed class Rig : IDisposable
    {
        public readonly Host Host = new Host();
        public readonly Player Player;
        public double Now;
        public Rig() { Player = new Player(Host);Player.PreparationTimeout=2;Player.ReportTimeout=1;Player.Begin(1,"demo_01",0); }
        public void Tick(double amount=.1) { Now+=amount;Player.Tick(Now); }
        public void Ready() { for(int i=0;i<20&&!Player.IsPrepared;i++)Tick();Assert(Player.IsPrepared,"页面应该准备完成"); }
        public void Playing(double position=1)
        { Ready();Player.Show();Host.Audio.Reading.Confirmed=true;Host.Audio.Reading.Position=position;Tick(); }
        public void Dispose() { Player.Dispose(); }
    }
    private static FeedCueSheet Sheet(string key) => new FeedCueSheet { key=key,duration=11,cues=new[]{
        new FeedCue{start=0,end=3,text="第一段"},new FeedCue{start=3,end=6,text="第二段"},
        new FeedCue{start=6,end=9,text="第三段"},new FeedCue{start=9,end=11,text="第四段"} } };
    private static void Assert(bool value,string message="状态不符合预期") { if(!value)throw new Exception(message); }
    private static readonly List<object> Results = new List<object>();
    private static int failures;
    private static void Test(string name,Action<Rig> action)
    {
        try { using(var rig=new Rig())action(rig);Results.Add(new{name,passed=true}); }
        catch(Exception error) { failures++;Results.Add(new{name,passed=false,error=error.Message});Console.WriteLine(name+": "+error.Message); }
    }
    public static int Main(string[] args)
    {
        Test("首画面在下一帧确认后上报",r=>{r.Tick();Assert(r.Player.State==Player.PlayerState.AwaitingPresentation&&r.Host.Reports==0);r.Tick();Assert(r.Host.Reports==1&&r.Player.ReportState==Player.SceneReportState.Succeeded);});
        Test("准备前显示请求保留到首画面完成",r=>{r.Player.Show();r.Ready();Assert(r.Player.VoiceStarted&&r.Host.Audio.Plays==1&&r.Player.IsVisible);});
        Test("准备前显示随后隐藏取消待播请求",r=>{r.Player.Show();r.Player.Hide();r.Ready();Assert(!r.Player.VoiceStarted&&r.Host.Audio.Plays==0&&!r.Player.IsVisible);});
        Test("新会话不会继承上一会话的显示请求",r=>{r.Player.Show();r.Player.Begin(2,"demo_01",r.Now);r.Ready();Assert(r.Host.Audio.Plays==0&&!r.Player.IsVisible);});
        Test("隐藏等待不推进字幕",r=>{r.Ready();r.Tick(40);Assert(r.Player.Position==0&&!r.Host.SubtitleVisible&&!r.Host.Ducking);});
        Test("上报成功不启动配音",r=>{r.Ready();Assert(r.Host.Audio.Plays==0&&!r.Player.IsVisible);});
        Test("重复显示仅发出一次播放",r=>{r.Ready();r.Player.Show();r.Player.Show();Assert(r.Host.Audio.Plays==1);});
        Test("起播确认前隐藏字幕",r=>{r.Ready();r.Player.Show();r.Tick();Assert(!r.Host.SubtitleVisible&&r.Player.Position==0&&!r.Host.Ducking);});
        Test("真实播放位置选择字幕",r=>{r.Playing(4);Assert(r.Host.Subtitle=="第二段"&&r.Host.SubtitleVisible&&r.Host.Ducking);});
        Test("实际音频位置可以纠正估算",r=>{r.Playing(5);r.Host.Audio.Reading.Position=2;r.Tick();Assert(r.Player.Position==2&&r.Host.Subtitle=="第一段");});
        Test("已确认零位置使用有效活动时间",r=>{r.Playing(0);r.Tick(.3);Assert(Math.Abs(r.Player.Position-.3)<.001);});
        Test("隐藏同时暂停音频字幕与背景音乐调整",r=>{r.Playing();r.Player.Hide();double old=r.Player.Position;r.Tick(10);Assert(r.Host.Audio.Pauses==1&&r.Player.Position==old&&!r.Host.SubtitleVisible&&!r.Host.Ducking);});
        Test("恢复使用同一音频实例",r=>{r.Playing(4);var audio=r.Host.Audio;r.Player.Hide();r.Tick(10);r.Player.Show();audio.Reading.Confirmed=true;audio.Reading.Position=4.1;r.Tick();Assert(r.Host.Audio==audio&&audio.Plays==2&&r.Player.Position>=4);});
        Test("恢复不补算隐藏时间",r=>{r.Playing(0);r.Tick(.2);double old=r.Player.Position;r.Player.Hide();r.Tick(20);r.Player.Show();r.Host.Audio.Reading.Confirmed=true;r.Tick();Assert(Math.Abs(r.Player.Position-old)<.001);});
        Test("缓冲冻结字幕",r=>{r.Playing(2);r.Host.Audio.Reading.Buffering=true;r.Tick(3);Assert(r.Player.Position==2&&r.Player.State==Player.PlayerState.Buffering&&!r.Host.SubtitleVisible&&!r.Host.Ducking);});
        Test("缓冲恢复重新计算有效时间",r=>{r.Playing(0);r.Host.Audio.Reading.Buffering=true;r.Tick(1);r.Tick(10);r.Host.Audio.Reading.Buffering=false;r.Tick();Assert(r.Player.Position==0);});
        Test("长时间缓冲进入可重试页面",r=>{r.Playing();r.Host.Audio.Reading.Buffering=true;r.Tick();r.Tick(31);r.Tick();Assert(r.Player.HasPreparationError&&r.Host.Fallbacks==1&&r.Host.Audio.Disposals==1);});
        Test("起播等待超时进入可重试页面",r=>{r.Ready();r.Player.Show();r.Tick(31);r.Tick();Assert(r.Player.HasPreparationError&&r.Host.Fallbacks==1&&!r.Host.SubtitleVisible);});
        Test("音频错误清理当前实例",r=>{r.Playing();r.Host.Audio.Reading.Failed=true;r.Tick();r.Tick();Assert(r.Host.Audio.Disposals==1&&r.Player.HasPreparationError&&!r.Host.Ducking);});
        Test("实际配音时长不匹配时停止",r=>{r.Playing();r.Host.Audio.Reading.Duration=20;r.Tick();Assert(r.Player.HasPreparationError&&r.Host.Audio.Disposals==1);});
        Test("自然结束隐藏字幕并恢复背景音乐",r=>{r.Playing(10);r.Host.Audio.Reading.Ended=true;r.Tick();Assert(r.Player.State==Player.PlayerState.Completed&&!r.Host.SubtitleVisible&&!r.Host.Ducking);});
        Test("已结束配音再次显示不重复播放",r=>{r.Playing();r.Host.Audio.Reading.Ended=true;r.Tick();r.Player.Hide();r.Player.Show();Assert(r.Host.Audio.Plays==1);});
        Test("静音不影响字幕时间",r=>{r.Host.Muted=true;r.Playing(4);Assert(r.Host.Audio.Muted&&r.Player.Position==4&&r.Host.SubtitleVisible);});
        Test("外部音频暂停冻结字幕",r=>{r.Playing(2);r.Host.AudioSuspended=true;r.Tick();r.Tick(20);Assert(r.Player.Position==2&&!r.Host.SubtitleVisible&&r.Host.Audio.Pauses==1);});
        Test("外部音频恢复等待同一播放器确认",r=>{r.Playing();r.Host.AudioSuspended=true;r.Tick();r.Host.AudioSuspended=false;r.Tick();Assert(r.Host.Audio.Plays==2&&!r.Host.SubtitleVisible);r.Host.Audio.Reading.Confirmed=true;r.Host.Audio.Reading.Position=1.1;r.Tick();Assert(r.Host.SubtitleVisible);});
        foreach(string mode in new[]{"throw","nested","fail","audio-key","cue-key","cue-time","cue-nan","yield"})
        { string captured=mode;Test("准备异常完成错误页面与上报 "+mode,r=>{r.Host.Mode=captured;r.Ready();Assert(r.Player.HasPreparationError&&r.Host.Fallbacks==1&&r.Host.Reports==1&&!r.Host.SubtitleVisible);}); }
        Test("加载超时取消嵌套任务并报告错误页面",r=>{r.Host.Mode="forever";r.Tick();r.Tick(3);r.Tick();Assert(r.Player.IsPrepared&&r.Host.Fallbacks==1&&r.Host.Reports==1&&r.Host.DisposedIterators==1);});
        Test("首页等待期间不暂停正常加载",r=>{r.Host.Available=false;r.Tick();Assert(!r.Host.GamePaused&&r.Host.Loads==0);});
        Test("首页等待超时显示现有错误页后上报",r=>{r.Host.Available=false;r.Tick(3);r.Tick();Assert(r.Player.IsPrepared&&r.Host.Reports==1&&r.Host.Fallbacks==1);});
        Test("场景准备不会等待渲染结束事件",r=>{r.Ready();Assert(r.Player.IsPrepared&&r.Host.Reports==1);});
        Test("未显示错误页面不报告场景就绪",r=>{r.Host.Mode="fail";r.Host.PresentationReady=false;r.Tick();r.Tick(3);Assert(r.Host.Reports==0&&r.Player.State==Player.PlayerState.Failed);});
        Test("错误页面无法创建时结束等待",r=>{r.Host.Mode="fail";r.Host.ThrowFallback=true;r.Tick();r.Tick(30);Assert(r.Player.State==Player.PlayerState.Failed&&r.Host.Fallbacks==1&&!r.Host.GamePaused);});
        Test("显示内容失败后显示错误页面",r=>{r.Host.ThrowFlush=true;r.Tick();r.Tick();r.Host.ThrowFlush=false;r.Tick();Assert(r.Host.Fallbacks==1&&r.Player.IsPrepared&&r.Host.Reports==1);});
        Test("重试后正常准备且不重复上报",r=>{r.Host.Mode="fail";r.Ready();r.Host.Mode=null;r.Host.Retry();r.Ready();Assert(!r.Player.HasPreparationError&&r.Host.Reports==1&&r.Host.Audio.Plays==0);});
        Test("可见错误页面重试恢复播放",r=>{r.Host.Mode="fail";r.Ready();r.Player.Show();r.Host.Mode=null;r.Host.Retry();r.Ready();Assert(r.Host.Audio.Plays==1&&r.Host.Reports==1);});
        Test("重试过程中隐藏不恢复配音",r=>{r.Host.Mode="fail";r.Ready();r.Player.Show();r.Host.Mode=null;r.Host.LoadFrames=2;r.Host.Retry();r.Player.Hide();r.Ready();Assert(r.Host.Audio.Plays==0&&!r.Player.IsVisible);});
        Test("准备取消后不接受旧完成结果",r=>{r.Host.Mode="forever";r.Tick();var old=r.Host.Preparation;r.Player.EndSession();var audio=new Audio();old.Complete(new Player.Media{Key="demo_01",Sheet=Sheet("demo_01"),Audio=audio});r.Tick();Assert(!r.Player.IsPrepared&&r.Host.Reports==0&&audio.Disposals==1&&r.Host.DisposedIterators==1);});
        Test("切换内容保留旧准备上下文的标识",r=>{r.Host.Mode="forever";r.Tick();var old=r.Host.Preparation;r.Player.Begin(2,"demo_02",r.Now);Assert(old.Key=="demo_01"&&old.Version==1&&!old.IsCurrent);});
        Test("旧上报回调不能影响新会话",r=>{r.Host.AutoReport=false;r.Ready();var old=r.Host.ReportCallbacks[0];r.Player.Begin(2,"demo_02",r.Now);r.Ready();old(true,null);Assert(r.Player.ReportState==Player.SceneReportState.Sending);});
        Test("上报回调不会触发播放",r=>{r.Host.AutoReport=false;r.Ready();r.Host.ReportCallbacks[0](true,null);Assert(r.Host.Audio.Plays==0&&!r.Player.IsVisible);});
        Test("正常上报成功只发送一次",r=>{r.Ready();for(int i=0;i<10;i++)r.Tick(2);Assert(r.Host.Reports==1);});
        Test("上报失败后有界重试并成功",r=>{r.Host.ReportSuccess=false;r.Ready();Assert(r.Player.ReportState==Player.SceneReportState.RetryWaiting);r.Host.ReportSuccess=true;r.Tick(2);Assert(r.Host.Reports==2&&r.Player.ReportState==Player.SceneReportState.Succeeded);});
        Test("上报无回调最多尝试三次",r=>{r.Host.AutoReport=false;r.Ready();for(int i=0;i<12;i++)r.Tick(2);Assert(r.Host.Reports==3&&r.Player.ReportState==Player.SceneReportState.Failed&&r.Player.HasPreparationError);});
        Test("超时旧回调不覆盖新上报结果",r=>{r.Host.AutoReport=false;r.Ready();var old=r.Host.ReportCallbacks[0];r.Tick(2);r.Tick(2);old(true,null);Assert(r.Host.Reports==2&&r.Player.ReportState==Player.SceneReportState.Sending);});
        Test("内容重试保留同会话正在等待的上报",r=>{r.Host.AutoReport=false;r.Host.Mode="fail";r.Ready();var callback=r.Host.ReportCallbacks[0];r.Host.Mode=null;r.Player.Retry();r.Ready();callback(true,null);Assert(r.Host.Reports==1&&r.Player.ReportState==Player.SceneReportState.Succeeded);});
        Test("上报最终失败后手动重试",r=>{r.Host.ReportSuccess=false;r.Ready();for(int i=0;i<4;i++)r.Tick(2);Assert(r.Player.ReportState==Player.SceneReportState.Failed);r.Host.ReportSuccess=true;r.Host.Retry();r.Ready();Assert(r.Player.ReportState==Player.SceneReportState.Succeeded&&!r.Player.HasPreparationError);});
        Test("提前进入等待内容完成且不启动卡片配音",r=>{r.Host.LoadFrames=2;r.Player.EnterGame();r.Ready();Assert(r.Host.Enters==1&&r.Host.Audio.Plays==0&&r.Player.State==Player.PlayerState.FullGame);});
        Test("提前进入后隐藏保持正式游戏暂停",r=>{r.Player.EnterGame();r.Player.SetFullGameVisible(false);r.Ready();Assert(r.Host.Enters==1&&!r.Host.GameVisible&&r.Host.Audio.Plays==0);});
        Test("重复进入仅交接一次",r=>{r.Playing();r.Player.EnterGame();r.Player.EnterGame();Assert(r.Host.Enters==1&&!r.Host.SubtitleVisible&&!r.Host.Ducking);});
        Test("退出完整游戏不重播已结束卡片配音",r=>{r.Playing();r.Player.EnterGame();r.Player.ExitGame();r.Player.Show();Assert(r.Host.Exits==1&&r.Host.Audio.Plays==1);});
        Test("完整游戏退出后可以再次进入",r=>{r.Ready();r.Player.EnterGame();r.Player.ExitGame();r.Player.EnterGame();Assert(r.Host.Enters==2);});
        Test("会话失效立即释放资源与暂停",r=>{r.Playing();r.Host.IsCurrent=false;r.Tick();Assert(!r.Player.Active&&r.Host.Audio.Disposals==1&&!r.Host.GamePaused&&!r.Host.SubtitleVisible);});
        Test("释放可重复调用",r=>{r.Playing();r.Player.Dispose();r.Player.Dispose();Assert(r.Host.Audio.Disposals==1&&r.Player.State==Player.PlayerState.Disposed);});
        Test("全部字幕边界来自同一配音位置",r=>{r.Playing(0);foreach(double time in new[]{0,3,6,9}){r.Host.Audio.Reading.Position=time;r.Tick();Assert(r.Host.SubtitleVisible);}r.Host.Audio.Reading.Position=11;r.Tick();Assert(!r.Host.SubtitleVisible);});
        Test("同会话多次重试释放每个配音实例",r=>{r.Playing();var first=r.Host.Audio;r.Player.Retry();r.Ready();var second=r.Host.Audio;r.Player.Retry();r.Ready();Assert(first.Disposals==1&&second.Disposals==1&&r.Host.Reports==1);});
        Test("后台恢复显示使用事件发生时的时间",r=>{r.Player.Dispose();double clock=0;var p=new Player(r.Host,()=>clock);p.Begin(1,"demo_01",0);p.Tick(.1);p.Tick(.2);clock=120;p.Show();p.Tick(120.1);Assert(p.State==Player.PlayerState.Starting&&!p.HasPreparationError&&r.Host.Audio.Plays==1);p.Dispose();});
        Test("意外暂停超过时限显示重试页面",r=>{r.Playing();r.Host.Audio.Reading.Paused=true;r.Tick();r.Tick(31);r.Tick();Assert(r.Player.HasPreparationError&&r.Host.Fallbacks==1);});
        Test("已结束配音保持完成状态",r=>{r.Playing();r.Host.Audio.Reading.Ended=true;r.Tick();r.Player.Hide();r.Player.Show();Assert(r.Player.State==Player.PlayerState.Completed);});
        Test("提交后外部修改字幕不影响当前配音",r=>{r.Playing(4);r.Host.Submitted.Key="demo_02";r.Host.Submitted.Sheet.key="demo_02";r.Host.Submitted.Sheet.cues[1].text="其他内容";r.Host.Submitted.Sheet.duration=100;r.Tick();Assert(r.Host.Subtitle=="第二段"&&!r.Player.HasPreparationError);});
        Test("错误媒体提交立即释放播放器",r=>{r.Host.Mode="cue-key";r.Ready();Assert(r.Host.Audio.Disposals==1);});
        Test("上报最后一次失败会取消正在执行的资源重试",r=>{r.Host.AutoReport=false;r.Ready();r.Tick(2);r.Tick(2);r.Tick(2);r.Tick(2);r.Host.Mode="forever";r.Player.Retry();r.Tick();r.Host.ReportCallbacks[2](false,"网络错误");r.Tick();Assert(r.Player.IsPrepared&&r.Player.ReportState==Player.SceneReportState.Failed&&r.Host.DisposedIterators==2);});
        Test("取消后迟到的错误不能改变新会话",r=>{r.Host.Mode="forever";r.Tick();var old=r.Host.Preparation;r.Host.Mode=null;r.Player.Begin(2,"demo_02",r.Now);old.Fail("旧错误");r.Ready();Assert(!r.Player.HasPreparationError&&r.Player.ContentKey=="demo_02");});
        Test("播放位置持续停止会结束等待并显示重试",r=>{r.Playing(4);r.Tick(31);r.Tick();Assert(r.Player.HasPreparationError&&r.Host.Fallbacks==1&&!r.Host.SubtitleVisible);});
        Test("播放确认丢失时隐藏字幕并冻结位置",r=>{r.Playing(4);r.Host.Audio.Reading.Confirmed=false;r.Tick();Assert(!r.Host.SubtitleVisible&&!r.Host.Ducking&&r.Player.Position==4);});
        Test("播放确认恢复不补算等待时间",r=>{r.Playing(0);r.Host.Audio.Reading.Confirmed=false;r.Tick();r.Tick(10);r.Host.Audio.Reading.Confirmed=true;r.Tick();Assert(r.Player.Position==0&&r.Host.SubtitleVisible);});
        Test("加载页面转入正式场景后自动重新准备内容",r=>{r.Host.Available=false;r.Tick(3);r.Tick();r.Player.Show();r.Host.Available=true;r.Host.PresentationReady=false;r.Tick();Assert(!r.Player.IsPrepared);r.Host.PresentationReady=true;r.Ready();Assert(r.Host.Reports==1&&r.Host.Audio.Plays==1);});
        Test("进入游戏后迟到的上报失败不打断正式游戏",r=>{r.Host.AutoReport=false;r.Ready();r.Player.EnterGame();for(int i=0;i<12;i++)r.Tick(2);Assert(r.Player.State==Player.PlayerState.FullGame&&r.Host.Enters==1&&r.Host.Fallbacks==0&&!r.Host.SubtitleVisible);});
        Test("加载页面期间提前进入等待真正的首页",r=>{r.Host.Available=false;r.Player.EnterGame();r.Player.SetFullGameVisible(false);r.Tick(3);r.Tick();Assert(r.Host.Enters==0&&r.Player.IsPrepared);r.Host.Available=true;r.Host.PresentationReady=false;r.Tick();r.Host.PresentationReady=true;r.Ready();Assert(r.Host.Enters==1&&!r.Host.GameVisible&&r.Host.Audio.Plays==0);});
        Test("错误内容在完整游戏入口重试后只交接一次",r=>{r.Host.Mode="fail";r.Ready();r.Player.EnterGame();Assert(r.Host.Enters==1);r.Host.Mode=null;r.Player.Retry();r.Ready();Assert(r.Host.Enters==2&&r.Host.Audio.Plays==0);});
        Test("无配音首页保持正常就绪与显示",r=>{r.Host.Mode="silent";r.Ready();r.Player.Show();r.Tick();Assert(r.Host.Reports==1&&r.Player.State==Player.PlayerState.Completed&&!r.Host.SubtitleVisible);});
        var report=new{finished=true,passed=failures==0,scope="Unified recommendation playback controller with deterministic resource, UI, audio and platform adapters",count=Results.Count,failures,checks=Results};
        string json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
        if(args.Length>0)File.WriteAllText(args[0],json);
        Console.WriteLine($"FeedPlaybackController: {Results.Count-failures}/{Results.Count}");
        return failures==0?0:1;
    }
}