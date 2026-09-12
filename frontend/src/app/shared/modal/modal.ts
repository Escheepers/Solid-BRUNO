import { AfterViewChecked, Component, ElementRef, ViewChild, input, output } from '@angular/core';

/**
 * Elements a focus trap should cycle through. Deliberately the common, well-known
 * subset (not an exhaustive ARIA-authoring-practices list) — sufficient for this
 * story's forms and simple enough to reason about without a third-party
 * focus-trap dependency.
 */
const FOCUSABLE_SELECTOR = [
  'a[href]',
  'button:not([disabled])',
  'input:not([disabled])',
  'select:not([disabled])',
  'textarea:not([disabled])',
  '[tabindex]:not([tabindex="-1"])',
].join(',');

/**
 * The one generic overlay component (`Modal` in `EXPERIENCE.md`'s Component
 * Patterns): open/close, backdrop, a focus trap, and focus-return-to-trigger. It
 * knows nothing about forms, vehicles, or any other business concept — content is
 * entirely up to its caller via `<ng-content>` (SRP). Per this story's explicit scope
 * (spec-2-1's Scope decision 1), Escape and a backdrop click close it immediately —
 * no dirty-check, no confirmation step; that behavior belongs to a future story once
 * a real caller needs it.
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

  private triggerElement: HTMLElement | null = null;
  private wasOpen = false;

  ngAfterViewChecked(): void {
    const isOpen = this.open();

    if (isOpen && !this.wasOpen) {
      this.triggerElement = document.activeElement as HTMLElement | null;
      this.focusFirstElement();
    } else if (!isOpen && this.wasOpen) {
      this.returnFocusToTrigger();
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
      this.trapTab(event);
    }
  }

  private trapTab(event: KeyboardEvent): void {
    const focusable = this.getFocusableElements();
    if (focusable.length === 0) {
      event.preventDefault();
      return;
    }

    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    const active = document.activeElement;

    if (event.shiftKey && active === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && active === last) {
      event.preventDefault();
      first.focus();
    }
  }

  private requestClose(): void {
    this.closeRequest.emit();
  }

  private focusFirstElement(): void {
    const focusable = this.getFocusableElements();
    const target = focusable[0] ?? this.panelRef?.nativeElement;
    target?.focus();
  }

  private returnFocusToTrigger(): void {
    this.triggerElement?.focus();
    this.triggerElement = null;
  }

  private getFocusableElements(): HTMLElement[] {
    if (!this.panelRef) {
      return [];
    }
    return Array.from(
      this.panelRef.nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
    );
  }
}
