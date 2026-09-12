import { Component, computed, input } from '@angular/core';

/**
 * The shared primary-action button (`{components.button-primary}` in DESIGN.md),
 * built once so the submit-button disabled+inline-spinner pattern
 * (`EXPERIENCE.md`'s Component Patterns/State Patterns) is reused by every mutating
 * form rather than re-implemented per feature. `loading` swaps the projected label
 * for a small spinner and forces `disabled` — the whole point being to prevent a
 * double-submit on a slow connection.
 */
@Component({
  selector: 'app-button',
  templateUrl: './button.html',
})
export class Button {
  readonly type = input<'button' | 'submit'>('button');
  readonly loading = input(false);
  readonly disabled = input(false);

  protected readonly isDisabled = computed(() => this.disabled() || this.loading());
}
