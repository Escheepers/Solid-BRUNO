import { Component, ElementRef, ViewChild } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ConfirmDialog } from '../confirm-dialog/confirm-dialog';
import { Modal } from '../modal/modal';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * Mirrors the exact "discard-guard" composition every real form Modal in the app
 * uses (`VehicleFormModal`, `CustomerFormModal`, `BookingFormModal` -- each of
 * their own doc comments says so verbatim: "mirrors ...'s exact discard-guard
 * composition"): a `Modal` whose `closeRequest` is intercepted -- an untouched
 * form closes immediately, a dirty one shows a nested `ConfirmDialog` instead,
 * and cancelling that dialog leaves the `Modal` open with focus back on the
 * exact field the user was on (never re-trapped to the `Modal`'s first field).
 */
@Component({
  selector: 'app-discard-guard-sweep-host',
  imports: [Modal, ConfirmDialog],
  template: `
    <button type="button" data-testid="trigger" (click)="open = true">Open</button>
    <app-modal [open]="open" title="Edit thing" (closeRequest)="onModalCloseRequest()">
      <input data-testid="field-a" placeholder="Field A" (input)="dirty = true" />
      <input data-testid="field-b" placeholder="Field B" (input)="dirty = true" />
      <app-confirm-dialog
        [open]="showDiscardConfirm"
        title="Discard changes?"
        message="You have unsaved changes. Discard them?"
        confirmLabel="Discard"
        cancelLabel="Keep editing"
        (confirm)="onConfirmDiscard()"
        (cancel)="onCancelDiscard()"
      />
    </app-modal>
  `,
})
class DiscardGuardSweepHost {
  open = false;
  dirty = false;
  showDiscardConfirm = false;

  onModalCloseRequest(): void {
    if (this.dirty) {
      this.showDiscardConfirm = true;
      return;
    }
    this.resetAndClose();
  }

  onConfirmDiscard(): void {
    this.showDiscardConfirm = false;
    this.resetAndClose();
  }

  onCancelDiscard(): void {
    this.showDiscardConfirm = false;
  }

  private resetAndClose(): void {
    this.dirty = false;
    this.open = false;
  }
}

/**
 * Mirrors `BookingFormModal`'s exact Modal-over-Modal composition: an outer
 * `Modal` containing a picker field and a button that opens a second, inner
 * `Modal` on top of it. On close, the inner `Modal`'s own `FocusTrap` would by
 * default return focus to that trigger button -- the host below overrides this
 * (mirroring `BookingFormModal.focusCustomerSelect`'s exact technique) to prove
 * the override mechanism itself reliably wins the race against `FocusTrap`'s
 * default deactivation, independent of any one feature's wiring.
 */
@Component({
  selector: 'app-nested-modal-override-sweep-host',
  imports: [Modal],
  template: `
    <app-modal [open]="outerOpen" title="Outer" (closeRequest)="outerOpen = false">
      <select data-testid="picker" #picker>
        <option value="">Choose…</option>
      </select>
      <button type="button" data-testid="open-inner" (click)="innerOpen = true">+ New</button>
      <app-modal [open]="innerOpen" title="Inner" (closeRequest)="onInnerClose()">
        <input data-testid="inner-field" placeholder="Inner field" />
      </app-modal>
    </app-modal>
  `,
})
class NestedModalOverrideSweepHost {
  @ViewChild('picker') private readonly pickerRef?: ElementRef<HTMLSelectElement>;

  outerOpen = false;
  innerOpen = false;

  onInnerClose(): void {
    this.innerOpen = false;
    setTimeout(() => this.pickerRef?.nativeElement.focus());
  }
}

/**
 * Consolidated focus-trap-and-return sweep (spec-6-4's Accessibility
 * Verification Pass) over the two composition *shapes* every real Modal/
 * ConfirmDialog usage across Epics 1-5 is built from -- `Modal`/`ConfirmDialog`
 * themselves already have full unit coverage of the base primitive (trap, open-
 * focus, close-return, Escape/backdrop) in `modal.spec.ts`/`confirm-dialog.spec.ts`,
 * including a plain nested-Modal-over-Modal case. What's covered only here is the
 * two *compositions* layered on top of those primitives: the discard-guard
 * (`Modal` + nested `ConfirmDialog`, used identically by `VehicleFormModal`/
 * `CustomerFormModal`/`BookingFormModal`) and the explicit-override nested-Modal
 * case (`BookingFormModal`'s "+ New Customer"). One sweep proves both compositions
 * once, rather than re-deriving the same assertions per feature (spec-6-4's
 * Boundaries: "not per-feature duplicate tests"). `BookingFormModal`'s own spec
 * additionally asserts this exact override is actually wired up in that real
 * component -- this file only proves the underlying mechanism is sound.
 */
describe('Focus trap-and-return sweep (Modal/ConfirmDialog compositions)', () => {
  describe('discard-guard composition (Modal + nested ConfirmDialog)', () => {
    let fixture: ComponentFixture<DiscardGuardSweepHost>;
    let host: DiscardGuardSweepHost;

    beforeEach(async () => {
      await TestBed.configureTestingModule({ imports: [DiscardGuardSweepHost] }).compileComponents();
      fixture = TestBed.createComponent(DiscardGuardSweepHost);
      host = fixture.componentInstance;
    });

    function trigger(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('[data-testid="trigger"]');
    }

    function fieldA(): HTMLInputElement {
      return fixture.nativeElement.querySelector('[data-testid="field-a"]');
    }

    function fieldB(): HTMLInputElement {
      return fixture.nativeElement.querySelector('[data-testid="field-b"]');
    }

    function modalDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="dialog"]');
    }

    function alertDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="alertdialog"]');
    }

    function escape(el: HTMLElement): void {
      el.dispatchEvent(
        new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
      );
    }

    it('traps focus in the Modal and returns it to the trigger on an untouched close', () => {
      fixture.detectChanges();
      trigger().focus();
      trigger().click();
      fixture.detectChanges();

      expect(modalDialog()).not.toBeNull();
      expect(document.activeElement).not.toBe(trigger());

      escape(modalDialog()!);
      fixture.detectChanges();

      expect(modalDialog()).toBeNull();
      expect(document.activeElement).toBe(trigger());
    });

    it('a dirty Modal shows the nested ConfirmDialog on Escape instead of closing, and traps focus in it', () => {
      fixture.detectChanges();
      trigger().click();
      fixture.detectChanges();

      fieldB().focus();
      fieldB().value = 'x';
      fieldB().dispatchEvent(new Event('input'));
      fixture.detectChanges();

      escape(modalDialog()!);
      fixture.detectChanges();

      expect(modalDialog()).not.toBeNull(); // Modal itself never closed.
      expect(alertDialog()).not.toBeNull();
      expect(document.activeElement).not.toBe(fieldB()); // focus moved into the dialog.
    });

    it('cancelling the discard ConfirmDialog returns focus to the exact field the user was on, not the Modal\'s first field', () => {
      fixture.detectChanges();
      trigger().click();
      fixture.detectChanges();

      fieldB().focus();
      fieldB().value = 'x';
      fieldB().dispatchEvent(new Event('input'));
      fixture.detectChanges();

      escape(modalDialog()!);
      fixture.detectChanges();

      const keepEditing = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Keep editing')!;
      keepEditing.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      expect(modalDialog()).not.toBeNull();
      expect(document.activeElement).toBe(fieldB());
      expect(document.activeElement).not.toBe(fieldA());
    });

    it('confirming the discard closes both the ConfirmDialog and the Modal, returning focus to the original trigger', () => {
      fixture.detectChanges();
      trigger().focus();
      trigger().click();
      fixture.detectChanges();

      fieldB().focus();
      fieldB().value = 'x';
      fieldB().dispatchEvent(new Event('input'));
      fixture.detectChanges();

      escape(modalDialog()!);
      fixture.detectChanges();

      const discard = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Discard')!;
      discard.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      expect(modalDialog()).toBeNull();
      expect(document.activeElement).toBe(trigger());
      expect(host.dirty).toBe(false);
    });
  });

  describe('nested Modal-over-Modal with an explicit focus-return override', () => {
    let fixture: ComponentFixture<NestedModalOverrideSweepHost>;

    beforeEach(async () => {
      await TestBed.configureTestingModule({
        imports: [NestedModalOverrideSweepHost],
      }).compileComponents();
      fixture = TestBed.createComponent(NestedModalOverrideSweepHost);
    });

    function picker(): HTMLSelectElement {
      return fixture.nativeElement.querySelector('[data-testid="picker"]');
    }

    function openInnerButton(): HTMLButtonElement {
      return fixture.nativeElement.querySelector('[data-testid="open-inner"]');
    }

    function dialogs(): HTMLElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('[role="dialog"]'));
    }

    it('traps focus in the inner Modal while both are open', () => {
      fixture.componentInstance.outerOpen = true;
      fixture.detectChanges();

      openInnerButton().focus();
      openInnerButton().click();
      fixture.detectChanges();

      expect(dialogs().length).toBe(2);
      // Mirrors `modal.spec.ts`'s own "moves focus into the modal on open"
      // assertion -- the first focusable element in the inner panel is its own
      // Close button (rendered before the projected content), not the
      // projected input.
      const innerDialog = dialogs()[1];
      expect(document.activeElement).toBe(
        innerDialog.querySelector('button[aria-label="Close"]'),
      );
      expect(innerDialog.contains(document.activeElement)).toBe(true);
    });

    it('overrides FocusTrap\'s default return-to-trigger, landing focus on the outer picker instead', async () => {
      fixture.componentInstance.outerOpen = true;
      fixture.detectChanges();

      openInnerButton().focus();
      openInnerButton().click();
      fixture.detectChanges();
      expect(document.activeElement).not.toBe(picker());

      const innerDialog = dialogs()[1];
      innerDialog.querySelector<HTMLButtonElement>('button[aria-label="Close"]')!.click();
      fixture.detectChanges();

      // Immediately after close, FocusTrap's own default has already fired
      // (it runs synchronously in ngAfterViewChecked) -- landing focus back on
      // the "+ New" trigger button, exactly the default this override corrects.
      expect(document.activeElement).toBe(openInnerButton());

      // The override is scheduled one macrotask later; once it runs, it wins.
      await flushMicrotasks();

      expect(document.activeElement).toBe(picker());
    });
  });
});
