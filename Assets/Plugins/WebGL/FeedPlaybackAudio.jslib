mergeInto(LibraryManager.library, {
  $FeedVoiceState__deps: ['$WEBAudio'],
  $FeedVoiceState: {
    before: {}, target: null, original: null, source: null, media: null,
    confirmed: false, epoch: 0, onPlay: null, contextAt: 0,
    captures: [], native: false,
    audio: null, file: null, api: null, listeners: [], visible: false,
    phase: 0, duration: 0, error: '', serial: 0,
    detach: function () {
      if(this.media && this.onPlay) {
        if(typeof this.media.offPlay==='function')this.media.offPlay(this.onPlay);
        if(typeof this.media.removeEventListener==='function')this.media.removeEventListener('playing',this.onPlay);
      }
      this.onPlay=null;this.media=null;this.confirmed=false;
    },
    watch: function (source) {
      this.detach();this.source=source;
      this.contextAt=WEBAudio.audioContext?WEBAudio.audioContext.currentTime:0;
      var state=this, epoch=++this.epoch, media=source && source.mediaElement;
      if(!media)return;
      this.media=media;
      this.onPlay=function(){if(state.epoch===epoch&&state.media===media)state.confirmed=true;};
      if(typeof media.onPlay==='function')media.onPlay(this.onPlay);
      else if(typeof media.addEventListener==='function')media.addEventListener('playing',this.onPlay);
    },
    clearNativeListeners: function () {
      var audio=this.audio;
      this.listeners.forEach(function(item){
        if(audio&&typeof audio['off'+item.name]==='function')audio['off'+item.name](item.callback);
      });
      this.listeners=[];
    },
    listenNative: function () {
      this.clearNativeListeners();
      var state=this, audio=this.audio, epoch=++this.epoch;
      function valid(){return state.epoch===epoch&&state.audio===audio;}
      function listen(name,callback){
        if(typeof audio['on'+name]==='function'){
          audio['on'+name](callback);state.listeners.push({name:name,callback:callback});
        }
      }
      listen('Play',function(){
        if(!valid()||state.phase===4||state.phase===-1)return;
        if(!state.visible){audio.pause();return;}
        state.confirmed=true;state.phase=1;
      });
      listen('TimeUpdate',function(){
        if(valid()&&state.visible&&state.confirmed&&state.phase===3&&Number(audio.currentTime)>state.waitingAt)state.phase=1;
      });
      listen('Waiting',function(){if(valid()&&state.visible&&state.phase!==4&&state.phase!==-1){state.waitingAt=Number(audio.currentTime)||0;state.phase=3;}});
      listen('Pause',function(){if(valid()&&state.phase!==4&&state.phase!==-1){state.confirmed=false;state.phase=2;}});
      listen('Ended',function(){if(valid()&&state.phase!==-1){state.confirmed=false;state.phase=4;}});
      listen('Stop',function(){if(valid()&&state.phase!==-1){state.confirmed=false;state.phase=4;}});
      listen('Error',function(error){
        if(valid())state.fail('Native audio: '+JSON.stringify(error));
      });
    },
    fail: function (message) {
      this.error=message;this.phase=-1;this.confirmed=false;
      console.error('[FeedVoice] '+message);
    },
    bindNative: function (duration) {
      var candidates=this.captures.filter(function(c){return !c.seconds||Math.abs(c.seconds-duration)<0.08;});
      if(candidates.length!==1){this.fail('Expected one narration asset; received '+candidates.length+'.');return 0;}
      var api=typeof tt!=='undefined'?tt:null;
      if(!api||typeof api.createInnerAudioContext!=='function'||typeof api.getFileSystemManager!=='function'){
        this.fail('Public native audio API is unavailable.');return 0;
      }
      var bytes=candidates[0].bytes, head=new Uint8Array(bytes,0,Math.min(4,bytes.byteLength));
      var extension=head[0]===82&&head[1]===73?'wav':head[0]===79&&head[1]===103?'ogg':'mp3';
      try {
        this.api=api;var fs=api.getFileSystemManager();
        this.file=api.env.USER_DATA_PATH+'/feed-voice-'+Date.now()+'-'+(++this.serial)+'.'+extension;
        fs.writeFileSync(this.file,bytes);
        this.api=api;this.audio=api.createInnerAudioContext();this.native=true;
        this.audio.autoplay=false;this.audio.loop=false;this.duration=duration;
        this.listenNative();this.phase=6;this.audio.src=this.file;this.captures=[];
        return this.phase===-1?0:1;
      }catch(error){this.fail(String(error));return 0;}
    },
    release: function () {
      this.epoch++;this.clearNativeListeners();this.detach();
      if(this.audio){try{this.audio.destroy();}catch(error){console.warn('[FeedVoice] '+String(error));}}
      if(this.file&&this.api){try{this.api.getFileSystemManager().unlinkSync(this.file);}catch(error){console.warn('[FeedVoice] '+String(error));}}
      if(this.target&&this.original)this.target.createSourceNode=this.original;
      this.target=null;this.original=null;this.source=null;this.before={};
      this.audio=null;this.file=null;this.api=null;this.native=false;this.visible=false;
      this.captures=[];this.phase=0;this.error='';
    }
  },
  FeedVoice_ProvideEncoded__deps: ['$FeedVoiceState'],
  FeedVoice_ProvideEncoded: function (ptr,length) {
    FeedVoiceState.captures=[{id:1,bytes:HEAPU8.buffer.slice(ptr,ptr+length),seconds:0}];
  },
  FeedVoice_Remember__deps: ['$FeedVoiceState'],
  FeedVoice_Remember: function () {
    FeedVoiceState.before={};
    Object.keys(WEBAudio.audioInstances||{}).forEach(function(k){FeedVoiceState.before[k]=true;});
  },
  FeedVoice_Bind__deps: ['$FeedVoiceState'],
  FeedVoice_Bind: function (duration,container) {
    var state=FeedVoiceState;
    if(container)return state.bindNative(duration);
    var candidates=[], exact=[];
    Object.keys(WEBAudio.audioInstances||{}).forEach(function(k){
      var clip=WEBAudio.audioInstances[k];
      if(!clip||typeof clip.createSourceNode!=='function')return;
      if(!state.before[k])candidates.push(clip);
      try{if(Math.abs(clip.getLength()/WEBAudio.FAKEMOD_SAMPLERATE-duration)<.06)exact.push(clip);}catch(error){}
    });
    var clip=candidates.length===1?candidates[0]:exact.length===1?exact[0]:null;
    if(!clip)return 0;
    state.target=clip;state.original=clip.createSourceNode;
    clip.createSourceNode=function(){var source=state.original.apply(this,arguments);state.watch(source);return source;};
    return 1;
  },
  FeedVoice_Arm__deps: ['$FeedVoiceState'],
  FeedVoice_Arm: function () {
    var state=FeedVoiceState;
    if(state.native&&state.visible&&(state.phase===1||state.phase===3||state.phase===7))return;
    state.confirmed=false;
    if(state.native){state.visible=true;state.listenNative();return;}
    if(state.source)state.watch(state.source);
  },
  FeedVoice_Native__deps: ['$FeedVoiceState'],
  FeedVoice_Native: function () {return FeedVoiceState.native?1:0;},
  FeedVoice_Play__deps: ['$FeedVoiceState'],
  FeedVoice_Play: function () {
    var state=FeedVoiceState;
    if(!state.native||!state.visible||state.phase===1||state.phase===3||state.phase===7||state.phase===4||state.phase===-1)return;
    state.phase=7;
    try{state.audio.play();}catch(error){state.fail(String(error));}
  },
  FeedVoice_Pause__deps: ['$FeedVoiceState'],
  FeedVoice_Pause: function () {
    var state=FeedVoiceState;state.visible=false;state.confirmed=false;
    if(state.native&&state.audio&&state.phase!==4&&state.phase!==-1){state.phase=2;state.audio.pause();}
  },
  FeedVoice_Stop__deps: ['$FeedVoiceState'],
  FeedVoice_Stop: function () {
    var state=FeedVoiceState;state.visible=false;state.confirmed=false;
    if(state.native&&state.audio){state.phase=4;state.epoch++;state.clearNativeListeners();state.audio.stop();}
  },
  FeedVoice_Volume__deps: ['$FeedVoiceState'],
  FeedVoice_Volume: function (volume) {if(FeedVoiceState.native&&FeedVoiceState.audio)FeedVoiceState.audio.volume=Math.max(0,Math.min(1,volume));},
  FeedVoice_Status__deps: ['$FeedVoiceState'],
  FeedVoice_Status: function () {return FeedVoiceState.native?FeedVoiceState.phase:0;},
  FeedVoice_Duration__deps: ['$FeedVoiceState'],
  FeedVoice_Duration: function () {
    var value=FeedVoiceState.audio?Number(FeedVoiceState.audio.duration):0;
    return Number.isFinite(value)&&value>0?value:FeedVoiceState.duration;
  },
  FeedVoice_Position__deps: ['$FeedVoiceState'],
  FeedVoice_Position: function () {
    var state=FeedVoiceState;
    if(state.native){
      if(!state.visible||!state.confirmed||state.phase!==1)return -1;
      if(state.audio.paused===true){state.phase=2;state.confirmed=false;return -1;}
      if(state.audio.ended===true){state.phase=4;state.confirmed=false;return -1;}
      var t=Number(state.audio.currentTime);return Number.isFinite(t)&&t>=0?t:0;
    }
    var source=state.source;
    if(!source||source.isStopped)return -1;
    if(state.media){
      if(!state.confirmed||state.media.paused===true||state.media.ended===true)return -1;
      var time=Number(state.media.currentTime);return Number.isFinite(time)&&time>=0?time:0;
    }
    if(typeof source.estimatePlaybackPosition==='function'){
      var position=source.estimatePlaybackPosition();
      if(Number.isFinite(position)&&position>0&&WEBAudio.audioContext&&WEBAudio.audioContext.currentTime>state.contextAt)return position;
    }
    return -1;
  },
  FeedVoice_Release__deps: ['$FeedVoiceState'],
  FeedVoice_Release: function () {FeedVoiceState.release();}
});