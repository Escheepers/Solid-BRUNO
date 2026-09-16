import {
  Component,
  ViewChild,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { NormalizedApiError, fieldFromType } from '../../core/api-client/normalized-api-error';
import { CustomerFormModal } from '../customers/customer-form-modal';
import { Customer, toCustomer } from '../customers/models/customer';
import { useCustomersQuery } from '../customers/customers.service';
import { Vehicle, toVehicle } from '../vehicles/models/vehicle';
import { useVehiclesQuery } from '../vehicles/vehicles.service';
import { Button } from '../../shared/button/button';
import { Combobox } from '../../shared/combobox/combobox';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { Input } from '../../shared/input/input';
import { Modal } from '../../shared/modal/modal';
import { ToastService } from '../../shared/toast/toast.service';
import { currencyFormatter } from './booking-formatters';
import { CreateBookingPayload, useCreateBookingMutation } from './bookings.service';

type BookingFormFieldName = 'vehicleId' | 'customerId' | 'startDate' | 'endDate';

const BLANK_FORM_VALUE = {
  vehicleId: '',
  customerId: '',
  startDate: '',
  endDate: '',
};

/**
 * The largest page size the pickers can request (spec-4-1's Scope decision 6,
 * unchanged by the later searchable-picker bugfix that swapped the pickers from
 * plain native `<select>`s to `Combobox<T>`): populated from the existing
 * paginated `useVehiclesQuery`/`useCustomersQuery` rather than a new unpaginated
 * "all active" endpoint -- reasonable at this assessment's data volumes, and
 * `Combobox` filters this same already-loaded in-memory page client-side rather
 * than issuing a new server-side search. Capped at 100, not a rounder 200 --
 * `GetVehiclesQueryValidator`/
 * `GetCustomersQueryValidator` both bound `PageSize` to `[1, 100]` (AD-10); a larger
 * value 400s the picker's own query, silently leaving both dropdowns empty (caught
 * live, not by any mocked-service unit test). `showInactive: false` on both queries
 * means a soft-deleted vehicle/inactive customer never appears as a pickable option
 * in the first place; the 409/404 paths in `applyError` below exist only for the
 * stale-picker/race case the I/O matrix names (the option was valid when the
 * picker last loaded, but no longer is by the time of submit).
 */
const PICKER_PAGE_SIZE = 100;

const MS_PER_DAY = 24 * 60 * 60 * 1000;

/** Whole-day difference between two `"yyyy-MM-dd"` date-input values, computed the
 * same way `CreateBookingCommandHandler` computes it server-side
 * (`EndDate.DayNumber - StartDate.DayNumber`) -- anchoring both to UTC midnight
 * keeps the subtraction free of DST/timezone drift regardless of the browser's
 * local timezone. */
function daysBetween(startIso: string, endIso: string): number {
  const start = new Date(`${startIso}T00:00:00Z`).getTime();
  const end = new Date(`${endIso}T00:00:00Z`).getTime();
  return Math.round((end - start) / MS_PER_DAY);
}

/**
 * The "+ New Booking" form (spec-4-1): a vehicle/customer picker, a Start/End date
 * pair, a live-computed read-only Total Price, and a nested "+ New Customer" link
 * that opens `CustomerFormModal` on top of this still-open modal (the app's first
 * Modal-over-Modal composition). Structurally mirrors `CustomerFormModal`/
 * `VehicleFormModal`'s exact Reactive Forms + discard-guard shape -- one `FormGroup`,
 * `submitAttempted`/`serverFieldErrors`/`serverErrorMessage` signals, the same
 * `onModalCloseRequest`/`ConfirmDialog` discard flow copied verbatim.
 *
 * The vehicle/customer pickers are `Combobox<T>` (the searchable-picker bugfix,
 * superseding spec-4-1's Scope decision 6's plain native `<select>`s) populated
 * directly from `useVehiclesQuery`/`useCustomersQuery` -- no new endpoint. Total
 * Price is a `computed` signal over the form's own live value
 * (`toSignal(form.valueChanges, ...)`) and the fetched vehicle list, recomputing
 * whenever vehicle/either date changes, rendered inside an `aria-live="polite"`
 * region per the Boundaries.
 */
@Component({
  selector: 'app-booking-form-modal',
  imports: [Modal, Input, Combobox, Button, ReactiveFormsModule, ConfirmDialog, CustomerFormModal],
  templateUrl: './booking-form-modal.html',
})
export class BookingFormModal {
  readonly open = input.required<boolean>();
  readonly closeRequest = output<void>();

  /** Typed as `Combobox<Customer>` rather than an `ElementRef` -- spec-6-4's
   * focus-return-to-Customer-picker fix now lands on the combobox's own
   * focusable text-input element via its public `focus()` method. */
  @ViewChild('customerSelect') private readonly customerComboboxRef?: Combobox<Customer>;

  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.nonNullable.group({
    vehicleId: ['', Validators.required],
    customerId: ['', Validators.required],
    startDate: ['', Validators.required],
    endDate: ['', Validators.required],
  });

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<BookingFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);
  protected readonly showDiscardConfirm = signal(false);
  protected readonly isCustomerModalOpen = signal(false);

  protected readonly vehicleIdError = this.fieldErrorSignal('vehicleId');
  protected readonly customerIdError = this.fieldErrorSignal('customerId');
  protected readonly startDateError = this.fieldErrorSignal('startDate');
  protected readonly endDateError = this.fieldErrorSignal('endDate');

  protected readonly vehiclesQuery = useVehiclesQuery(() => ({
    page: 1,
    pageSize: PICKER_PAGE_SIZE,
    search: '',
    showInactive: false,
  }));
  protected readonly customersQuery = useCustomersQuery(() => ({
    page: 1,
    pageSize: PICKER_PAGE_SIZE,
    search: '',
    showInactive: false,
  }));

  protected readonly vehicles = computed<Vehicle[]>(
    () => this.vehiclesQuery.data()?.items.map(toVehicle) ?? [],
  );
  protected readonly customers = computed<Customer[]>(
    () => this.customersQuery.data()?.items.map(toCustomer) ?? [],
  );

  protected readonly createMutation = useCreateBookingMutation();
  protected readonly isSubmitting = computed(() => this.createMutation.isPending());

  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  /** `null` whenever the vehicle/dates aren't yet a valid combination -- no vehicle
   * selected, either date blank, or `endDate <= startDate` (the exact same
   * "not yet computable" cases `CreateBookingCommandHandler`/`Booking.Create` would
   * reject). Recomputes automatically whenever the form's own value changes. */
  protected readonly totalPrice = computed<number | null>(() => {
    const { vehicleId, startDate, endDate } = this.formValue();
    const vehicle = this.vehicles().find((v) => v.id === vehicleId);
    if (!vehicle || !startDate || !endDate) {
      return null;
    }

    const days = daysBetween(startDate, endDate);
    return days > 0 ? vehicle.dailyRate * days : null;
  });

  protected readonly totalPriceDisplay = computed(() => {
    const price = this.totalPrice();
    return price === null ? null : currencyFormatter.format(price);
  });

  private wasOpen = false;

  constructor() {
    /**
     * Pre-populates (blanks, for create-only) the form every time the Modal
     * transitions from closed to open -- mirrors `CustomerFormModal`/
     * `VehicleFormModal`'s exact `effect()`, minus the edit-mode branch (this story
     * ships create-only; spec-4-1's Never section: "No Edit-a-Booking UI").
     */
    effect(() => {
      const isOpen = this.open();

      if (isOpen && !this.wasOpen) {
        this.populateForm();
      }
      this.wasOpen = isOpen;
    });

    this.wireClearFieldErrorOnChange();
  }

  /**
   * Clears a field's own `serverFieldErrors` entry the moment its control's value
   * actually changes (bugfix: spec-booking-form-error-handling-fixes.md) -- a stale
   * 400/409 field error no longer lingers once the user has corrected that exact
   * field, without waiting for another submit. Scoped per-control so editing one
   * field never clears a different field's still-valid error. `populateForm()`'s
   * own `form.reset(...)` also fires each control's `valueChanges` once, but that
   * is always immediately followed there by an unconditional
   * `serverFieldErrors.set({})`, so this handler running first (or at all) during a
   * reset never leaves a stale or incorrectly-cleared error behind.
   */
  private wireClearFieldErrorOnChange(): void {
    for (const name of Object.keys(this.form.controls) as BookingFormFieldName[]) {
      this.form.controls[name].valueChanges.subscribe(() => {
        this.serverFieldErrors.update((errors) => {
          if (!(name in errors)) {
            return errors;
          }
          const { [name]: _removed, ...rest } = errors;
          return rest;
        });
      });
    }
  }

  protected vehicleLabel(vehicle: Vehicle): string {
    return `${vehicle.make} ${vehicle.model} — ${vehicle.registrationNumber}`;
  }

  protected customerLabel(customer: Customer): string {
    return `${customer.firstName} ${customer.lastName}`;
  }

  /** `Combobox<T>`'s `optionValue` -- the value actually written to the
   * `vehicleId`/`customerId` form controls (unchanged from what the old native
   * `<select>` wrote via each `<option [value]>`). Trivial, but `Combobox`'s
   * `ColumnDef`-style function inputs need one per spec. */
  protected vehicleIdOf(vehicle: Vehicle): string {
    return vehicle.id;
  }

  protected customerIdOf(customer: Customer): string {
    return customer.id;
  }

  protected onSubmit(): void {
    this.serverErrorMessage.set(null);
    this.serverFieldErrors.set({});
    this.submitAttempted.set(true);

    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    const payload: CreateBookingPayload = {
      vehicleId: value.vehicleId,
      customerId: value.customerId,
      startDate: value.startDate,
      endDate: value.endDate,
    };

    this.createMutation.mutate(payload, {
      onSuccess: () => {
        this.toastService.success('Booking created.');
        this.resetAndClose();
      },
      onError: (error) => this.applyError(error),
    });
  }

  /** Opens the nested "+ New Customer" modal on top of this still-open one --
   * the app's first Modal-over-Modal composition. */
  protected openNewCustomerModal(): void {
    this.isCustomerModalOpen.set(true);
  }

  protected onCustomerModalClose(): void {
    this.isCustomerModalOpen.set(false);
    this.focusCustomerSelect();
  }

  /**
   * Fires only on a successful CREATE from the nested `CustomerFormModal`
   * (`created`, spec-4-1's Scope decision 7 -- never on an update, since this
   * nested instance is only ever used in create mode). Selects the new customer
   * in this form's own `customerId` control -- marked dirty explicitly, since a
   * programmatic `setValue` alone doesn't flip Reactive Forms' `dirty` flag, and
   * this selection is genuine user-driven data the discard-guard should protect --
   * and closes the nested modal, leaving the vehicle/dates already entered
   * untouched (per the Boundaries: "creating a customer never loses the vehicle/
   * dates already entered underneath"). Focus-return to the Customer select
   * happens once, in `onCustomerModalClose` below -- `CustomerFormModal`'s own
   * `resetAndClose()` always emits `closeRequest` immediately after `created`
   * on this path (see its `onSubmit`), so that handler already covers this
   * case too; duplicating the focus call here would just call `.focus()` on
   * the same element twice for no benefit (DRY).
   */
  protected onCustomerCreated(customer: Customer): void {
    this.form.controls.customerId.setValue(customer.id);
    this.form.controls.customerId.markAsDirty();
    this.isCustomerModalOpen.set(false);
  }

  /**
   * Explicitly returns focus to this form's own Customer combobox once the
   * nested "+ New Customer" `Modal` closes -- on both the create and cancel
   * paths, since `CustomerFormModal` emits `closeRequest` on every path that
   * actually closes it (spec-6-4's Accessibility Verification Pass;
   * `EXPERIENCE.md`'s one carve-out from `FocusTrap`'s default "return to
   * trigger" behavior: "the inner Modal ... returns it to the Customer picker
   * field on close" -- after creating a customer inline, the useful next
   * action is picking that customer, not re-clicking "+ New Customer").
   * Deferred one macrotask so it runs after Angular's change detection has
   * already closed the nested `Modal` and that `Modal`'s own
   * `FocusTrap.deactivate()` has already returned focus to the "+ New
   * Customer" button -- calling this synchronously here would run first and
   * be immediately overwritten by that default.
   */
  private focusCustomerSelect(): void {
    setTimeout(() => this.customerComboboxRef?.focus());
  }

  /**
   * Intercepts `Modal`'s close request (Escape/backdrop/close button) -- mirrors
   * `CustomerFormModal`/`VehicleFormModal`'s exact discard-guard composition.
   */
  protected onModalCloseRequest(): void {
    if (this.form.dirty) {
      this.showDiscardConfirm.set(true);
      return;
    }
    this.resetAndClose();
  }

  protected onConfirmDiscard(): void {
    this.showDiscardConfirm.set(false);
    this.resetAndClose();
  }

  protected onCancelDiscard(): void {
    this.showDiscardConfirm.set(false);
  }

  private resetAndClose(): void {
    this.populateForm();
    this.closeRequest.emit();
  }

  private populateForm(): void {
    this.form.reset(BLANK_FORM_VALUE);
    this.submitAttempted.set(false);
    this.serverFieldErrors.set({});
    this.serverErrorMessage.set(null);
    this.isCustomerModalOpen.set(false);
  }

  /**
   * Maps a `NormalizedApiError` per spec-4-1's I/O matrix, extended by
   * spec-booking-form-error-handling-fixes.md for the Overlap rule (spec-4-2):
   * - a 400 (`EndDate <= StartDate`) carries an `errors` dictionary keyed
   *   `EndDate` -> mapped onto the `endDate` control, same as every other form's
   *   400 handling.
   * - a 404 (nonexistent/inactive customer) falls through to the generic
   *   top-of-form banner, exactly as the I/O matrix specifies ("Falls through to
   *   ServerError") -- not field-mapped.
   * - a 409 without an `errors` dictionary is one of two domain-rule violations
   *   this endpoint can produce, distinguished via `fieldFromType` (already used
   *   by `VehicleFormModal`/`CustomerFormModal`): the soft-deleted-vehicle rule
   *   (`isDeleted`) maps onto `vehicleId`; the Overlap rule (`overlap`, added by
   *   spec-4-2 after this form's original 409 handling was written -- the bug
   *   this spec fixes) maps onto `endDate`, since the AC's "under the date
   *   fields" lands on the trailing field of the pair, consistent with the
   *   `EndDate <= StartDate` 400 above. An unrecognized rule name falls through
   *   to the generic top-of-form banner rather than guessing a field.
   */
  private applyError(error: NormalizedApiError): void {
    if (error.kind === 'server-error') {
      this.serverErrorMessage.set(error.message);
      return;
    }

    if (error.kind === 'not-found') {
      this.serverErrorMessage.set(error.detail);
      return;
    }

    if (error.errors) {
      const mapped: Partial<Record<BookingFormFieldName, string>> = {};
      for (const [key, messages] of Object.entries(error.errors)) {
        if (messages.length === 0) {
          continue;
        }
        const fieldName = this.toControlName(key);
        if (fieldName in this.form.controls) {
          mapped[fieldName as BookingFormFieldName] = messages[0];
        }
      }

      if (Object.keys(mapped).length > 0) {
        this.serverFieldErrors.set(mapped);
      } else {
        this.serverErrorMessage.set(error.detail);
      }
      return;
    }

    const rule = fieldFromType(error.type);
    if (rule === 'isDeleted') {
      this.serverFieldErrors.set({ vehicleId: error.detail });
    } else if (rule === 'overlap') {
      this.serverFieldErrors.set({ endDate: error.detail });
    } else {
      this.serverErrorMessage.set(error.detail);
    }
  }

  private toControlName(pascalKey: string): string {
    return pascalKey.charAt(0).toLowerCase() + pascalKey.slice(1);
  }

  /**
   * Builds a `computed` signal for one field's displayed error -- identical
   * pattern to `CustomerFormModal`/`VehicleFormModal`'s `fieldErrorSignal`: a
   * server-supplied message takes priority; otherwise, once a submit has been
   * attempted, a plain "This field is required." reflects the control's own live
   * validity, clearing the moment the user fixes the field.
   */
  private fieldErrorSignal(name: BookingFormFieldName) {
    const control = this.form.controls[name];
    const liveValue = toSignal(control.valueChanges, { initialValue: control.value });

    return () => {
      liveValue();
      const serverMessage = this.serverFieldErrors()[name];
      if (serverMessage) {
        return serverMessage;
      }

      if (!this.submitAttempted() || control.valid) {
        return undefined;
      }

      if (control.errors?.['required']) {
        return 'This field is required.';
      }
      return undefined;
    };
  }
}
