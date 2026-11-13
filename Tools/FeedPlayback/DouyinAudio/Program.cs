using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Douyin.Feed;
using TTSDK;

static class DouyinAudioTests
{
    static readonly List<object> checks=new();
    static readonly byte[] Bytes={73,68,51,4,0,0,0,0};
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Test(string name,Action run){run();checks.Add(new{name,passed=true});}
    static FeedDouyinAudio Create(){TT.Files=new();TTAudioManager.Supported=true;return new(Bytes,12);}
    static TTAudioManager.InnerAudioContext Audio=>TTAudioManager.Created[^1];
    static void Ready(FeedDouyinAudio a){Audio.AudioId=7;a.Play(false);Audio.onPlay();a.Observe(0);Audio.Reply(0);}
    static void Main(string[] args)
    {
        Test("SDK file and audio creation use the same local path",()=>{using var a=Create();Require(TTAudioManager.Options.src.StartsWith("scfile://user/")&&TT.Files.Data.ContainsKey(TTAudioManager.Options.src),"wrong file context");Require(!TTAudioManager.Options.autoPlay&&!TTAudioManager.Options.needDownload&&Audio.Plays==0,"hidden audio started");Audio.AudioId=7;});
        Test("IDE audio uses the desktop file protocol with an iOS simulated device",()=>{TT.Info=new(){platform="devtools",system="iOS 15"};using var a=Create();Require(TTAudioManager.Options.src.StartsWith("ttfile://user/")&&TT.Files.Data.ContainsKey(TTAudioManager.Options.src),"desktop native path differs from file path");Audio.AudioId=7;TT.Info=new();});
        Test("wrapped native property replies preserve current position",()=>{UNBridge.Wrapped=true;using var a=Create();Ready(a);a.Observe(.2);Audio.Reply(2.5);Require(a.Observe(.3).Position==2.5,"wrapped position lost");UNBridge.Wrapped=false;});
        Test("pause followed by ended reports completion without pause",()=>{using var a=Create();Ready(a);Audio.onPause();Audio.onEnded();var state=a.Observe(13);Require(state.Ended&&!state.Paused,"completion hidden by pause");});
        Test("OnShow before asynchronous creation is retained",()=>{using var a=Create();a.Play(false);a.Observe(1);Require(Audio.Plays==0,"played before audio ID");Audio.AudioId=8;a.Observe(2);Require(Audio.Plays==1,"pending show lost");a.Observe(3);Require(Audio.Plays==1,"duplicate play");});
        Test("OnHide cancels an early playback request",()=>{using var a=Create();a.Play(false);a.Pause();Audio.AudioId=8;a.Observe(3);Audio.onCanPlay();Require(Audio.Plays==0,"hidden audio played");a.Play(true);Require(Audio.Plays==1,"resume lost");});
        Test("native can-play retries an unconfirmed early play once",()=>{using var a=Create();Audio.AudioId=7;a.Play(false);Audio.onCanPlay();Audio.onCanPlay();Require(Audio.Plays==2,"ready event did not retry once");Audio.onPlay();Audio.onCanPlay();Require(Audio.Plays==2,"confirmed playback restarted");});
        Test("subtitles wait for playback confirmation and a position result",()=>{using var a=Create();Audio.AudioId=7;a.Play(false);Require(!a.Observe(0).Confirmed,"unconfirmed audio advanced");Audio.onPlay();Require(!a.Observe(.05).Confirmed,"missing position accepted");Audio.Reply(0);Require(a.Observe(.1).Confirmed,"confirmed zero position rejected");});
        Test("delayed native positions return actual playback time",()=>{using var a=Create();Ready(a);a.Observe(.2);Audio.Reply(4.5);Require(a.Observe(.3).Position==4.5,"incorrect playback position");});
        Test("duplicate OnShow preserves the current playback",()=>{using var a=Create();Ready(a);a.Play(false);a.Play(true);Require(Audio.Plays==1,"duplicate OnShow restarted");});
        Test("OnHide freezes position and resumes the same native instance",()=>{using var a=Create();Ready(a);a.Observe(.2);Audio.Reply(4);a.Pause();Require(!a.Observe(40).Confirmed&&Audio.Pauses==1,"hide did not pause");a.Play(true);Audio.onPlay();a.Observe(41);Audio.Reply(4);Require(a.Observe(41.1).Position==4&&Audio.Plays==2,"resume changed position");});
        Test("position callbacks from an earlier visibility interval are ignored",()=>{using var a=Create();Ready(a);a.Observe(.2);var old=Audio.PositionQueries.Dequeue();var oldPause=Audio.PauseQueries.Dequeue();a.Pause();a.Play(true);a.Observe(1);old(10);oldPause(true);Require(a.Observe(1.05).Position==0,"old request changed state");Audio.onPlay();Audio.Reply(1);Require(a.Observe(1.2).Confirmed,"current callback rejected");});
        Test("cancelled asynchronous creation is eventually destroyed",()=>{var a=Create();var native=Audio;a.Dispose();Require(native.Destroys==0,"invalid ID destroyed");native.AudioId=4;FeedDouyinAudio.CleanupRetired();Require(native.Destroys==1&&TT.Files.Data.Count==0,"cancelled resource leaked");});
        Test("callbacks after disposal do not restore playback",()=>{var a=Create();Ready(a);a.Observe(.2);var callback=Audio.PositionQueries.Dequeue();var play=Audio.onPlay;a.Dispose();callback(10);play();Require(Audio.Plays==1&&Audio.Destroys==1,"disposed audio restored");});
        Test("buffering freezes and actual progress resumes playback",()=>{using var a=Create();Ready(a);Audio.onWaiting();Require(a.Observe(.2).Buffering,"buffering missing");Audio.Reply(0);Require(a.Observe(.4).Buffering,"stationary callback resumed");Audio.Reply(.3);Require(a.Observe(.5).Confirmed,"progress did not resume");});
        Test("native audio errors remain available to the error view",()=>{using var a=Create();Ready(a);Audio.onError("10003: file error");var s=a.Observe(1);Require(s.Failed&&s.Error.Contains("10003")&&!s.Confirmed,"native error hidden");Audio.onPlay();Require(a.Observe(2).Failed,"late event cleared failure");});
        Test("completion prevents subsequent playback",()=>{using var a=Create();Ready(a);Audio.onEnded();a.Play(true);Require(a.Observe(2).Ended&&Audio.Plays==1,"completed audio restarted");});
        Test("muting does not pause the subtitle timeline",()=>{using var a=Create();Ready(a);a.SetVolume(0);Require(Audio.Volume==0&&a.Observe(.1).Confirmed&&Audio.Pauses==0,"mute paused audio");});
        Test("native duration is read in seconds",()=>{using var a=Create();Ready(a);Audio.Duration=11.92;Require(a.Observe(.1).Duration==11.92,"native duration ignored");});
        Test("unknown native duration preserves the cue duration",()=>{using var a=Create();Ready(a);Audio.Duration=double.NaN;Require(a.Observe(.1).Duration==12,"invalid duration propagated");});
        Test("failed file writes do not create an audio instance",()=>{TT.Files=new(){Error="write denied"};var before=TTAudioManager.Created.Count;try{new FeedDouyinAudio(Bytes,12);throw new Exception("failure accepted");}catch(InvalidOperationException){Require(TTAudioManager.Created.Count==before,"created audio without file");}});
        Test("UnityAudio selects SDK audio on an iOS host",()=>{TT.Files=new();TT.Info=new();using var voice=new FeedPlaybackController.UnityAudio(new UnityEngine.AudioSource(),"acq_01",true);voice.BindEncoded(Bytes,12);Audio.AudioId=7;voice.Play(false);Audio.onPlay();voice.Observe(0);Audio.Reply(0);Require(voice.Observe(.1).Confirmed,"UnityAudio did not use the iOS SDK adapter");voice.SetMuted(true);Require(Audio.Volume==0,"UnityAudio mute not forwarded");voice.Pause();Require(Audio.Pauses==1,"UnityAudio hide not forwarded");});
        Test("unsupported native service is rejected by the SDK adapter",()=>{TTAudioManager.Supported=false;try{new FeedDouyinAudio(Bytes,12);throw new Exception("unsupported audio accepted");}catch(InvalidOperationException){}finally{TTAudioManager.Supported=true;}});
        Test("controller starts iOS subtitles after native confirmation",()=>{using var r=new Rig();r.Player.Show();r.Prepare();Audio.AudioId=8;r.Step(.1);Audio.onPlay();r.Step(.2);Audio.Reply(0);r.Step(.3);Require(r.Host.Subtitle=="first"&&r.Host.Visible,"first subtitle missing");});
        Test("hidden native preparation does not consume the narration",()=>{using var r=new Rig();r.Prepare();Audio.AudioId=8;r.Step(60);Require(Audio.Plays==0&&!r.Host.Visible,"hidden preparation played");r.Player.Show();Audio.onPlay();r.Step(60.1);Audio.Reply(0);r.Step(60.2);Require(r.Host.Subtitle=="first"&&r.Host.Visible,"hidden waiting consumed first cue");});
        Test("controller pauses Douyin SDK audio and resumes its subtitle position",()=>{using var r=new Rig();r.Player.Show();r.Prepare();Audio.AudioId=8;r.Step(.1);Audio.onPlay();r.Step(.2);Audio.Reply(4);r.Step(.3);r.Player.Hide();Require(!r.Host.Visible&&Audio.Pauses==1,"controller hide failed");r.Step(65);r.Player.Show();Audio.onPlay();r.Step(65.1);Audio.Reply(4);r.Step(65.2);Require(r.Host.Subtitle=="second"&&r.Host.Visible,"resume did not retain current cue");});
        Test("controller completes when native pause precedes ended",()=>{using var r=new Rig();r.Player.Show();r.Prepare();Audio.AudioId=8;r.Step(.1);Audio.onPlay();r.Step(.2);Audio.Reply(11.9);r.Step(.3);Audio.onPause();Audio.onEnded();r.Step(12);r.Step(45);Require(r.Host.Fallback==null&&!r.Host.Visible,"ended audio became a paused timeout");});
        Test("native property failures preserve the service message",()=>{using var a=Create();Audio.AudioId=7;a.Play(false);Audio.onPlay();UNBridge.Failure="property unavailable";var result=a.Observe(0);UNBridge.Failure=null;Require(result.Failed&&result.Error.Contains("property unavailable"),"property failure lost");});
        Test("native property timeout is reported",()=>{using var a=Create();Audio.AudioId=7;a.Play(false);Audio.onPlay();UNBridge.Timeout=true;var result=a.Observe(0);UNBridge.Timeout=false;Require(result.Failed&&result.Error.Contains("超时"),"property timeout lost");});
        Test("controller reports native iOS errors with the native message",()=>{using var r=new Rig();r.Player.Show();r.Prepare();Audio.AudioId=8;r.Step(.1);Audio.onError("10003 unsupported file");r.Step(.2);Require(r.Host.Fallback.Contains("10003")&&!r.Host.Visible,"controller lost native failure");});
        Test("Android container uses the Douyin SDK player",()=>
        {
            TT.Info=new(){platform="android",system="Android 15"};
            using var voice=new FeedPlaybackController.UnityAudio(new UnityEngine.AudioSource(),"android",true);
            voice.BindEncoded(Bytes,12);Audio.AudioId=7;voice.Play(false);
            Require(Audio.Plays==1,"Android SDK play missing");TT.Info=new();
        });
        Test("local AudioClip binds readable PCM to the Douyin player",()=>
        {
            using var voice=new FeedPlaybackController.UnityAudio(new UnityEngine.AudioSource(),"local",true);
            voice.Bind(new UnityEngine.AudioClip(),12);Audio.AudioId=7;
            var data=TT.Files.Data[TTAudioManager.Options.src];
            Require(data.Length==108&&data[0]==82&&data[8]==87,"PCM WAV data missing");
            Require(Audio.Plays==0,"local preparation played while hidden");
        });
        Test("resume requires a new position response before subtitle confirmation",()=>
        {
            using var a=Create();Ready(a);a.Observe(.2);Audio.Reply(4);
            a.Pause();a.Play(true);Audio.onPlay();
            Require(!a.Observe(1).Confirmed,"cached position confirmed resume");
            Audio.Reply(4);Require(a.Observe(1.05).Confirmed,"fresh resume position rejected");
        });
        Test("playback callbacks from an earlier show are ignored",()=>
        {
            using var a=Create();Ready(a);var oldPlay=Audio.onPlay;var oldPause=Audio.onPause;
            a.Pause();a.Play(true);a.Observe(1);Audio.Reply(0);
            oldPlay();Require(!a.Observe(1.01).Confirmed,"old play event confirmed resume");
            Audio.onPlay();oldPause();Require(a.Observe(1.02).Confirmed,"old pause event changed current play");
        });
        Test("malformed native properties report an error without waiting indefinitely",()=>
        {
            using var a=Create();Audio.AudioId=7;a.Play(false);UNBridge.Malformed=true;
            var result=a.Observe(0);UNBridge.Malformed=false;
            Require(result.Failed&&result.Error.Contains("无效"),"invalid property accepted");
        });
        Test("wrong audio identity in a property response is rejected",()=>
        {
            using var a=Create();Audio.AudioId=7;a.Play(false);UNBridge.WrongId=true;
            a.Observe(0);Audio.Reply(1);UNBridge.WrongId=false;
            Require(a.Observe(.01).Failed,"foreign audio property accepted");
        });
        Test("native audio exceptions stop narration and retain the service message",()=>
        {
            using var a=Create();Ready(a);Audio.onError("10003 unsupported file");
            Require(a.Observe(.2).Failed&&Audio.Pauses==1,"failed native audio continued");
        });
        Test("native errors after a pause immediately reach the controller",()=>
        {
            using var a=Create();Ready(a);Audio.onPause();Audio.onError("10004 read failed");
            var state=a.Observe(.2);
            Require(state.Failed&&!state.Paused&&state.Error.Contains("10004"),"pause concealed native failure");
        });
        File.WriteAllText(args[0],JsonSerializer.Serialize(new{passed=true,count=checks.Count,scope="Douyin SDK audio adapter with asynchronous SDK responses",checks},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"Douyin SDK audio: {checks.Count}/{checks.Count}");
    }
    sealed class Rig : IDisposable
    {
        public readonly Host Host=new();public readonly FeedPlaybackController Player;
        public Rig(){TT.Files=new();TT.Info=new();TTAudioManager.Supported=true;Player=new(Host);Player.Begin(1,"acq_01",0);}
        public void Step(double time)=>Player.Tick(time);
        public void Prepare(){Step(.01);Step(.02);}
        public void Dispose()=>Player.Dispose();
    }
    sealed class Host : FeedPlaybackController.IHost
    {
        public bool Available=>true;public bool AudioSuspended=>false;public bool Muted=>false;
        public bool Visible;public string Subtitle,Fallback;
        FeedPlaybackController.UnityAudio voice;
        public bool Current(int version)=>true;
        public System.Collections.IEnumerator Prepare(FeedPlaybackController.Preparation p)
        {
            voice=new(new UnityEngine.AudioSource(),p.Key,true);voice.BindEncoded(Bytes,12);
            p.Complete(new FeedPlaybackController.Media{Key=p.Key,Audio=voice,Sheet=new FeedCueSheet{key=p.Key,duration=12,cues=new[]{new FeedCue{start=0,end=3,text="first"},new FeedCue{start=3,end=12,text="second"}}}});
            yield break;
        }
        public void PresentContent(){}public void PresentFallback(string text,Action retry){Fallback=text;}
        public bool FlushPresentation(bool fallback)=>true;public void ReleaseContent(){voice?.Dispose();voice=null;}
        public void SetSubtitle(string text,bool visible){Subtitle=text;Visible=visible;}
        public void SetCardVisible(bool value){}public void SetDucking(bool value){}public void PauseGame(){}public void ResumeCardAudio(){}public void EnterGame(){}public void ExitGame(){}public void SetFullGameVisible(bool value){}public void ReleasePause(){}
        public void ReportSceneReady(int version,bool fallback,Action<bool,string> complete)=>complete(true,"ok");
        public void Diagnostic(string message){}
    }

}
