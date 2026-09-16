import {
  Component,
  ElementRef,
  ViewChild,
  computed,
  forwardRef,
  input,
  signal,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextComboboxId = 0;

/**
 * The one generic, reusable typeahead picker (the searchable-picker bugfix spec) --
 * a real ARIA Combobox (List Autocomplete) pattern: a text input with
 * `role="combobox"` plus a `role="listbox"` popup, filtering an already-loaded
 * in-memory `options: T[]` live as the user types. Mirrors `Input`'s exact
 * `ControlValueAccessor` wiring (`forwardRef` + `NG_VALUE_ACCESSOR`) so it drops
 * into `formControlName` with zero `FormGroup` changes, and mirrors `DataTable<T>`'s
 * exact generic-over-T shape (`optionLabel`/`optionValue` caller-supplied
 * functions) -- `Combobox` has no idea what a "vehicle" or "customer" is (SRP).
 * Built on its second real consumer (Vehicle and Customer, both in
 * `BookingFormModal`, at the same time) per this codebase's established DRY
 * threshold, mirroring how `CapturingLoggerProvider`/`createConfirmableAction`
 * were each extracted on their own second-real-consumer trigger.
 *
 * Uses signals for all internal state (the in-progress filter, open/active
 * state, the committed value) rather than `Input`'s plain-fields-plus-
 * `markForCheck()` approach -- this component's filtering/keyboard-nav state is
 * genuinely derived/reactive (mirrors `DataTable`'s own signal-heavy internals),
 * and signals propagate to the view without needing an explicit
 * `ChangeDetectorRef`.
 */
@Component({
  selector: 'app-combobox',
  templateUrl: './combobox.html',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => Combobox),
      multi: true,
    },
  ],
})
export class Combobox<T> implements ControlValueAccessor {
  /**
   * The DOM `id` this component's internal text input renders (for an
   * external `<label for>` to target, mirroring the old native `<select>`'s
   * own `id`). Named `controlId` rather than the more obvious `id` -- Angular
   * reflects a *plain* (unbound) `id="..."` attribute onto a component's own
   * host element in addition to feeding it to a same-named `@Input`/`input()`,
   * which would silently create a second, duplicate `id` in the DOM (the host
   * `<app-combobox>` tag itself) and break any `document.querySelector`/
   * `getElementById` lookup for it. Avoiding the collision by not reusing a
   * native attribute name is the robust fix -- safe for every caller, not just
   * ones that remember to bind it as `[id]="'...'"`.
   */
  readonly controlId = input.required<string>();
  readonly options = input.required<T[]>();
  readonly optionLabel = input.required<(option: T) => string>();
  readonly optionValue = input.required<(option: T) => string>();
  /** Mirrors `Input.error` exactly -- drives `aria-invalid`/`aria-describedby` on
   * this component's own internal text input (`controlId() + '-error'`, the same
   * convention every hand-rolled error `<div>` in this codebase already uses). */
  readonly error = input<string | null | undefined>(undefined);

  @ViewChild('textInput') private readonly textInputRef?: ElementRef<HTMLInputElement>;

  protected readonly listboxId = `app-combobox-${nextComboboxId++}-listbox`;

  /** The committed form-control value (an option's `optionValue()`, e.g. `.id`). */
  protected readonly currentValue = signal('');
  /**
   * `null` whenever nothing is being actively edited -- the displayed text
   * (`displayText` below) then just resolves from the committed selection. Set
   * to the raw typed string while the user is editing, and reset to `null` by
   * `close()` on every path that closes the popup (Enter/Escape/Tab/blur/
   * selecting an option), which is exactly what "reverts to the selected
   * option's label" means: falling back to `selectedLabel` again. A plain
   * `computed` (`displayText`) derives the visible text from this rather than
   * an `effect()` copying into a separate signal -- avoids relying on Angular's
   * effect-flush timing (computeds are pull-based and always up to date the
   * moment `options()` resolves, which is what keeps the "value set before
   * options load" case from ever getting stuck showing stale/blank text).
   */
  protected readonly filterOverride = signal<string | null>(null);
  protected readonly isOpen = signal(false);
  protected readonly activeIndex = signal(-1);
  protected readonly disabledState = signal(false);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  protected readonly hasError = computed(() => !!this.error());
  protected readonly describedById = computed(() => `${this.controlId()}-error`);

