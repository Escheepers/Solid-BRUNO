import { ElementRef } from '@angular/core';

import { FocusTrap } from './focus-trap';

describe('FocusTrap', () => {
  let trigger: HTMLButtonElement;
  let panel: HTMLElement;
  let first: HTMLButtonElement;
  let middle: HTMLInputElement;
  let last: HTMLButtonElement;
  let focusTrap: FocusTrap;

  beforeEach(() => {
    document.body.innerHTML = '';

    trigger = document.createElement('button');
    trigger.textContent = 'Trigger';
    document.body.appendChild(trigger);

    panel = document.createElement('div');
    first = document.createElement('button');
    first.textContent = 'First';
    middle = document.createElement('input');
    last = document.createElement('button');
    last.textContent = 'Last';
    panel.append(first, middle, last);
    document.body.appendChild(panel);

    focusTrap = new FocusTrap(new ElementRef(panel));
  });

  afterEach(() => {
    document.body.innerHTML = '';
  });

  it('captures the currently focused element as the trigger and moves focus to the first focusable element on activate', () => {
    trigger.focus();
    expect(document.activeElement).toBe(trigger);

    focusTrap.activate();

    expect(document.activeElement).toBe(first);
  });

  it('focuses the panel itself on activate when it has no focusable descendants', () => {
    const emptyPanel = document.createElement('div');
    emptyPanel.tabIndex = -1;
    document.body.appendChild(emptyPanel);
    const emptyTrap = new FocusTrap(new ElementRef(emptyPanel));

    trigger.focus();
    emptyTrap.activate();

    expect(document.activeElement).toBe(emptyPanel);
  });

  it('returns focus to the captured trigger element on deactivate', () => {
    trigger.focus();
    focusTrap.activate();
    expect(document.activeElement).not.toBe(trigger);

    focusTrap.deactivate();

    expect(document.activeElement).toBe(trigger);
  });

  it('clears the captured trigger after deactivate, so a later deactivate call is a no-op', () => {
    trigger.focus();
    focusTrap.activate();
    focusTrap.deactivate();

    first.focus();
    focusTrap.deactivate();

    expect(document.activeElement).toBe(first);
  });

  it('does not throw when deactivate is called before activate', () => {
    expect(() => focusTrap.deactivate()).not.toThrow();
  });

  it('cycles Tab from the last focusable element back to the first, preventing default', () => {
    last.focus();
    const event = new KeyboardEvent('keydown', { key: 'Tab', cancelable: true });
    const preventSpy = vi.spyOn(event, 'preventDefault');

    focusTrap.handleTabKey(event);

    expect(preventSpy).toHaveBeenCalled();
    expect(document.activeElement).toBe(first);
  });

  it('cycles Shift+Tab from the first focusable element back to the last, preventing default', () => {
    first.focus();
    const event = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: true, cancelable: true });
    const preventSpy = vi.spyOn(event, 'preventDefault');

    focusTrap.handleTabKey(event);

    expect(preventSpy).toHaveBeenCalled();
    expect(document.activeElement).toBe(last);
  });

  it('does not interfere with Tab on a middle element', () => {
    middle.focus();
    const event = new KeyboardEvent('keydown', { key: 'Tab', cancelable: true });
    const preventSpy = vi.spyOn(event, 'preventDefault');

    focusTrap.handleTabKey(event);

    expect(preventSpy).not.toHaveBeenCalled();
    expect(document.activeElement).toBe(middle);
  });

  it('prevents default on Tab when the panel has no focusable elements', () => {
    const emptyPanel = document.createElement('div');
    document.body.appendChild(emptyPanel);
    const emptyTrap = new FocusTrap(new ElementRef(emptyPanel));

    const event = new KeyboardEvent('keydown', { key: 'Tab', cancelable: true });
    const preventSpy = vi.spyOn(event, 'preventDefault');

    emptyTrap.handleTabKey(event);

    expect(preventSpy).toHaveBeenCalled();
  });
});
