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
  let expected;
  if(command==='patch') {
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
  } else if(command==='nextTrack') initial.player.setNextTrack();
  else if(command==='prevTrack') initial.player.setPrevTrack();
  else if(command!=='read') throw Error('Unknown command');
  for(let i=0;i<40;i++) {
    const {player,playback,settings}=contexts();
    const applied=command==='patch' ? Object.entries(patch).every(([section,fields])=>Object.entries(fields).every(([key,v])=>settings[section]?.[key]===v)) : expected!==undefined ? playback.isPlaying===expected : player.state.currentTrack?.id!==before;
    if(command==='read' || applied) {
      const track=player.state.currentTrack?.track;
      return {...settings,playing:playback.isPlaying,hasTrack:!!player.state.currentTrack,track:track?{title:track.title,platform:track.platform,externalId:track.externalId}:null};
    }
    await new Promise(resolve=>setTimeout(resolve,100));
  }
  throw Error('Command not confirmed');
}
