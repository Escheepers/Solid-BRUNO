import { ChangeDetectorRef, Component, OnChanges, forwardRef, inject, input } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextInputId = 0;

/**
 * The shared text-field control (`{components.input}` in DESIGN.md /
 * `EXPERIENCE.md`'s Component Patterns) — a real `ControlValueAccessor`, usable as
 * `<app-input formControlName="..." label="..." [error]="..." />` inside any
 * `FormGroup`. Only knows about the form-control/label/error-display contract; it has
 * no idea whether a given `error` came from a client-side validator, a 400, or a
 * 409 — that classification is entirely the calling form's concern (SRP).
 */
@Component({
  selector: 'app-input',
  templateUrl: './input.html',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => Input),
      multi: true,
    },
  ],
})
export class Input implements ControlValueAccessor, OnChanges {
  readonly label = input<string>('');
  readonly type = input<string>('text');
  readonly error = input<string | null | undefined>(undefined);
  /** Lowest allowed value for `type="date"`/`"number"` fields (e.g. `yyyy-MM-dd` or `1900`); ignored otherwise. */
  readonly min = input<string | number | null>(null);
  /** Highest allowed value, same shape as `min`. */
  readonly max = input<string | number | null>(null);

  protected readonly inputId = `app-input-${nextInputId++}`;

  protected value = '';
  protected disabled = false;

  private readonly cdr = inject(ChangeDetectorRef);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  /**
   * `error` is this component's one reactive (signal) input; `ngOnChanges` fires
   * synchronously whenever it (or `label`/`type`) changes, so an explicit
   * `markForCheck()` here keeps it in lockstep with `value`/`disabled` (plain fields
   * written imperatively from the CVA methods below) — every piece of this
   * component's displayed state is refreshed through the same mechanism.
   */
  ngOnChanges(): void {
    this.cdr.markForCheck();
  }

  protected get hasError(): boolean {
    return !!this.error();
  }

  writeValue(value: string | null): void {
    this.value = value ?? '';
    this.cdr.markForCheck();
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
    this.cdr.markForCheck();
  }

  protected onInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.value = value;
    this.onChange(value);
  }

  protected onBlur(): void {
    this.onTouched();
  }
}
