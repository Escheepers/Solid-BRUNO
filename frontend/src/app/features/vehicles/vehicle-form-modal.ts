import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { NormalizedApiError, fieldFromType } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { Input } from '../../shared/input/input';
import { Modal } from '../../shared/modal/modal';
import { ToastService } from '../../shared/toast/toast.service';
import { Vehicle } from './models/vehicle';
import {
  CreateVehiclePayload,
  useCreateVehicleMutation,
  useUpdateVehicleMutation,
} from './vehicles.service';

type VehicleFormFieldName = 'registrationNumber' | 'make' | 'model' | 'year' | 'dailyRate';

/** Mirrors `Vehicle`'s plausible-year range: 1900 up to next calendar year. */
const EARLIEST_YEAR = 1900;
const LATEST_YEAR = new Date().getFullYear() + 1;

const BLANK_FORM_VALUE = {
  registrationNumber: '',
  make: '',
  model: '',
  year: '',
  dailyRate: '',
};

/**
 * The "+ New Vehicle" / "Edit Vehicle" form — one component serving both create and
 * edit (spec-2-2's Scope decision 5; the two forms are ~95% identical, generalized
 * from Story 2.1's `CreateVehicleModal` rather than duplicated). `vehicle()` is
 * `null` for create, or the `Vehicle` being edited; its presence branches the HTTP
 * verb (POST/PUT), the success toast copy, and the Modal title, and pre-populates
 * the form. Owns the `FormGroup` and all business-rule/validation-vs-server error
 * mapping — the shared components it composes stay ignorant of any of this (SRP).
 * Client-side `Validators` mirror exactly what `CreateVehicleCommandValidator`/
 * `UpdateVehicleCommandValidator` check server-side (RegistrationNumber/Make/Model
 * required, DailyRate positive) — the only place field-shape rules are duplicated on
 * the frontend, per the DRY requirement. `Year` also gets the same plausible range the
 * domain enforces (1900 to next calendar year) as `min`/`max`, so a bad year is caught
 * in the browser; the backend still validates it as a domain invariant (409), not a
 * FluentValidation shape check (spec-2-1's Design Notes).
 *
 * Create and update each go through their own service-level mutation
 * (`useCreateVehicleMutation`/`useUpdateVehicleMutation`, both already owning their
 * own list-invalidation), mirroring `CustomerFormModal`/`BookingFormModal`'s exact
 * same pattern (audit fix: these used to be inlined directly in this component,
 * the one form whose create/update mutations hadn't yet moved to their own
 * `*.service.ts` — moved here purely for consistency, no change to the underlying
 * HTTP call, invalidation, or error-handling behaviour).
 *
 * The discard-guard (spec-2-2) lives entirely here, not in `Modal`: intercepting
 * `Modal`'s `closeRequest`, if the form is dirty a `ConfirmDialog` is shown instead
 * of changing `Modal`'s `open` state at all — `Modal` never actually closes (and
 * never runs its own focus-return logic) until the discard is confirmed, which is
 * exactly why cancelling the discard returns focus to the exact field the user was
 * on without any bespoke "remember which field" tracking (see the spec's Design
 * Notes).
 */
