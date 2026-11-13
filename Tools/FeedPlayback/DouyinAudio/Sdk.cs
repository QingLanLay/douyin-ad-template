using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class MonoBehaviour {}
    public class AudioClip
    {
        public int frequency=32000, samples=32, channels=1;
        public float length=12;
        public bool Readable=true;
        public bool GetData(float[] samples,int offset)=>Readable;
    }
    public static class Mathf { public static float Clamp(float value,float min,float max)=>Math.Clamp(value,min,max); }
    public static class AudioListener { public static float volume=1; }
    public class AudioSource
    {
        public bool playOnAwake,loop,mute,isPlaying;public float pitch=1,volume=1,time;public int timeSamples;public AudioClip clip;
        public void Play(){isPlaying=true;}public void UnPause(){isPlaying=true;}public void Pause(){isPlaying=false;}public void Stop(){isPlaying=false;}
    }
}
namespace TTSDK
{
    public abstract class TTFileSystemManager
    {
        public const string USER_DATA_PATH = "scfile://user";
        public abstract string WriteFileSync(string path, byte[] data);
        public abstract string UnlinkSync(string path);
    }
    public sealed class TestFiles : TTFileSystemManager
    {
        public readonly Dictionary<string,byte[]> Data = new();
        public string Error;
        public override string WriteFileSync(string path, byte[] data) { if(Error != null)return Error;Data.Add(path,(byte[])data.Clone());return null; }
        public override string UnlinkSync(string path) { Data.Remove(path);return null; }
    }
    public class TTSystemInfo {public string platform="ios",system="iOS 18";}
    public static class TT { public static TestFiles Files = new();public static TTSystemInfo Info=new();public static TTSystemInfo GetSystemInfo()=>Info; public static TTFileSystemManager GetFileSystemManager()=>Files; }
    public static class TTAudioManager
    {
        public static bool Supported = true;
        public static readonly List<InnerAudioContext> Created = new();
        public static InnerAudioContextOptions Options;
        public static bool CanUseNativeAudio()=>Supported;
        public static InnerAudioContext CreateInnerAudioContext(InnerAudioContextOptions options) { Options=options;var a=new InnerAudioContext();Created.Add(a);return a; }
        public sealed class InnerAudioContextOptions { public string src;public bool autoPlay,loop,needDownload;public double volume,playbackRate; }
        public sealed class InnerAudioContext
        {
            public Action onCanPlay,onPlay,onPause,onStop,onEnded,onWaiting;
            public Action<string> onError;
            public int AudioId {get;set;}=-1;
            public double Duration {get;set;}=12;
            public double Volume {get;set;}=1;
            public int Plays,Pauses,Stops,Destroys;
            public readonly Queue<Action<double>> PositionQueries=new();
            public readonly Queue<Action<bool>> PauseQueries=new();
            public void Play(){Plays++;}
            public void Pause(){Pauses++;}
            public void Stop(){Stops++;}
            public void Destroy(){Destroys++;AudioId=-1;}
            public void GetCurrentTime(Action<double> callback)=>PositionQueries.Enqueue(callback);
            public void GetPausedState(Action<bool> callback)=>PauseQueries.Enqueue(callback);
            public void Reply(double position,bool paused=false){if(PositionQueries.TryDequeue(out var p))p(position);if(PauseQueries.TryDequeue(out var q))q(paused);}
        }
    }
}

namespace TTSDK.UNBridgeLib
{
    public sealed class BridgeCallBack
    {
        public Action<LitJson.JsonData> OnSuccess;
        public Action<int,string> OnFailed;
        public Action OnTimeout;
    }
}
public static class UNBridge
{
    public static bool Wrapped,Malformed,WrongId;
    public static string Failure;
    public static bool Timeout;
    public static void Call(string name, TTSDK.UNBridgeLib.LitJson.JsonData data, TTSDK.UNBridgeLib.BridgeCallBack callback)
    {
        if(Failure!=null){callback.OnFailed(-1,Failure);return;}
        if(Timeout){callback.OnTimeout();return;}
        if(Malformed){callback.OnSuccess("invalid");return;}
        int id=(int)data["id"];
        var audio=TTSDK.TTAudioManager.Created.FindLast(a=>a.AudioId==id);
        void Reply(TTSDK.UNBridgeLib.LitJson.JsonData value)
        {
            if(Wrapped||WrongId){var result=new TTSDK.UNBridgeLib.LitJson.JsonData();result["id"]=WrongId?id+1:id;result["value"]=value;value=result;}
            callback.OnSuccess(value);
        }
        if(data["property"].ToString()=="currentTime")audio.PositionQueries.Enqueue(value=>Reply(value));
        else audio.PauseQueries.Enqueue(value=>Reply(value));
    }
}
namespace TTSDK.UNBridgeLib.LitJson
{
    public sealed class JsonData
    {
        object value;
        Dictionary<string,JsonData> fields;
        public bool IsObject=>fields!=null;
        public bool IsBoolean=>value is bool;
        public bool IsInt=>value is int;
        public bool IsLong=>value is long;
        public bool IsDouble=>value is double;
        public JsonData this[string key]{get=>fields[key];set{fields??=new();fields[key]=value;}}
        public bool ContainsKey(string key)=>fields?.ContainsKey(key)==true;
        public override string ToString()=>value?.ToString();
        public static implicit operator JsonData(int v)=>new(){value=v};
        public static implicit operator JsonData(long v)=>new(){value=v};
        public static implicit operator JsonData(double v)=>new(){value=v};
        public static implicit operator JsonData(bool v)=>new(){value=v};
        public static implicit operator JsonData(string v)=>new(){value=v};
        public static explicit operator int(JsonData v)=>(int)v.value;
        public static explicit operator long(JsonData v)=>(long)v.value;
        public static explicit operator double(JsonData v)=>(double)v.value;
        public static explicit operator bool(JsonData v)=>(bool)v.value;
    }
}
