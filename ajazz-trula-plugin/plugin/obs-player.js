async function(token,command,patch) {
  if(location.origin!=='https://trula.io' || !location.pathname.startsWith('/cp/') || new URL(location.href).searchParams.get('token')!==token) throw Error('Wrong panel');
  // Read current provider values from the mounted React tree, never create a second root/player.
  function contexts() {
    let root;
    for(const node of document.querySelectorAll('body *')) {
      const key=Object.keys(node).find(k=>k.startsWith('__reactContainer$'));
      if(key) { const fiber=node[key]; root=fiber.stateNode?.current || fiber; break; }
    }
    if(!root) throw Error('React root unavailable');
    let player, playback, settings, settingsProvider, queryClient;
    const stack=[root], seen=new Set();
    while(stack.length) {
      const fiber=stack.pop(); if(!fiber || seen.has(fiber)) continue; seen.add(fiber);
      const v=fiber.memoizedProps?.value;
      if(v?.settings?.panel && v?.settings?.source) {settings=v.settings;settingsProvider=v;}
      if(v && typeof v.getQueryCache==='function' && typeof v.invalidateQueries==='function') queryClient=v;
      if(v && typeof v.dispatch==='function' && v.state && 'currentTrack' in v.state && typeof v.setNextTrack==='function') player=v;
      // Trula keeps playback in useSyncExternalStore, separate from its player context.
      for(let hook=fiber.memoizedState, n=0;hook && n<100;hook=hook.next,n++) {
        const snapshot=hook.memoizedState;
        if(snapshot && typeof snapshot.isPlaying==='boolean' && typeof snapshot.isStarted==='boolean' && typeof snapshot.startTime==='number') playback=snapshot;
      }
      if(fiber.child) stack.push(fiber.child);
      if(fiber.sibling) stack.push(fiber.sibling);
    }
    if(!player || !playback || !settings || !queryClient) throw Error('Player providers unavailable');
    return {player,playback,settings,settingsProvider,queryClient};
  }
  const initial=contexts();
  const before=initial.player.state.currentTrack?.id;
  let expected,queuedRequestId=null;
  let queueDone=false;
  if(command==='enqueue') {
    if(!patch || !/^[A-Za-z0-9-]{1,100}$/.test(patch.requestId))throw Error('Некорректная заявка.');
    const input=new URL(patch.url), host=input.hostname;
    let externalId='',platform='';
    if(input.protocol!=='https:' || input.username || input.password || input.port)throw Error('Неподдерживаемая ссылка.');
    if(host==='youtu.be'){externalId=input.pathname.slice(1);platform='youtube';}
    else if(host==='vk.com'){const m=input.pathname.match(/^\/video(-?[0-9]+_[0-9]+)$/);if(m){externalId=m[1];platform='vk';}}
    if((platform==='youtube' && !/^[A-Za-z0-9_-]{11}$/.test(externalId)) || !platform)throw Error('Неподдерживаемая ссылка.');
    const receiptsKey='AjazzTrulaAcceptedRequests.v1',queueKey='ForwardTracks';
    const storedArray=key=>{const raw=localStorage.getItem(key);return !raw||raw==='undefined'||raw==='null'?[]:JSON.parse(raw);};
    let queue,receipts;
    try {queue=storedArray(queueKey);receipts=storedArray(receiptsKey);}catch {throw Error('Очередь Trula повреждена. Обновите панель.');}
    if(!Array.isArray(queue) || !Array.isArray(receipts))throw Error('Неизвестный формат очереди Trula.');
    if(receipts.includes(patch.requestId) || queue.some(x=>x?._ajazzRequestId===patch.requestId)) {
      queuedRequestId=patch.requestId;queueDone=true;
    }else {
      if(queue.length>=30)throw Error('В очереди следующих треков уже 30 записей.');
      const response=await fetch('/api/v1/playlist/search?limit=20&offset=0&query='+encodeURIComponent(patch.url),{headers:{token}});
      if(!response.ok)throw Error('Trula не смогла найти трек по ссылке.');
      const result=await response.json();
      const found=(result.data||[]).filter(x=>x.type==='track').map(x=>x.data).find(x=>x?.platform===platform && String(x.externalId)===externalId);
      if(!found || found.id==null || !found.title)throw Error('Trula не нашла указанный трек.');
      // Trula consumes ForwardTracks before selecting a new order/background track.
      // The playlist marker selects music playback/volume, but no playlist is written to the server.
      const entry={id:'ajazz-'+patch.requestId,track:found,playlist:{id:null,title:'Принятые заявки',type:'playlist'},_ajazzRequestId:patch.requestId};
      // Re-read immediately before append so a track ending during the search cannot be resurrected.
      queue=storedArray(queueKey);receipts=storedArray(receiptsKey);
      if(!Array.isArray(queue) || queue.length>=30 || !Array.isArray(receipts))throw Error('Очередь Trula изменилась. Повторите нажатие.');
      if(!receipts.includes(patch.requestId) && !queue.some(x=>x?._ajazzRequestId===patch.requestId)) {
        localStorage.setItem(queueKey,JSON.stringify([...queue,entry]));
        localStorage.setItem(receiptsKey,JSON.stringify([...receipts,patch.requestId].slice(-1000)));
        if(!contexts().player.state.currentTrack)contexts().player.setNextTrack();
      }
      queuedRequestId=patch.requestId;queueDone=true;
    }
  } else if(command==='patch') {
    const response=await fetch('/api/v1/widget/panel/',{method:'PATCH',headers:{token,'Content-Type':'application/json'},body:JSON.stringify(patch)});
    if(!response.ok) throw Error('Settings rejected');
    // Manual volume sliders leave an optimistic overlay over the query data.
    // Drop it after a successful save so it cannot mask the confirmed server values.
    if(typeof initial.settingsProvider.setPatch==='function') initial.settingsProvider.setPatch(undefined);
    const queries=initial.queryClient.getQueryCache().getAll().filter(q=>q.state.data?.panel && q.state.data?.source);
    if(queries.length!==1) throw Error('Settings query unavailable');
    await initial.queryClient.invalidateQueries({queryKey:queries[0].queryKey,exact:true});
  } else if(command==='togglePlay') {
    if(!initial.player.state.currentTrack) throw Error('No track');
    expected=!initial.playback.isPlaying;
    initial.player.dispatch({type:expected?'PLAY':'PAUSE'});
  } else if(command==='skipOrder') {
    const current=initial.player.state.currentTrack;
    if(!current || current.playlist || !current.id) throw Error('Сейчас не играет заказ из очереди.');
    const response=await fetch('/api/v1/widget/panel/orders/actions/delete',{method:'POST',headers:{token,'Content-Type':'application/json'},body:JSON.stringify({orderId:current.id})});
    if(!response.ok) throw Error('Trula не подтвердила удаление заказа из очереди.');
    if(contexts().player.state.currentTrack?.id===current.id) initial.player.setNextTrack();
  } else if(command==='nextTrack') initial.player.setNextTrack();
  else if(command==='prevTrack') initial.player.setPrevTrack();
  else if(command!=='read') throw Error('Unknown command');
  for(let i=0;i<40;i++) {
    const {player,playback,settings}=contexts();
    const applied=command==='patch' ? Object.entries(patch).every(([section,fields])=>Object.entries(fields).every(([key,v])=>settings[section]?.[key]===v)) : expected!==undefined ? playback.isPlaying===expected : player.state.currentTrack?.id!==before;
    if(command==='read' || queueDone || applied) {
      const track=player.state.currentTrack?.track;
      return {...settings,queuedRequestId,playing:playback.isPlaying,hasTrack:!!player.state.currentTrack,isPlaylistTrack:!!player.state.currentTrack?.playlist,track:track?{title:track.title,platform:track.platform,externalId:track.externalId}:null};
    }
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  throw Error('Command not confirmed');
}
