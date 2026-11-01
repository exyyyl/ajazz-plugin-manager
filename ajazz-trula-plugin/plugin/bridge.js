// Runs inside Trula's own player providers at the real HTTPS origin.
function ProjectDeckBridge() {
  const player=of(), playback=Wa(), settings=zo(), patch=_v();
  const current=u.useRef(); current.current={player,playback,settings,patch};
  u.useEffect(()=>{
    let stopped=false, busy=false;
    const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
    const snapshot=()=>({...current.current.settings.settings,playing:current.current.playback.isPlaying,hasTrack:!!current.current.player.state?.currentTrack});
    const publish=message=>window.chrome.webview.postMessage({...message,state:snapshot()});
    const listener=async event=>{
      const item=event.data;
      if(!item?.id || busy) return;
      busy=true; let error=false;
      try {
        const value=current.current; let expected;
        if(item.command==='patch') await value.patch(item.patch);
        else if(item.command==='togglePlay') { expected=!value.playback.isPlaying; value.player.dispatch({type:expected?'PLAY':'PAUSE'}); }
        else if(item.command==='nextTrack') value.player.setNextTrack();
        else if(item.command==='prevTrack') value.player.setPrevTrack();
        else throw Error('Unknown command');
        for(let n=0;n<30;n++) {
          await wait(100);
          const state=snapshot();
          const applied=item.command==='patch' ? Object.entries(item.patch).every(([section,fields])=>Object.entries(fields).every(([key,v])=>state[section]?.[key]===v)) : expected===undefined || state.playing===expected;
          if(applied) break;
          if(n===29) throw Error('Not confirmed');
        }
      } catch { error=true; }
      if(!stopped) publish({type:'result',id:item.id,error});
      busy=false;
    };
    window.chrome.webview.addEventListener('message',listener);
    const timer=setInterval(()=>{if(current.current.settings.settings?.panel)publish({type:'state'});},500);
    return()=>{stopped=true;clearInterval(timer);window.chrome.webview.removeEventListener('message',listener);};
  },[]);
  return null;
}