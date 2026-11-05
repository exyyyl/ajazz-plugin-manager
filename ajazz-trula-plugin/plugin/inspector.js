'use strict';
let socket,context,action,inspectorId,settings={};
const copyAnimations=new WeakMap();
function updateText(element,text){
 if(typeof element==='string')element=document.getElementById(element);
 text=String(text??'');if(element.textContent===text)return;
 const previous=copyAnimations.get(element),height=element.getBoundingClientRect().height;
 if(previous)previous.cancel();
 element.textContent=text;
 if(!element.animate||matchMedia('(prefers-reduced-motion: reduce)').matches||!element.getClientRects().length)return;
 const nextHeight=element.getBoundingClientRect().height;
 const from={opacity:.65},to={opacity:1};
 // Resize block copy along with its text; notices have their own collapsing wrapper.
 if(height&&nextHeight&&height!==nextHeight&&element.matches('#status,#action-description-text')){from.height=height+'px';to.height=nextHeight+'px';}
 copyAnimations.set(element,element.animate([from,to],{duration:180,easing:'cubic-bezier(.2,.7,.3,1)'}));
}
function updateClass(element,name){if(element.className!==name)element.className=name;}
function updateStatus(text){updateText('status',['Подключено к Trula в OBS.','Trula подключена к OBS.'].includes(text)?'Trula подключена':text);}
function showNotice(id,text,visible=!!text){
 const wrapper=document.getElementById(id+'-wrap');
 // Keep the old copy during collapse so the height can animate all the way to zero.
 if(visible)updateText(id,text);
 wrapper.classList.toggle('is-open',visible);
 const hidden=String(!visible);if(wrapper.getAttribute('aria-hidden')!==hidden)wrapper.setAttribute('aria-hidden',hidden);
}
function send(payload){if(socket?.readyState===1)socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload}));}
function connectElgatoStreamDeckSocket(port,uuid,registerEvent,info,actionInfo){
 const data=JSON.parse(actionInfo);context=data.context;action=data.action;inspectorId=uuid;settings=data.payload.settings||{};
 document.getElementById('request-settings').hidden=action!=='com.projectdeck.trula.requests';
 document.getElementById('connection').hidden=action!=='com.projectdeck.trula.connect';
 document.getElementById('status').hidden=['com.projectdeck.trula.connect','com.projectdeck.trula.chat','com.projectdeck.trula.requests'].includes(action);
 document.getElementById('chat-settings').hidden=action!=='com.projectdeck.trula.chat';
 document.getElementById('music-settings').hidden=action!=='com.projectdeck.trula.playback';
 document.getElementById('levels-settings').hidden=!['com.projectdeck.trula.streamlevels','com.projectdeck.trula.monitorlevels'].includes(action);
 document.getElementById('step-settings').hidden=action!=='com.projectdeck.trula.volumestep';
 loadChatSettings();
 socket=new WebSocket('ws://127.0.0.1:'+port);
 socket.onopen=()=>{socket.send(JSON.stringify({event:registerEvent,uuid}));setTimeout(()=>send({getConnection:true,settings}),200);};
 socket.onclose=()=>{updateStatus('StreamDock отключён.');renderChatStatus({connected:false,trulaReady:false,twitchConnected:false,message:'StreamDock отключён.'});};
 socket.onmessage=e=>{const m=JSON.parse(e.data);if(m.event==='didReceiveSettings'){settings=m.payload.settings||{};loadChatSettings();}if(m.event==='sendToPropertyInspector'){renderRequests(m.payload);if(m.payload.obsLaunchMessage)showNotice('obs-launch-feedback',m.payload.obsLaunchMessage);if(m.payload.message)updateStatus(m.payload.message);if(typeof m.payload.connected==='boolean')renderTrulaAccount(m.payload);if(typeof m.payload.twitchConnected==='boolean'){document.getElementById('twitch-disconnect').hidden=!m.payload.twitchConnected;renderTwitchAccount(m.payload);}renderChatStatus(m.payload);}};
}
function connectSocket(...args){connectElgatoStreamDeckSocket(...args);}
function request(command,message){
 if(socket?.readyState!==1){updateStatus('Нет связи со StreamDock.');return;}
 updateStatus(message);
 // StreamDock reliably forwards settings changes; its custom inspector messages
 // are not forwarded in all versions. Store only an expiring UI command, no secrets.
 const uiRequest={id:Date.now()+'-'+Math.random().toString(36).slice(2),command,created:Date.now()};
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:{...settings,uiRequest}}));
}
document.getElementById('connect').onclick=()=>request('openConnection','Открываю окно подключения…');
document.getElementById('obs').onclick=()=>{showNotice('obs-launch-feedback','Проверяю запуск OBS…');request('startObs','Проверяю запуск OBS…');};
document.getElementById('reset').onclick=()=>request('resetConnection','Сбрасываю подключение…');
function loadChatSettings(){renderDescription();renderRequests({});document.getElementById('message-format').value=settings.messageFormat||'both';document.getElementById('music-command').value=settings.musicCommand||'togglePlay';
 document.getElementById('first-level').value=settings.firstLevel??50;document.getElementById('second-level').value=settings.secondLevel??10;
 document.getElementById('volume-output').value=settings.volumeOutput||'source';document.getElementById('volume-direction').value=settings.direction||'up';document.getElementById('volume-step').value=settings.step??5;
}
document.getElementById('twitch-connect').onclick=()=>request('openTwitch','Открываю вход Twitch…');
document.getElementById('twitch-disconnect').onclick=()=>request('disconnectTwitch','Отключаю Twitch…');
document.getElementById('message-format').onchange=()=>{
 if(socket?.readyState!==1)return;
 settings={...settings,messageFormat:document.getElementById('message-format').value};delete settings.uiRequest;delete settings.channel;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));
 updateStatus('Настройки сообщения сохранены.');
};
document.getElementById('music-command').onchange=()=>{
 if(socket?.readyState!==1)return;
 settings={...settings,musicCommand:document.getElementById('music-command').value};delete settings.uiRequest;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));
 renderDescription();updateStatus('Действие сохранено.');
};

