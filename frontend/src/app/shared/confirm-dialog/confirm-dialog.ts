import { AfterViewChecked, Component, ElementRef, ViewChild, input, output } from '@angular/core';

import { FocusTrap } from '../a11y/focus-trap';
import { Button } from '../button/button';

/**
 * The neutral-variant `ConfirmDialog` (`{components.confirm-dialog-neutral}` in
 * DESIGN.md) — a generic confirm/cancel prompt with a title and message. It has no
 * idea what it's confirming (SRP: not a vehicle/booking/customer concept, just a
 * title+message+two buttons); `VehicleFormModal` supplies the discard-changes copy
 * as its first real consumer (spec-2-2). Only the neutral variant is built — no
 * `destructive` styling/prop — until a real consumer needs it (Epic 3's Erase
 * action, per spec-2-2's Scope decision 3).
 *
 * Uses the same `FocusTrap` helper `Modal` uses (DRY, spec-2-2's Scope decision 4):
 * traps focus while open, returns it to whatever had focus when it opened. Escape
 * emits `cancel` and calls `stopPropagation()` — this dialog is rendered
 * alongside an already-open `Modal` during the discard-guard flow, and
 * `EXPERIENCE.md`'s Interaction Primitives require Escape to close only the
 * topmost dialog, never bubble to close the `Modal` underneath too.
 */
@Component({
  selector: 'app-confirm-dialog',
  imports: [Button],
  templateUrl: './confirm-dialog.html',
})
export class ConfirmDialog implements AfterViewChecked {
  readonly open = input.required<boolean>();
  readonly title = input<string>('');
  readonly message = input<string>('');
  readonly confirmLabel = input('Confirm');
  readonly cancelLabel = input('Cancel');

  readonly confirm = output<void>();
  readonly cancel = output<void>();

  @ViewChild('panel') private readonly panelRef?: ElementRef<HTMLElement>;

  private focusTrap: FocusTrap | null = null;
  private wasOpen = false;

  ngAfterViewChecked(): void {
    const isOpen = this.open();

    if (isOpen && !this.wasOpen) {
      this.focusTrap = this.panelRef ? new FocusTrap(this.panelRef) : null;
      this.focusTrap?.activate();
    } else if (!isOpen && this.wasOpen) {
      this.focusTrap?.deactivate();
      this.focusTrap = null;
    }

    this.wasOpen = isOpen;
  }

  protected onKeydown(event: KeyboardEvent): void {
    /**
     * Stops every keydown from bubbling further while this dialog is open — not
     * just Escape. `VehicleFormModal` nests this dialog inside `Modal`'s own
     * projected content for its discard-guard flow, so `Modal`'s panel DOM contains
     * both the form fields and this dialog; without this, an unstopped Tab would
     * bubble to `Modal`'s own `FocusTrap`, which would then cycle across the
     * *combined* set of form-and-dialog focusable elements instead of staying
     * confined to this (topmost) dialog. `EXPERIENCE.md`'s "Escape closes the
     * topmost modal/dialog only" rule implies the same "topmost dialog owns all
     * keyboard interaction while open" principle for Tab.
     */
    event.stopPropagation();

    if (event.key === 'Escape') {
      event.preventDefault();
      this.cancel.emit();
      return;
    }

    if (event.key === 'Tab') {
      this.focusTrap?.handleTabKey(event);
    }
  }

  protected onConfirmClick(): void {
    this.confirm.emit();
  }

  protected onCancelClick(): void {
    this.cancel.emit();
  }
}
