const { test } = require('node:test');
const assert = require('node:assert/strict');
const { normalizeEvent } = require('./compatibility.cjs');
test('normal keypad presses toggle instead of forcing the state supplied by AJAZZ', () => {
  for (const event of ['keyDown', 'keyUp']) {
    const message = { event, action: 'com.elgato.discord.mute', payload: { controller: '', isInMultiAction: false, state: 0, userDesiredState: 1, settings: { example: true } } };
    normalizeEvent(message);
    assert.equal(message.payload.controller, 'Keypad');
    assert.equal('userDesiredState' in message.payload, false);
    assert.equal(message.payload.state, 0);
    assert.deepEqual(message.payload.settings, { example: true });
  }
});
test('explicit multi action state and property inspector data are preserved', () => {
  for (const message of [
    { event: 'keyDown', action: 'com.elgato.discord.mute', payload: { isInMultiAction: true, userDesiredState: 1 } },
    { event: 'sendToPlugin', action: 'com.elgato.discord.mute', payload: { grantAccess: true } },
    { event: 'keyDown', action: 'other.plugin', payload: { isInMultiAction: false, userDesiredState: 1 } },
  ]) {
    const original = structuredClone(message);
    normalizeEvent(message);
    if (message.payload.isInMultiAction === true) delete message.payload.controller;
    assert.deepEqual(message, original);
  }
});
