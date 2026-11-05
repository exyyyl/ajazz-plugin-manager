const {test}=require('node:test');
const assert=require('node:assert/strict');
const {readFileSync}=require('node:fs');
const vm=require('node:vm');
const script=readFileSync(require('node:path').join(__dirname,'../plugin/obs-player.js'),'utf8');
function fixture(overlay=false,deleteOk=true) {
  const storage=new Map(),calls=[];
  const localStorage={getItem:key=>storage.get(key)??null,setItem:(key,value)=>storage.set(key,value),removeItem:key=>storage.delete(key)};
  const searchTrack={id:123,title:'Starboy',platform:'youtube',externalId:'dMMUH_ZpbB0'};
  const settings={panel:{orderVolume:5,playlistVolume:7},source:{orderVolume:50,playlistVolume:50}};
  const playback={isPlaying:false,isStarted:false,startTime:0};
  const player={state:{currentTrack:{id:'first'}},dispatch({type}){playback.isPlaying=type==='PLAY';},setNextTrack(){const queue=JSON.parse(localStorage.getItem('ForwardTracks')||'[]');player.state.currentTrack=queue.length?queue.shift():{id:'next'};localStorage.setItem('ForwardTracks',JSON.stringify(queue));},setPrevTrack(){player.state.currentTrack={id:'previous'};}};
  const query={queryKey:['settings'],state:{data:settings}};
  let pending;
  const queryClient={getQueryCache(){return {getAll:()=>[query]};},async invalidateQueries(){for(const [section,fields] of Object.entries(pending)) Object.assign(settings[section],fields);}};
  const provider={settings:overlay?{...settings,panel:{orderVolume:25,playlistVolume:35}}:settings,setPatch(value){assert.equal(value,undefined);provider.settings=settings;}};
  const fibers=[{memoizedProps:{value:player}},{memoizedState:{memoizedState:playback}},{memoizedProps:{value:provider}},{memoizedProps:{value:queryClient}}];
  fibers.forEach((fiber,i)=>fiber.sibling=fibers[i+1]);
  const root={child:fibers[0]}, container={__reactContainer$test:{stateNode:{current:root}}};
  const location={origin:'https://trula.io',pathname:'/cp/events',href:'https://trula.io/cp/events?token=test'};
  const context=vm.createContext({localStorage,location,URL,document:{querySelectorAll:()=>[container]},setTimeout,fetch:async(url,options)=>{calls.push({url,method:options.method||'GET'});if(url.startsWith('/api/v1/playlist/search?'))return {ok:true,json:async()=>({data:[{type:'track',data:searchTrack}]})};if(url==='/api/v1/widget/panel/orders/actions/delete'){assert.equal(options.method,'POST');assert.deepEqual(JSON.parse(options.body),{orderId:'first'});return {ok:deleteOk};}assert.equal(url,'/api/v1/widget/panel/');assert.equal(options.headers.token,'test');pending=JSON.parse(options.body);return {ok:true};}});
  return {run:vm.runInContext('('+script+')',context),settings,playback,location,root,player,storage,calls,searchTrack};
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

test('skip removes current order and advances, never removes playlist tracks',async()=>{
 const f=fixture();await f.run('test','skipOrder');assert.equal(f.player.state.currentTrack.id,'next');
 const playlist=fixture();playlist.player.state.currentTrack.playlist={id:1};await assert.rejects(playlist.run('test','skipOrder'),/не играет заказ/);assert.equal(playlist.player.state.currentTrack.id,'first');
 const failed=fixture(false,false);await assert.rejects(failed.run('test','skipOrder'),/не подтвердила/);assert.equal(failed.player.state.currentTrack.id,'first');
});

test('reports the current output category for accurate mute icons',async()=>{
 const f=fixture();assert.equal((await f.run('test','read')).isPlaylistTrack,false);
 f.player.state.currentTrack.playlist={id:'playlist'};assert.equal((await f.run('test','read')).isPlaylistTrack,true);
});

test('enqueue appends an exact track without interrupting playback or writing a playlist',async()=>{
 const f=fixture();f.playback.isPlaying=true;const state=await f.run('test','enqueue',{requestId:'request-1',url:'https://youtu.be/dMMUH_ZpbB0'});
 assert.equal(state.queuedRequestId,'request-1');assert.equal(f.player.state.currentTrack.id,'first');assert.equal(f.playback.isPlaying,true);
 const queue=JSON.parse(f.storage.get('ForwardTracks'));assert.equal(queue.length,1);assert.equal(queue[0].track.externalId,'dMMUH_ZpbB0');assert.equal(queue[0].playlist.id,null);
 assert.equal(f.calls.length,1);assert.equal(f.calls[0].method,'GET');assert.match(f.calls[0].url,/playlist\/search/);
 await f.run('test','nextTrack');assert.equal(f.player.state.currentTrack.track.externalId,'dMMUH_ZpbB0');assert.equal(JSON.parse(f.storage.get('ForwardTracks')).length,0);
});
test('retrying an accepted redemption never appends it twice, even after consumption',async()=>{
 const f=fixture(),request={requestId:'request-1',url:'https://youtu.be/dMMUH_ZpbB0'};
 await f.run('test','enqueue',request);await f.run('test','enqueue',request);assert.equal(JSON.parse(f.storage.get('ForwardTracks')).length,1);
 await f.run('test','nextTrack');await f.run('test','enqueue',request);assert.equal(JSON.parse(f.storage.get('ForwardTracks')).length,0);assert.equal(f.calls.length,1);
});
test('queue rejects untrusted links, wrong search matches and overflow',async()=>{
 const f=fixture();await assert.rejects(f.run('test','enqueue',{requestId:'r',url:'https://evil.example/track'}),/ссылка/);assert.equal(f.calls.length,0);
 f.searchTrack.externalId='o6ytLPKscrU';await assert.rejects(f.run('test','enqueue',{requestId:'r',url:'https://youtu.be/dMMUH_ZpbB0'}),/не нашла/);assert.equal(f.storage.has('ForwardTracks'),false);
 f.storage.set('ForwardTracks',JSON.stringify(Array.from({length:30},(_,id)=>({id}))));
 await assert.rejects(f.run('test','enqueue',{requestId:'r',url:'https://youtu.be/dMMUH_ZpbB0'}),/30/);
});

test('native empty queue sentinel is accepted',async()=>{
 const f=fixture();f.storage.set('ForwardTracks','undefined');await f.run('test','enqueue',{requestId:'r',url:'https://youtu.be/dMMUH_ZpbB0'});assert.equal(JSON.parse(f.storage.get('ForwardTracks')).length,1);
});

test('an idle empty player starts the forward queue',async()=>{
 const f=fixture();f.player.state.currentTrack=null;await f.run('test','enqueue',{requestId:'idle',url:'https://youtu.be/dMMUH_ZpbB0'});assert.equal(f.player.state.currentTrack.track.externalId,'dMMUH_ZpbB0');
});