function renderTwitchAccount(payload){
 const connected=payload.twitchConnected;
 const extraAccess=connected&&payload.twitchRequestsAllowed===false;
 document.getElementById('twitch-connect').hidden=connected&&!extraAccess;
 updateText('twitch-connect',extraAccess?'Разрешить заявки':'Подключить Twitch');
 const login=(payload.twitchAccount||'').replace(/^Twitch:\s*/,'');
 updateText('twitch-name',connected?login:'Аккаунт Twitch');
 const badge=document.getElementById('twitch-badge');
 badge.hidden=false;
 updateText(badge,connected?'Twitch подключён: '+login:'Twitch отключён');
 updateClass(badge,'service-status'+(connected?'':' disconnected'));
}

function renderTrulaAccount(payload){
 const saved=payload.connected,ready=saved&&payload.trulaReady;
 const badge=document.getElementById('trula-badge');
 updateText(badge,ready?'Trula подключена':saved?'Trula: нет связи с OBS':'Trula отключена');
 updateClass(badge,'service-status'+(ready?'':' disconnected'));
 updateText('connect',saved?'Изменить':'Подключить Trula');
 document.getElementById('reset').hidden=!saved;
 const status=document.getElementById('status');
 updateClass(status,ready?'':'disconnected');
}

const chatConnection={};
function saveVolumeSettings(){
 if(socket?.readyState!==1)return;
 const first=Number(document.getElementById('first-level').value),second=Number(document.getElementById('second-level').value),step=Number(document.getElementById('volume-step').value);
 const levels=!document.getElementById('levels-settings').hidden;
 if(levels && (['first-level','second-level'].some(id=>document.getElementById(id).value==='') || ![first,second].every(v=>Number.isInteger(v)&&v>=0&&v<=100)||first===second)){updateStatus('Задайте два разных целых уровня от 0 до 100%.');return;}
 if(!levels && (document.getElementById('volume-step').value==='' || !Number.isInteger(step)||step<1||step>100)){updateStatus('Шаг должен быть от 1 до 100%.');return;}
 settings=levels?{...settings,firstLevel:first,secondLevel:second}:{...settings,volumeOutput:document.getElementById('volume-output').value,direction:document.getElementById('volume-direction').value,step};delete settings.uiRequest;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));renderDescription();updateStatus('Настройки громкости сохранены.');
}
for(const id of ['first-level','second-level','volume-output','volume-direction','volume-step'])document.getElementById(id).onchange=saveVolumeSettings;
function renderChatStatus(payload){
 Object.assign(chatConnection,payload);
 const trula=!!(chatConnection.connected&&chatConnection.trulaReady),twitch=chatConnection.twitchConnected===true;
 const trulaStatus=document.getElementById('chat-trula-status');
 updateBadge(trulaStatus,trula?'Подключена':chatConnection.connected?'Нет связи с OBS':'Отключена',trula?'connected':'disconnected');
 const login=(chatConnection.twitchAccount||'').replace(/^Twitch:\s*/,'');
 const twitchStatus=document.getElementById('chat-twitch-status');
 updateBadge(twitchStatus,twitch?'Подключён: '+login:'Отключён',twitch?'connected':'disconnected');
 const generic=['Подключено к Trula в OBS.','Trula подключена к OBS.','Подключение Trula сохранено.'];
 const message=payload.message||'';
 showNotice('chat-feedback',message,!!message&&!generic.includes(message));
}

