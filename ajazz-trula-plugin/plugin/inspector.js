'use strict';
let socket,context,action,inspectorId,settings={};
function send(payload){if(socket?.readyState===1)socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload}));}
function connectElgatoStreamDeckSocket(port,uuid,registerEvent,info,actionInfo){
 const data=JSON.parse(actionInfo);context=data.context;action=data.action;inspectorId=uuid;settings=data.payload.settings||{};
 document.getElementById('connection').hidden=action!=='com.projectdeck.trula.connect';
 document.getElementById('status').hidden=['com.projectdeck.trula.connect','com.projectdeck.trula.chat'].includes(action);
 document.getElementById('chat-settings').hidden=action!=='com.projectdeck.trula.chat';
 document.getElementById('music-settings').hidden=action!=='com.projectdeck.trula.playback';
 document.getElementById('levels-settings').hidden=!['com.projectdeck.trula.streamlevels','com.projectdeck.trula.monitorlevels'].includes(action);
 document.getElementById('step-settings').hidden=action!=='com.projectdeck.trula.volumestep';
 loadChatSettings();
 socket=new WebSocket('ws://127.0.0.1:'+port);
 socket.onopen=()=>{socket.send(JSON.stringify({event:registerEvent,uuid}));setTimeout(()=>send({getConnection:true,settings}),200);};
 socket.onclose=()=>{document.getElementById('status').textContent='StreamDock отключён.';renderChatStatus({connected:false,trulaReady:false,twitchConnected:false,message:'StreamDock отключён.'});};
 socket.onmessage=e=>{const m=JSON.parse(e.data);if(m.event==='didReceiveSettings'){settings=m.payload.settings||{};loadChatSettings();}if(m.event==='sendToPropertyInspector'){if(m.payload.obsLaunchMessage){const feedback=document.getElementById('obs-launch-feedback');feedback.hidden=false;feedback.textContent=m.payload.obsLaunchMessage;}if(m.payload.message)document.getElementById('status').textContent=m.payload.message;if(typeof m.payload.connected==='boolean')renderTrulaAccount(m.payload);if(typeof m.payload.twitchConnected==='boolean'){document.getElementById('twitch-connect').hidden=m.payload.twitchConnected;document.getElementById('twitch-disconnect').hidden=!m.payload.twitchConnected;renderTwitchAccount(m.payload);}renderChatStatus(m.payload);}};
}
function connectSocket(...args){connectElgatoStreamDeckSocket(...args);}
function request(command,message){
 if(socket?.readyState!==1){document.getElementById('status').textContent='Нет связи со StreamDock.';return;}
 document.getElementById('status').textContent=message;
 // StreamDock reliably forwards settings changes; its custom inspector messages
 // are not forwarded in all versions. Store only an expiring UI command, no secrets.
 const uiRequest={id:Date.now()+'-'+Math.random().toString(36).slice(2),command,created:Date.now()};
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:{...settings,uiRequest}}));
}
document.getElementById('connect').onclick=()=>request('openConnection','Открываю окно подключения…');
document.getElementById('obs').onclick=()=>{const feedback=document.getElementById('obs-launch-feedback');feedback.hidden=false;feedback.textContent='Проверяю запуск OBS…';request('startObs','Проверяю запуск OBS…');};
document.getElementById('reset').onclick=()=>request('resetConnection','Сбрасываю подключение…');
function loadChatSettings(){renderDescription();document.getElementById('message-format').value=settings.messageFormat||'both';document.getElementById('music-command').value=settings.musicCommand||'togglePlay';
 document.getElementById('first-level').value=settings.firstLevel??50;document.getElementById('second-level').value=settings.secondLevel??10;
 document.getElementById('volume-output').value=settings.volumeOutput||'source';document.getElementById('volume-direction').value=settings.direction||'up';document.getElementById('volume-step').value=settings.step??5;
}
document.getElementById('twitch-connect').onclick=()=>request('openTwitch','Открываю вход Twitch…');
document.getElementById('twitch-disconnect').onclick=()=>request('disconnectTwitch','Отключаю Twitch…');
document.getElementById('message-format').onchange=()=>{
 if(socket?.readyState!==1)return;
 settings={...settings,messageFormat:document.getElementById('message-format').value};delete settings.uiRequest;delete settings.channel;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));
 document.getElementById('status').textContent='Настройки сообщения сохранены.';
};
document.getElementById('music-command').onchange=()=>{
 if(socket?.readyState!==1)return;
 settings={...settings,musicCommand:document.getElementById('music-command').value};delete settings.uiRequest;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));
 renderDescription();document.getElementById('status').textContent='Действие сохранено.';
};

function renderTwitchAccount(payload){
 const connected=payload.twitchConnected;
 const login=(payload.twitchAccount||'').replace(/^Twitch:\s*/,'');
 document.getElementById('twitch-name').textContent=connected?login:'Аккаунт Twitch';
 const badge=document.getElementById('twitch-badge');
 badge.hidden=false;
 badge.textContent=connected?'Twitch подключён: '+login:'Twitch отключён';
 badge.className='service-status'+(connected?'':' disconnected');
}

