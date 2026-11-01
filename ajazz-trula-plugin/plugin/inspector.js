'use strict';
let socket, context, settings = {}, action;
const ids = ['token', 'command', 'output', 'media', 'step'];
function form() {
  document.getElementById('audio').hidden = !['mute','volumeUp','volumeDown'].includes(document.getElementById('command').value);
}
function load(value) {
  settings = value || {};
  for (const id of ids) if (settings[id] !== undefined) document.getElementById(id).value = settings[id];
  form();
}
function connectElgatoStreamDeckSocket(port, uuid, registerEvent, info, actionInfo) {
  const data = JSON.parse(actionInfo); context = data.context; action = data.action; load(data.payload.settings);
  document.getElementById('advanced').hidden = action !== 'com.projectdeck.trula.control';
  socket = new WebSocket('ws://127.0.0.1:' + port);
  socket.onopen = () => { socket.send(JSON.stringify({event:registerEvent, uuid})); socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload:{getConnection:true}})); };
  socket.onmessage = e => {
    const m = JSON.parse(e.data);
    if (m.event === 'didReceiveSettings') load(m.payload.settings);
    if (m.event === 'sendToPropertyInspector') {
      document.getElementById('status').textContent = m.payload.message;
      if (m.payload.saved) { document.getElementById('token').value = ''; socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload:{getConnection:true}})); }
    }
  };
}
document.getElementById('command').onchange = form;
document.getElementById('token').oninput = form;
document.getElementById('save').onclick = () => {
  if (!socket || socket.readyState !== 1) return;
  const connection = document.getElementById('token').value;
  for (const id of ids.filter(id=>id !== 'token')) settings[id] = document.getElementById(id).value;
  delete settings.token;
  // Normalize the panel URL before saving: keep only the credential, never arbitrary URLs.
  if (connection) socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload:{saveConnection:true,connection}}));
  socket.send(JSON.stringify({event:'setSettings', context, payload:settings}));
  if (!connection) socket.send(JSON.stringify({event:'sendToPlugin', action, context, payload:{check:true, settings}}));
  document.getElementById('status').textContent = 'Проверка подключения…';
};
// Compatibility alias used by some Stream Dock releases.
function connectSocket(...args) { connectElgatoStreamDeckSocket(...args); }
form();

document.getElementById('player').onclick = () => socket?.readyState===1 && socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload:{showPlayer:true}}));
document.getElementById('execute').onclick = () => socket?.readyState===1 && socket.send(JSON.stringify({event:'sendToPlugin',action,context,payload:{execute:true,settings}}));