function renderDescription(){
 const kind=(action||'').split('.').pop();
 const descriptions={
  playback:({togglePlay:'Приостанавливает музыку в панели Trula в OBS. Повторное нажатие продолжает воспроизведение.',nextTrack:'Переключает музыку на следующий трек в панели Trula в OBS.',skipOrder:'[Бета].Удаляет текущий заказ из очереди Trula и включает следующий трек. Баллы канала не возвращаются. Если играет плейлист, очередь не меняется.',prevTrack:'Переключает музыку на предыдущий трек в панели Trula в OBS.'})[settings.musicCommand||'togglePlay'],
  stream:'Выключает музыку Trula на стриме. Повторное нажатие возвращает прежнюю громкость. Звук у вас не меняется.',
  monitor:'Выключает музыку в вашей панели Trula. Повторное нажатие возвращает прежнюю громкость. Звук на стриме не меняется.',
  streamlevels:'Переключает громкость музыки на стриме между уровнями 1 и 2. Если текущая громкость отличается от уровня 1, нажатие устанавливает уровень 1. Звук у вас не меняется.',
  monitorlevels:'Переключает громкость музыки у вас между уровнями 1 и 2. Если текущая громкость отличается от уровня 1, нажатие устанавливает уровень 1. Звук на стриме не меняется.',
  volumestep:'Каждое нажатие увеличивает или уменьшает громкость музыки выбранного выхода на заданный шаг. Границы — 0–100%; на кнопке отображается MIN или MAX при их достижении.',
  requests:'[Бета]. По нажатию добавляет последнюю заявку выбранной музыкальной награды Twitch в очередь следующих треков. Текущий трек не прерывается, плейлисты не меняются. Используйте для отклонённых Trula заявок: статус их выполнения в Twitch не меняется. Учитываются заявки после запуска плагина, не старше 30 минут.',
  chat:'Отправляет текущий трек в чат вашего авторизованного аккаунта Twitch. Выберите название, ссылку или оба варианта. Если данных нет, в сообщение добавляется пояснение.',
  next:'Включает следующий трек в панели Trula в OBS.',
  previous:'Включает предыдущий трек в панели Trula в OBS.',
  control:'Выполняет выбранную команду управления Trula.'
 };
 const text=descriptions[kind];document.getElementById('action-description').hidden=kind==='connect'||!text;
 updateText('action-description-text',text||'');
}

let requestRewardCache=[];
const requestConnection={};
function renderRequests(payload){
 Object.assign(requestConnection,payload);
 if(Array.isArray(payload.rewards))requestRewardCache=payload.rewards;
 const select=document.getElementById('request-reward'),signature=JSON.stringify([requestRewardCache,settings.rewardId||'']);
 if(select.dataset.signature!==signature){
  select.replaceChildren();const placeholder=document.createElement('option');placeholder.value='';placeholder.textContent='Выберите музыкальную награду';select.append(placeholder);
  for(const reward of requestRewardCache){const option=document.createElement('option');option.value=reward.id;option.textContent=reward.title;select.append(option);}
  if(settings.rewardId&&!requestRewardCache.some(x=>x.id===settings.rewardId)){const saved=document.createElement('option');saved.value=settings.rewardId;saved.textContent='Сохранённая награда (ожидание Twitch)';select.append(saved);}
  select.value=settings.rewardId||'';select.dataset.signature=signature;
 }
 const current=requestConnection,trula=!!(current.connected&&current.trulaReady),ready=current.requestReady===true;
 const trulaBadge=document.getElementById('request-trula-status');updateBadge(trulaBadge,trula?'Подключена':current.connected?'Нет связи с OBS':'Отключена',trula?'connected':'disconnected');
 const twitchBadge=document.getElementById('request-twitch-status');
 const waiting=!ready&&['Подключение к заявкам','Переподключение к заявкам'].some(x=>(current.requestStatus||'').startsWith(x));
 updateBadge(twitchBadge,ready?'Подключены':!current.twitchConnected?'Не подключён':!current.twitchRequestsAllowed?'Нужно разрешение':waiting?'Подключение…':'Недоступны',ready?'connected':waiting?'waiting':'disconnected');
 showNotice('request-status',ready?'':current.requestStatus||'');
 const message=payload.message||'',generic=['Подключено к Trula в OBS.','Trula подключена к OBS.','Подключение Trula сохранено.'];
 if(message&&!generic.includes(message))showNotice('request-feedback',message);

 if(typeof payload.requestDetail==='string')showNotice('request-detail',payload.requestDetail,!!payload.requestDetail&&payload.requestDetail!==payload.requestStatus);
}
document.getElementById('request-reward').onchange=()=>{
 if(socket?.readyState!==1)return;
 settings={...settings,rewardId:document.getElementById('request-reward').value};delete settings.uiRequest;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));
};

function updateBadge(element,text,state){updateText(element,text);updateClass(element,'chat-service-value '+state);}
