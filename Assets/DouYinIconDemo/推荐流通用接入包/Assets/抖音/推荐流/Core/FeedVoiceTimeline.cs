using System;

namespace Douyin.Feed
{
    [Serializable] public sealed class FeedCue { public float start,end; public string text; }
    [Serializable] public sealed class FeedCueSheet { public string key; public float duration; public int samples,sampleRate; public FeedCue[] cues; }

    public enum FeedVoicePhase { Ready, Starting, Playing, Paused, Buffering, Completed, Failed }

    public sealed class FeedVoiceTimeline
    {
        public FeedVoicePhase Phase { get; private set; } = FeedVoicePhase.Ready;
        public double Position { get; private set; }
        public bool Confirmed { get; private set; }
        public bool Visible { get; private set; }
        public bool HasFailed => Phase == FeedVoicePhase.Failed;
        private double duration, requestedAt, lastObservation, bufferingAt;
        private bool advancing;

        public void Reset(double cueDuration, double now)
        {
            duration=cueDuration;Position=0;Confirmed=false;Visible=false;advancing=false;
            Phase=FeedVoicePhase.Ready;lastObservation=requestedAt=now;
        }
        public void Show(double now)
        {
            if(Visible||Phase==FeedVoicePhase.Completed||Phase==FeedVoicePhase.Failed)return;
            Visible=true;Confirmed=false;advancing=false;
            Phase=FeedVoicePhase.Starting;lastObservation=requestedAt=now;
        }
        public void Hide(double now)
        {
            Visible=false;Confirmed=false;advancing=false;lastObservation=now;
            if(Phase!=FeedVoicePhase.Completed&&Phase!=FeedVoicePhase.Failed)Phase=FeedVoicePhase.Paused;
        }
        public void Observe(double now, bool hostConfirmed, double hostPosition, double hostDuration,
            bool buffering, bool ended, bool failed)
        {
            double delta=Math.Max(0,now-lastObservation);lastObservation=now;
            if(!Visible||Phase==FeedVoicePhase.Completed||Phase==FeedVoicePhase.Failed)return;
            if(failed){Fail();return;}
            if(ended){Phase=FeedVoicePhase.Completed;Confirmed=false;advancing=false;return;}
            if(buffering)
            {
                if(Phase!=FeedVoicePhase.Buffering)bufferingAt=now;
                Phase=FeedVoicePhase.Buffering;advancing=false;
                if(now-bufferingAt>=30)Fail();
                return;
            }
            if(!hostConfirmed)
            {
                if(advancing||Phase==FeedVoicePhase.Buffering)requestedAt=now;
                advancing=false;Confirmed=false;Phase=FeedVoicePhase.Starting;
                if(now-requestedAt>=30)Fail();
                return;
            }
            bool previouslyAdvancing=advancing;
            Confirmed=true;advancing=true;Phase=FeedVoicePhase.Playing;
            if(hostPosition>0&&!double.IsNaN(hostPosition)&&!double.IsInfinity(hostPosition))
                Position=hostPosition*(hostDuration>0?duration/hostDuration:1);
            else if(previouslyAdvancing)Position+=delta;
            Position=Math.Max(0,Math.Min(duration,Position));
        }
        public int CueIndex(FeedCueSheet sheet)
        {
            if(!Visible||!Confirmed||Phase!=FeedVoicePhase.Playing||sheet?.cues==null)return -1;
            for(int i=0;i<sheet.cues.Length;i++)
                if(Position>=sheet.cues[i].start&&Position<sheet.cues[i].end)return i;
            return -1;
        }
        public void Fail(){Phase=FeedVoicePhase.Failed;Confirmed=false;advancing=false;}
        public void Stop(){Visible=false;Confirmed=false;advancing=false;Phase=FeedVoicePhase.Completed;}
    }
}
