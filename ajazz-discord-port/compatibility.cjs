function normalizeEvent(message, pluginUUID) {
  if (message.event === 'didReceiveGlobalSettings' && pluginUUID && !message.context) message.context = pluginUUID;
  if (!message.action?.startsWith('com.elgato.discord.') || !message.payload) return message;
  if (['willAppear', 'willDisappear', 'keyDown', 'keyUp', 'didReceiveSettings'].includes(message.event) && !message.payload.controller) {
    message.payload.controller = 'Keypad';
  }
  if (['keyDown', 'keyUp'].includes(message.event) && message.payload.isInMultiAction === false) {
    delete message.payload.userDesiredState;
  }
  return message;
}
module.exports = { normalizeEvent };