@Component({
  selector: 'app-vehicle-form-modal',
  imports: [Modal, Input, Button, ReactiveFormsModule, ConfirmDialog],
  templateUrl: './vehicle-form-modal.html',
})
export class VehicleFormModal {
  readonly open = input.required<boolean>();
  readonly vehicle = input<Vehicle | null>(null);
  readonly closeRequest = output<void>();
  /** Fires only after a successful CREATE (never an edit or a cancel), so the owning list page can
   * jump to where the new row is visible. Mirrors `CustomerFormModal.created`. */
  readonly created = output<void>();

  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = this.fb.nonNullable.group({
    registrationNumber: ['', Validators.required],
    make: ['', Validators.required],
    model: ['', Validators.required],
    year: ['', [Validators.required, Validators.min(EARLIEST_YEAR), Validators.max(LATEST_YEAR)]],
    dailyRate: ['', [Validators.required, Validators.min(0.01)]],
  });

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<VehicleFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);
  protected readonly showDiscardConfirm = signal(false);

  protected readonly registrationNumberError = this.fieldErrorSignal('registrationNumber');
  protected readonly makeError = this.fieldErrorSignal('make');
  protected readonly modelError = this.fieldErrorSignal('model');
  protected readonly yearError = this.fieldErrorSignal('year');
  protected readonly dailyRateError = this.fieldErrorSignal('dailyRate');

  protected readonly earliestYear = EARLIEST_YEAR;
  protected readonly latestYear = LATEST_YEAR;

  protected readonly modalTitle = computed(() => (this.vehicle() ? 'Edit Vehicle' : '+ New Vehicle'));
  protected readonly submitLabel = computed(() =>
    this.vehicle() ? 'Save Changes' : 'Create Vehicle',
  );

  protected readonly createMutation = useCreateVehicleMutation();
  protected readonly updateMutation = useUpdateVehicleMutation();

  protected readonly isSubmitting = computed(
    () => this.createMutation.isPending() || this.updateMutation.isPending(),
  );

  private wasOpen = false;

  constructor() {
    /**
     * Pre-populates the form from `vehicle()` (or blanks it, for create) every time
     * the Modal transitions from closed to open — not merely whenever `vehicle()`
     * changes, since `VehiclesPage` mounts a single, reused `VehicleFormModal`
     * instance rather than recreating one per open. Keying only on `vehicle()`
     * would fail to re-populate when the same vehicle reference is edited twice in
     * a row (e.g. reopening immediately after a "Keep editing" cancel elsewhere, or
     * without the list query refetching in between).
     */
    effect(() => {
      const isOpen = this.open();
      const vehicle = this.vehicle();

      if (isOpen && !this.wasOpen) {
        this.populateForm(vehicle);
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
   * `serverFieldErrors.set({})`, so this handler running during a reset never
   * leaves a stale or incorrectly-cleared error behind. Mirrors
   * `BookingFormModal`'s exact same addition. Each subscription is piped through
   * `takeUntilDestroyed(this.destroyRef)` (audit fix: this had no teardown at all
   * before, an unmanaged-subscription violation regardless of this app's current
   * usage pattern of never actually destroying a mounted Modal instance) -- `destroyRef`
   * is injected as its own class field precisely so this call site doesn't have to
   * care whether it runs inside or outside an injection context.
   */
  private wireClearFieldErrorOnChange(): void {
    for (const name of Object.keys(this.form.controls) as VehicleFormFieldName[]) {
      this.form.controls[name].valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
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

  protected onSubmit(): void {
    this.serverErrorMessage.set(null);
    this.serverFieldErrors.set({});
    this.submitAttempted.set(true);

    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    const payload: CreateVehiclePayload = {
      registrationNumber: value.registrationNumber,
      make: value.make,
      model: value.model,
      year: Number(value.year),
      dailyRate: Number(value.dailyRate),
    };

    const vehicle = this.vehicle();

    if (vehicle) {
      this.updateMutation.mutate(
        { vehicleId: vehicle.id, payload },
        {
          onSuccess: () => {
            this.toastService.success('Vehicle updated.');
            this.resetAndClose();
          },
          onError: (error: NormalizedApiError) => this.applyError(error),
        },
      );
      return;
    }

    this.createMutation.mutate(payload, {
      onSuccess: () => {
        this.toastService.success('Vehicle created.');
        this.created.emit();
        this.resetAndClose();
      },
      onError: (error: NormalizedApiError) => this.applyError(error),
    });
  }

  /**
   * Intercepts `Modal`'s close request (Escape/backdrop/close button): an untouched
   * form closes immediately as before (`EXPERIENCE.md`: "no confirmation for a
   * still-empty/untouched form"); a dirty form shows the discard-confirmation
   * dialog instead, leaving `Modal`'s `open` state untouched.
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
    this.populateForm(null);
    this.closeRequest.emit();
  }

  private populateForm(vehicle: Vehicle | null): void {
    this.form.reset(
      vehicle
        ? {
            registrationNumber: vehicle.registrationNumber,
            make: vehicle.make,
            model: vehicle.model,
            year: String(vehicle.year),
            dailyRate: String(vehicle.dailyRate),
          }
        : BLANK_FORM_VALUE,
    );
    this.submitAttempted.set(false);
    this.serverFieldErrors.set({});
    this.serverErrorMessage.set(null);
  }

  /**
   * Maps a `NormalizedApiError` onto either the specific field(s) it belongs to or a
   * top-of-form banner — the one place this classification happens (`Input`/`Modal`
   * never see raw error shapes). A 400's `errors` dictionary (FluentValidation, keyed
   * PascalCase) maps each key to its control by lowercasing its first letter; a
   * 409's single-field case uses `fieldFromType`; a `ServerError` never touches a
   * field at all. Identical for both create (POST) and edit (PUT) — a 409 against a
   * different vehicle's RegistrationNumber renders inline exactly the same way.
   */
  private applyError(error: NormalizedApiError): void {
    if (error.kind === 'server-error') {
      this.serverErrorMessage.set(error.message);
      return;
    }

    /**
     * A `NotFoundError` (Story 2.5) is not a shape this form ever meaningfully
     * expects — create never 404s, and edit only would on a genuine race with
     * another user's delete — but `NormalizedApiError` growing this third member
     * means the compiler no longer lets `error.errors`/`error.type` below be
     * accessed without first ruling it out. Treated the same as a `ServerError`:
     * a top-of-form banner using the ProblemDetails `detail` message.
     */
    if (error.kind === 'not-found') {
      this.serverErrorMessage.set(error.detail);
      return;
    }

    if (error.errors) {
      const mapped: Partial<Record<VehicleFormFieldName, string>> = {};
      for (const [key, messages] of Object.entries(error.errors)) {
        if (messages.length === 0) {
          continue;
        }
        const fieldName = this.toControlName(key);
        mapped[fieldName] = messages[0];
      }
      this.serverFieldErrors.set(mapped);
      return;
    }

    const field = fieldFromType(error.type) as VehicleFormFieldName | undefined;
    if (field && field in this.form.controls) {
      this.serverFieldErrors.set({ [field]: error.detail });
    } else {
      this.serverErrorMessage.set(error.detail);
    }
  }

  private toControlName(pascalKey: string): VehicleFormFieldName {
    return (pascalKey.charAt(0).toLowerCase() + pascalKey.slice(1)) as VehicleFormFieldName;
  }

  /**
   * Builds a `computed` signal for one field's displayed error: a server-supplied
   * message (400/409) takes priority; otherwise, once a submit has been attempted, a
   * plain client-side validation message reflects that control's own live validity —
   * reactive to the control's own `valueChanges` so it clears the moment the user
   * fixes the field, without waiting for another submit.
   */
  private fieldErrorSignal(name: VehicleFormFieldName) {
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
      if (control.errors?.['min'] || control.errors?.['max']) {
        return name === 'year'
          ? `Year must be between ${EARLIEST_YEAR} and ${LATEST_YEAR}.`
          : 'Daily rate must be greater than zero.';
      }
      return undefined;
    };
  }
}
