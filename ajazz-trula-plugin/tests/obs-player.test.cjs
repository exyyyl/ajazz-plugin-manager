const {test}=require('node:test');
const assert=require('node:assert/strict');
const {readFileSync}=require('node:fs');
const vm=require('node:vm');
const script=readFileSync(require('node:path').join(__dirname,'../plugin/obs-player.js'),'utf8');
function fixture(overlay=false) {
  const settings={panel:{orderVolume:5,playlistVolume:7},source:{orderVolume:50,playlistVolume:50}};
  const playback={isPlaying:false,isStarted:false,startTime:0};
  const player={state:{currentTrack:{id:'first'}},dispatch({type}){playback.isPlaying=type==='PLAY';},setNextTrack(){player.state.currentTrack={id:'next'};},setPrevTrack(){player.state.currentTrack={id:'previous'};}};
  const query={queryKey:['settings'],state:{data:settings}};
  let pending;
  const queryClient={getQueryCache(){return {getAll:()=>[query]};},async invalidateQueries(){for(const [section,fields] of Object.entries(pending)) Object.assign(settings[section],fields);}};
  const provider={settings:overlay?{...settings,panel:{orderVolume:25,playlistVolume:35}}:settings,setPatch(value){assert.equal(value,undefined);provider.settings=settings;}};
  const fibers=[{memoizedProps:{value:player}},{memoizedState:{memoizedState:playback}},{memoizedProps:{value:provider}},{memoizedProps:{value:queryClient}}];
  fibers.forEach((fiber,i)=>fiber.sibling=fibers[i+1]);
  const root={child:fibers[0]}, container={__reactContainer$test:{stateNode:{current:root}}};
  const location={origin:'https://trula.io',pathname:'/cp/events',href:'https://trula.io/cp/events?token=test'};
  const context=vm.createContext({location,URL,document:{querySelectorAll:()=>[container]},setTimeout,fetch:async(url,options)=>{assert.equal(url,'/api/v1/widget/panel/');assert.equal(options.headers.token,'test');pending=JSON.parse(options.body);return {ok:true};}});
  return {run:vm.runInContext('('+script+')',context),settings,playback,location,root,player};
}
test('reads the mounted OBS player and follows manual changes',async()=>{
  const f=fixture(); assert.equal((await f.run('test','read')).playing,false);
  f.playback.isPlaying=true; f.settings.panel.orderVolume=11;
  const current=await f.run('test','read');assert.equal(current.playing,true);assert.equal(current.panel.orderVolume,11);
});
test('pause/play use the existing dispatcher; track commands confirm a new track',async()=>{
  const f=fixture();assert.equal((await f.run('test','togglePlay')).playing,true);
  assert.equal((await f.run('test','togglePlay')).playing,false);
  await f.run('test','nextTrack');assert.equal(f.player.state.currentTrack.id,'next');
  await f.run('test','prevTrack');assert.equal(f.player.state.currentTrack.id,'previous');
});
test('patch rereads panel settings and preserves the other output',async()=>{
  const f=fixture();const state=await f.run('test','patch',{source:{orderVolume:0,playlistVolume:0}});
  assert.equal(state.source.orderVolume,0);assert.equal(state.panel.orderVolume,5);assert.equal(state.panel.playlistVolume,7);
});
test('wrong token or origin cannot control the panel',async()=>{
  const f=fixture();await assert.rejects(f.run('wrong','togglePlay'),/Wrong panel/);
  f.location.origin='https://example.com';await assert.rejects(f.run('test','togglePlay'),/Wrong panel/);
  assert.equal(f.playback.isPlaying,false);
});
test('manual slider overlay cannot mask a confirmed mute',async()=>{
  const f=fixture(true);assert.equal((await f.run('test','read')).panel.playlistVolume,35);
  const state=await f.run('test','patch',{panel:{orderVolume:0,playlistVolume:0}});
  assert.equal(state.panel.orderVolume,0);assert.equal(state.panel.playlistVolume,0);assert.equal(state.source.orderVolume,50);
});
test('missing providers fail without creating a player',async()=>{
  const f=fixture();f.root.child=null;await assert.rejects(f.run('test','read'),/providers unavailable/);
});
test('exports only public track metadata for the chat action',async()=>{
  const f=fixture();f.player.state.currentTrack.track={title:'Starboy',platform:'youtube',externalId:'dMMUH_ZpbB0',privateToken:'never-export'};
  const state=await f.run('test','read');assert.equal(state.track.title,'Starboy');assert.equal(state.track.externalId,'dMMUH_ZpbB0');assert.equal('privateToken' in state.track,false);
});
