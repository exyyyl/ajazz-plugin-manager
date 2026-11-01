function normalizeEvent(message) {
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
