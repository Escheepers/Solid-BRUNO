import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ConfirmDialog } from './confirm-dialog';

@Component({
  selector: 'app-confirm-dialog-test-host',
  imports: [ConfirmDialog],
  template: `
    <button type="button" data-testid="trigger" (click)="open = true">Open</button>
    <app-confirm-dialog
      [open]="open"
      title="Discard changes?"
      message="You have unsaved changes. Discard them?"
      confirmLabel="Discard"
      cancelLabel="Keep editing"
      (confirm)="onConfirm()"
      (cancel)="onCancel()"
    />
  `,
})
class TestHost {
  open = false;
  confirmed = false;
  cancelled = false;

  onConfirm(): void {
    this.confirmed = true;
    this.open = false;
  }

  onCancel(): void {
    this.cancelled = true;
    this.open = false;
  }
}

describe('ConfirmDialog', () => {
  let fixture: ComponentFixture<TestHost>;
  let host: TestHost;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    host = fixture.componentInstance;
  });

  function dialog(): HTMLElement | null {
    return fixture.nativeElement.querySelector('[role="alertdialog"]');
  }

  function trigger(): HTMLButtonElement {
    return fixture.nativeElement.querySelector('[data-testid="trigger"]');
  }

  function buttonNamed(label: string): HTMLButtonElement | undefined {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === label,
    );
  }

  it('renders nothing when open is false', () => {
    fixture.detectChanges();
    expect(dialog()).toBeNull();
  });

  it('renders the title, message, and role="alertdialog"/aria-modal when open is true', () => {
    host.open = true;
    fixture.detectChanges();

    const panel = dialog();
    expect(panel).not.toBeNull();
    expect(panel!.getAttribute('aria-modal')).toBe('true');
    expect(fixture.nativeElement.textContent).toContain('Discard changes?');
    expect(fixture.nativeElement.textContent).toContain('You have unsaved changes');
  });

  it('clicking the confirm button emits confirm', () => {
    host.open = true;
    fixture.detectChanges();

    buttonNamed('Discard')!.click();
    fixture.detectChanges();

    expect(host.confirmed).toBe(true);
  });

  it('clicking the cancel button emits cancel', () => {
    host.open = true;
    fixture.detectChanges();

    buttonNamed('Keep editing')!.click();
    fixture.detectChanges();

    expect(host.cancelled).toBe(true);
  });

  it('Escape emits cancel and stops the event from propagating further', () => {
    host.open = true;
    fixture.detectChanges();

    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    const stopPropagationSpy = vi.spyOn(event, 'stopPropagation');
    dialog()!.dispatchEvent(event);
    fixture.detectChanges();

    expect(host.cancelled).toBe(true);
    expect(stopPropagationSpy).toHaveBeenCalled();
  });

  it('moves focus into the dialog on open', () => {
    host.open = true;
    fixture.detectChanges();

    expect(document.activeElement?.textContent?.trim()).toBe('Keep editing');
  });

  it('returns focus to the triggering element on close', () => {
    fixture.detectChanges();
    trigger().focus();
    trigger().click();
    fixture.detectChanges();

    expect(document.activeElement).not.toBe(trigger());

    buttonNamed('Keep editing')!.click();
    fixture.detectChanges();

    expect(document.activeElement).toBe(trigger());
  });

  it('traps Tab within the dialog, cycling from the last focusable element back to the first', () => {
    host.open = true;
    fixture.detectChanges();

    const focusable: HTMLElement[] = Array.from(
      fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
    );
    const last = focusable[focusable.length - 1];
    last.focus();

    const event = new KeyboardEvent('keydown', { key: 'Tab', bubbles: true, cancelable: true });
    dialog()!.dispatchEvent(event);

    expect(document.activeElement).toBe(focusable[0]);
  });
});
