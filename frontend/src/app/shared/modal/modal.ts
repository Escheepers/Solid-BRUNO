import { AfterViewChecked, Component, ElementRef, ViewChild, input, output } from '@angular/core';

import { FocusTrap } from '../a11y/focus-trap';

/**
 * The one generic overlay component (`Modal` in `EXPERIENCE.md`'s Component
 * Patterns): open/close, backdrop, a focus trap, and focus-return-to-trigger. It
 * knows nothing about forms, vehicles, or any other business concept — content is
 * entirely up to its caller via `<ng-content>` (SRP). Per this story's explicit scope
 * (spec-2-1's Scope decision 1), Escape and a backdrop click close it immediately —
 * no dirty-check, no confirmation step; a dirty-form guard is a caller's concern
 * (see `VehicleFormModal`, spec-2-2) not this component's. The trap-tab/focus-
 * capture/focus-return mechanics themselves live in the shared `FocusTrap` helper
 * (spec-2-2's Scope decision 4 — extracted so `ConfirmDialog` can reuse the exact
 * same behavior without duplicating it).
 */
@Component({
  selector: 'app-modal',
  templateUrl: './modal.html',
})
export class Modal implements AfterViewChecked {
  readonly open = input.required<boolean>();
  readonly title = input<string>();

  readonly closeRequest = output<void>();

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

  protected onBackdropClick(): void {
    this.requestClose();
  }

  protected onCloseButtonClick(): void {
    this.requestClose();
  }

  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      this.requestClose();
      return;
    }

    if (event.key === 'Tab') {
      this.focusTrap?.handleTabKey(event);
    }
  }

  private requestClose(): void {
    this.closeRequest.emit();
  }
}
