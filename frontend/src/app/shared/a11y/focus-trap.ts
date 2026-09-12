import { ElementRef } from '@angular/core';

/**
 * Elements a focus trap should cycle through. Deliberately the common, well-known
 * subset (not an exhaustive ARIA-authoring-practices list) — sufficient for this
 * app's modals/dialogs and simple enough to reason about without a third-party
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
 * Plain (non-Angular) focus-trap helper shared by `Modal` and `ConfirmDialog`
 * (spec-2-2's Scope decision 4 — a DRY extraction of `Modal`'s pre-existing inline
 * logic, unchanged in behavior). A plain class rather than a component/directive
 * because it's constructed against a panel `ElementRef` by two unrelated overlay
 * components rather than declaratively attached to a single host element.
 */
export class FocusTrap {
  private triggerElement: HTMLElement | null = null;

  constructor(private readonly panelRef: ElementRef<HTMLElement>) {}

  /** Captures the currently focused element as the trigger, then moves focus into
   * the panel — the first focusable descendant, or the panel itself if none. */
  activate(): void {
    this.triggerElement = document.activeElement as HTMLElement | null;
    this.focusFirstElement();
  }

  /** Returns focus to the captured trigger element and clears it. */
  deactivate(): void {
    this.triggerElement?.focus();
    this.triggerElement = null;
  }

  /** Cycles Tab/Shift+Tab within the panel's focusable elements, wrapping from the
   * last back to the first (and vice versa for Shift+Tab). */
  handleTabKey(event: KeyboardEvent): void {
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

  private focusFirstElement(): void {
    const focusable = this.getFocusableElements();
    const target = focusable[0] ?? this.panelRef.nativeElement;
    target?.focus();
  }

  private getFocusableElements(): HTMLElement[] {
    return Array.from(
      this.panelRef.nativeElement.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
    );
  }
}