  private readonly selectedOption = computed(() =>
    this.options().find((option) => this.optionValue()(option) === this.currentValue()),
  );

  protected readonly selectedLabel = computed(() => {
    const option = this.selectedOption();
    return option ? this.optionLabel()(option) : '';
  });

  /** The text actually shown in the input -- see `filterOverride`'s own doc
   * comment. */
  protected readonly displayText = computed(() => this.filterOverride() ?? this.selectedLabel());

  /**
   * The options actually rendered in the popup. Shows the full list whenever
   * nothing has been typed since the popup last closed (`filterOverride` is
   * `null`, or an empty string), and narrows to a live case-insensitive
   * substring match the moment the user types.
   */
  protected readonly filteredOptions = computed<T[]>(() => {
    const query = (this.filterOverride() ?? '').trim().toLowerCase();
    if (!query) {
      return this.options();
    }
    return this.options().filter((option) =>
      this.optionLabel()(option).toLowerCase().includes(query),
    );
  });

  protected readonly activeOptionId = computed<string | null>(() => {
    if (!this.isOpen()) {
      return null;
    }
    const options = this.filteredOptions();
    const index = this.activeIndex();
    if (index < 0 || index >= options.length) {
      return null;
    }
    return this.optionId(options[index]);
  });

  writeValue(value: string | null): void {
    this.currentValue.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabledState.set(isDisabled);
  }

  /** Exposed so callers can return focus to this control's own text input --
   * e.g. `BookingFormModal`'s spec-6-4 focus-return-to-Customer-picker fix, now
   * landing on the combobox's own focusable element instead of a native
   * `<select>`. */
  focus(): void {
    this.textInputRef?.nativeElement.focus();
  }

  protected onInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.filterOverride.set(text);
    this.isOpen.set(true);
    this.activeIndex.set(this.filteredOptions().length > 0 ? 0 : -1);
  }

  protected onBlur(): void {
    this.close();
    this.onTouched();
  }

  protected onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        if (!this.isOpen()) {
          this.open();
          return;
        }
        this.moveActive(1);
        return;
      case 'ArrowUp':
        if (!this.isOpen()) {
          return;
        }
        event.preventDefault();
        this.moveActive(-1);
        return;
      case 'Enter':
        if (!this.isOpen()) {
          return;
        }
        event.preventDefault();
        this.selectActive();
        return;
      case 'Escape':
        if (!this.isOpen()) {
          return;
        }
        // Stops here rather than bubbling to `Modal`'s own Escape handler --
        // closing just this popup must never also trigger the enclosing
        // Modal's close/discard-guard flow (mirrors `ConfirmDialog`/`Modal`'s
        // own "topmost handler owns the key" precedent).
        event.preventDefault();
        event.stopPropagation();
        this.close();
        return;
      default:
        return;
    }
  }

  protected selectOption(option: T): void {
    this.commit(option);
    this.close();
  }

  /** Prevents the listbox's own `mousedown` from stealing focus off the text
   * input before the corresponding `click` fires `selectOption` -- the input
   * would otherwise `blur()` (closing the popup) before the click handler ever
   * runs. */
  protected onOptionMousedown(event: MouseEvent): void {
    event.preventDefault();
  }

  protected optionId(option: T): string {
    return `${this.listboxId}-option-${this.optionValue()(option)}`;
  }

  private open(): void {
    this.isOpen.set(true);
    const options = this.filteredOptions();
    const selected = this.selectedOption();
    const index = selected
      ? options.findIndex((option) => this.optionValue()(option) === this.optionValue()(selected))
      : -1;
    this.activeIndex.set(index >= 0 ? index : options.length > 0 ? 0 : -1);
  }

  private close(): void {
    this.isOpen.set(false);
    this.activeIndex.set(-1);
    this.filterOverride.set(null);
  }

  private moveActive(delta: number): void {
    const length = this.filteredOptions().length;
    if (length === 0) {
      this.activeIndex.set(-1);
      return;
    }
    const next = this.activeIndex() + delta;
    this.activeIndex.set(Math.min(Math.max(next, 0), length - 1));
  }

  private selectActive(): void {
    const options = this.filteredOptions();
    const index = this.activeIndex();
    if (index < 0 || index >= options.length) {
      return;
    }
    this.commit(options[index]);
    this.close();
  }

  private commit(option: T): void {
    const value = this.optionValue()(option);
    this.currentValue.set(value);
    this.onChange(value);
  }
}