function renderTrulaAccount(payload){
 const saved=payload.connected,ready=saved&&payload.trulaReady;
 const badge=document.getElementById('trula-badge');
 badge.textContent=ready?'Trula подключена':saved?'Trula: нет связи с OBS':'Trula отключена';
 badge.className='service-status'+(ready?'':' disconnected');
 document.getElementById('connect').textContent=saved?'Изменить':'Подключить Trula';
 document.getElementById('reset').hidden=!saved;
 const status=document.getElementById('status');
 status.className=ready?'':'disconnected';
 if(['Подключено к Trula в OBS.','Trula подключена к OBS.'].includes(status.textContent))status.textContent='Trula подключена';
}

const chatConnection={};
function saveVolumeSettings(){
 if(socket?.readyState!==1)return;
 const first=Number(document.getElementById('first-level').value),second=Number(document.getElementById('second-level').value),step=Number(document.getElementById('volume-step').value);
 const levels=!document.getElementById('levels-settings').hidden;
 if(levels && (['first-level','second-level'].some(id=>document.getElementById(id).value==='') || ![first,second].every(v=>Number.isInteger(v)&&v>=0&&v<=100)||first===second)){document.getElementById('status').textContent='Задайте два разных целых уровня от 0 до 100%.';return;}
 if(!levels && (document.getElementById('volume-step').value==='' || !Number.isInteger(step)||step<1||step>100)){document.getElementById('status').textContent='Шаг должен быть от 1 до 100%.';return;}
 settings=levels?{...settings,firstLevel:first,secondLevel:second}:{...settings,volumeOutput:document.getElementById('volume-output').value,direction:document.getElementById('volume-direction').value,step};delete settings.uiRequest;
 socket.send(JSON.stringify({event:'setSettings',context:inspectorId,payload:settings}));renderDescription();document.getElementById('status').textContent='Настройки громкости сохранены.';
}
for(const id of ['first-level','second-level','volume-output','volume-direction','volume-step'])document.getElementById(id).onchange=saveVolumeSettings;
function renderChatStatus(payload){
 Object.assign(chatConnection,payload);
 const trula=!!(chatConnection.connected&&chatConnection.trulaReady),twitch=chatConnection.twitchConnected===true;
 const trulaStatus=document.getElementById('chat-trula-status');
 trulaStatus.textContent=trula?'Подключена':chatConnection.connected?'Нет связи с OBS':'Отключена';
 trulaStatus.className='chat-service-value '+(trula?'connected':'disconnected');
 const login=(chatConnection.twitchAccount||'').replace(/^Twitch:\s*/,'');
 const twitchStatus=document.getElementById('chat-twitch-status');
 twitchStatus.textContent=twitch?'Подключён: '+login:'Отключён';
 twitchStatus.className='chat-service-value '+(twitch?'connected':'disconnected');
 const generic=['Подключено к Trula в OBS.','Trula подключена к OBS.','Подключение Trula сохранено.'];
 const message=payload.message||'';
 const feedback=document.getElementById('chat-feedback');
 feedback.hidden=!message||generic.includes(message);
 feedback.textContent=feedback.hidden?'':message;
}

function renderDescription(){
 const kind=(action||'').split('.').pop();
 const descriptions={
  playback:({togglePlay:'Приостанавливает музыку в панели Trula в OBS. Повторное нажатие продолжает воспроизведение.',nextTrack:'Переключает музыку на следующий трек в панели Trula в OBS.',skipOrder:'Удаляет текущий заказ из очереди Trula и включает следующий трек. Баллы канала не возвращаются. Если играет плейлист, очередь не меняется.',prevTrack:'Переключает музыку на предыдущий трек в панели Trula в OBS.'})[settings.musicCommand||'togglePlay'],
  stream:'Выключает музыку Trula на стриме. Повторное нажатие возвращает прежнюю громкость. Звук у вас не меняется.',
  monitor:'Выключает музыку в вашей панели Trula. Повторное нажатие возвращает прежнюю громкость. Звук на стриме не меняется.',
  streamlevels:'Переключает громкость музыки на стриме между уровнями 1 и 2. Если текущая громкость отличается от уровня 1, нажатие устанавливает уровень 1. Звук у вас не меняется.',
  monitorlevels:'Переключает громкость музыки у вас между уровнями 1 и 2. Если текущая громкость отличается от уровня 1, нажатие устанавливает уровень 1. Звук на стриме не меняется.',
  volumestep:'Каждое нажатие увеличивает или уменьшает громкость музыки выбранного выхода на заданный шаг. Границы — 0–100%; на кнопке отображается MIN или MAX при их достижении.',
  chat:'Отправляет текущий трек в чат вашего авторизованного аккаунта Twitch. Выберите название, ссылку или оба варианта. Если данных нет, в сообщение добавляется пояснение.',
  next:'Включает следующий трек в панели Trula в OBS.',
  previous:'Включает предыдущий трек в панели Trula в OBS.',
  control:'Выполняет выбранную команду управления Trula.'
 };
 const text=descriptions[kind];document.getElementById('action-description').hidden=kind==='connect'||!text;
 document.getElementById('action-description-text').textContent=text||'';
}
