import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { Modal } from './modal';

@Component({
  selector: 'app-modal-test-host',
  imports: [Modal],
  template: `
    <button type="button" data-testid="trigger" (click)="open = true">Open</button>
    <app-modal [open]="open" title="Test Modal" (closeRequest)="open = false">
      <input data-testid="first-field" placeholder="First field" />
      <input data-testid="second-field" placeholder="Second field" />
    </app-modal>
  `,
})
class TestHost {
  open = false;
}

describe('Modal', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
  });

  function dialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="dialog"]');
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="trigger"]');
  }

  it('renders nothing when open is false', () => {
    fixture.detectChanges();
    expect(dialog()).toBeNull();
  });

  it('renders the projected content and role="dialog"/aria-modal when open is true', () => {
    host.open = true;
    fixture.detectChanges();

    const panel = dialog();
    expect(panel).not.toBeNull();
    expect(panel!.getAttribute('aria-modal')).toBe('true');
    expect(fixture.nativeElement.querySelector('[data-testid="first-field"]')).not.toBeNull();
  });

  it('emits closeRequest when Escape is pressed', () => {
    host.open = true;
    fixture.detectChanges();

    dialog()!.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(dialog()).toBeNull();
  });

  it('emits closeRequest when the backdrop is clicked', () => {
    host.open = true;
    fixture.detectChanges();

    const backdrop = fixture.nativeElement.querySelector('[data-testid="modal-backdrop"]');
    backdrop.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(dialog()).toBeNull();
  });

  it('does not close when a click occurs inside the panel', () => {
    host.open = true;
    fixture.detectChanges();

    dialog()!.dispatchEvent(new MouseEvent('click', { bubbles: true }));
    fixture.detectChanges();

    expect(dialog()).not.toBeNull();
  });

  it('emits closeRequest when the close (x) button is clicked', () => {
    host.open = true;
    fixture.detectChanges();

    fixture.nativeElement.querySelector('button[aria-label="Close"]').click();
    fixture.detectChanges();

    expect(dialog()).toBeNull();
  });

  it('moves focus into the modal on open', () => {
    host.open = true;
    fixture.detectChanges();

    expect(document.activeElement?.getAttribute('aria-label')).toBe('Close');
  });

  it('returns focus to the triggering element on close', () => {
    fixture.detectChanges();
    trigger().focus();
    trigger().click();
    fixture.detectChanges();

    expect(document.activeElement).not.toBe(trigger());

    fixture.nativeElement.querySelector('button[aria-label="Close"]').click();
    fixture.detectChanges();

    expect(document.activeElement).toBe(trigger());
  });

  it('traps Tab within the modal, cycling from the last focusable element back to the first', () => {
    host.open = true;
    fixture.detectChanges();

    const focusable: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('[role="dialog"] button, [role="dialog"] input'),
    );
    const last = focusable[focusable.length - 1];
    last.focus();

    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    dialog()!.dispatchEvent(event);

    expect(document.activeElement).toBe(focusable[0]);
  });

  it('traps Shift+Tab within the modal, cycling from the first focusable element back to the last', () => {
    host.open = true;
    fixture.detectChanges();

    const focusable: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('[role="dialog"] button, [role="dialog"] input'),
    );
    focusable[0].focus();

    const event = new KeyboardEvent('keydown', {
      key: 'Tab',
      shiftKey: true,
      bubbles: true,
      cancelable: true,
    });
    dialog()!.dispatchEvent(event);

    expect(document.activeElement).toBe(focusable[focusable.length - 1]);
  });
});
