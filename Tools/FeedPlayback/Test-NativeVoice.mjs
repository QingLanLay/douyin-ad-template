import fs from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import assert from 'node:assert/strict';
const projectRoot=process.argv[3]||process.cwd();
const source=fs.readFileSync(path.join(projectRoot,'Assets/Plugins/WebGL/FeedPlaybackAudio.jslib'),'utf8');
const events=[],files=new Map(),created=[];
let imports=0,failSource=false;
const memory=new ArrayBuffer(1024*1024);
const heap=new Uint8Array(memory),floats=new Float32Array(memory);
function createAudio(){
  const handlers=new Map();
  const audio={currentTime:0,duration:11,paused:true,ended:false,volume:1,playCalls:0,pauseCalls:0,stopCalls:0,destroyed:false,
    play(){this.playCalls++;this.paused=false;},
    pause(){this.pauseCalls++;this.paused=true;},
    stop(){this.stopCalls++;this.paused=true;this.currentTime=0;this.emit('Stop');},
    destroy(){this.destroyed=true;},
    emit(name,value){for(const callback of [...(handlers.get(name)||[])])callback(value);},
    callbacks(name){return [...(handlers.get(name)||[])];},
    count(){return [...handlers.values()].reduce((n,set)=>n+set.size,0);}
  };
  for(const name of ['Play','Pause','Stop','Ended','Error','Waiting','TimeUpdate']){
    audio['on'+name]=f=>{if(!handlers.has(name))handlers.set(name,new Set());handlers.get(name).add(f);};
    audio['off'+name]=f=>handlers.get(name)?.delete(f);
  }
  let src='';Object.defineProperty(audio,'src',{get:()=>src,set:value=>{src=value;if(failSource)audio.emit('Error',{errMsg:'source unavailable'});}});
  created.push(audio);return audio;
}
const sdk={_JS_Sound_Load(){imports++;return imports;},_JS_Sound_Load_PCM(){imports++;return imports;}};
const ctx={console:{log:console.log,warn:console.warn,error:message=>events.push(message)},LibraryManager:{library:{}},WEBAudio:{audioInstances:{},FAKEMOD_SAMPLERATE:44100,audioContext:null},
  HEAPU8:heap,HEAPF32:floats,UnityNamespace:{UnitySoundAdapter:sdk},
  mergeInto:(target,items)=>Object.assign(target,items),
  _JS_Sound_Load:()=>{throw Error('Unexpected legacy backend');},_JS_Sound_Load_PCM:()=>{throw Error('Unexpected legacy backend');},
  tt:{env:{USER_DATA_PATH:'ttfile://user'},getFileSystemManager:()=>({writeFileSync:(path,data)=>files.set(path,data),unlinkSync:path=>files.delete(path)}),createInnerAudioContext:createAudio}
};
vm.createContext(ctx);vm.runInContext(source,ctx);const api=ctx.LibraryManager.library;ctx.FeedVoiceState=api.$FeedVoiceState;
const checks=[];function test(name,callback){callback();checks.push({name,passed:true});}
function capture(){heap.set([82,73,70,70],128);api.FeedVoice_ProvideEncoded(128,64);}
capture();
test('明确音频数据保持独立副本',()=>{assert.equal(ctx.FeedVoiceState.captures.length,1);assert.equal(ctx.FeedVoiceState.captures[0].bytes.byteLength,64);});
test('原始声道表为空时可准备公开播放器',()=>assert.equal(api.FeedVoice_Bind(11,1),1));
const audio=created.at(-1);
test('隐藏预加载不调用播放',()=>{assert.equal(audio.playCalls,0);assert.equal(api.FeedVoice_Position(),-1);});
test('隐藏期间的播放请求被拒绝',()=>{api.FeedVoice_Play();assert.equal(audio.playCalls,0);});
api.FeedVoice_Arm();api.FeedVoice_Play();
test('有效展示只请求播放一次',()=>{api.FeedVoice_Play();assert.equal(audio.playCalls,1);});
test('等待宿主确认时字幕保持冻结',()=>{audio.currentTime=2;assert.equal(api.FeedVoice_Position(),-1);});
audio.currentTime=0;audio.emit('Play');
test('宿主确认后零播放位置有效',()=>assert.equal(api.FeedVoice_Position(),0));
test('重复展示保持已确认状态',()=>{api.FeedVoice_Arm();api.FeedVoice_Play();assert.equal(api.FeedVoice_Position(),0);assert.equal(audio.playCalls,1);});
audio.currentTime=4.2;
test('读取同一个播放器的实际位置',()=>assert.equal(api.FeedVoice_Position(),4.2));
test('宿主暂停事件立即冻结',()=>{audio.emit('Pause');assert.equal(api.FeedVoice_Status(),2);assert.equal(api.FeedVoice_Position(),-1);});
audio.emit('Play');test('宿主恢复事件保留位置',()=>assert.equal(api.FeedVoice_Position(),4.2));
test('宿主缓冲期间冻结',()=>{audio.emit('Waiting');assert.equal(api.FeedVoice_Position(),-1);assert.equal(api.FeedVoice_Status(),3);});
api.FeedVoice_Play();test('缓冲不重复调用播放',()=>assert.equal(audio.playCalls,1));
audio.emit('TimeUpdate');
test('缓冲状态不接受停滞的进度事件',()=>assert.equal(api.FeedVoice_Position(),-1));
audio.currentTime=4.3;audio.emit('TimeUpdate');
test('进度恢复事件释放缓冲状态',()=>assert.equal(api.FeedVoice_Position(),4.3));
const previous=audio.callbacks('Play')[0];api.FeedVoice_Pause();
test('卡片隐藏立即暂停',()=>{assert.equal(audio.pauseCalls,1);assert.equal(api.FeedVoice_Position(),-1);});
api.FeedVoice_Arm();previous();
test('上一次播放回调不确认恢复',()=>assert.equal(api.FeedVoice_Position(),-1));
api.FeedVoice_Play();audio.emit('Play');
test('恢复保留同一位置及音频实例',()=>{assert.equal(created.length,1);assert.equal(api.FeedVoice_Position(),4.3);});
test('音量与静音不修改字幕时间',()=>{api.FeedVoice_Volume(0);assert.equal(audio.volume,0);assert.equal(api.FeedVoice_Position(),4.3);});
audio.emit('Ended');
test('完成以播放器结束事件为准',()=>{assert.equal(api.FeedVoice_Status(),4);assert.equal(api.FeedVoice_Position(),-1);});
test('结束后不重复播放',()=>{api.FeedVoice_Play();assert.equal(audio.playCalls,2);});
const listener=audio.callbacks('Play')[0];api.FeedVoice_Release();
test('会话释放音源监听与临时文件',()=>{assert.equal(audio.count(),0);assert.equal(audio.destroyed,true);assert.equal(files.size,0);});
capture();assert.equal(api.FeedVoice_Bind(11,1),1);api.FeedVoice_Arm();listener();
test('前一会话回调不能影响新会话',()=>assert.equal(api.FeedVoice_Position(),-1));
api.FeedVoice_Release();
ctx.FeedVoiceState.captures=[];
test('缺少本次音频时拒绝准备',()=>assert.equal(api.FeedVoice_Bind(11,1),0));
api.FeedVoice_Release();
capture();failSource=true;
test('准备音源时的同步错误保持失败',()=>{assert.equal(api.FeedVoice_Bind(11,1),0);assert.equal(api.FeedVoice_Status(),-1);});
const rejected=created.at(-1);rejected.emit('Waiting');rejected.emit('Ended');rejected.emit('Stop');
test('失败后的迟到事件不恢复播放器',()=>assert.equal(api.FeedVoice_Status(),-1));
api.FeedVoice_Release();failSource=false;
test('准备失败后释放音源与临时文件',()=>{assert.equal(rejected.destroyed,true);assert.equal(rejected.count(),0);assert.equal(files.size,0);});
const reportDirectory=process.argv[2]||'.artifacts/FeedAudioTests';
fs.mkdirSync(reportDirectory,{recursive:true});
fs.writeFileSync(path.join(reportDirectory,'native-bridge-tests.json'),JSON.stringify({passed:true,count:checks.length,scope:'Public native audio API simulation and real project bridge',checks},null,2));
console.log(`PASS ${checks.length}/${checks.length}`);