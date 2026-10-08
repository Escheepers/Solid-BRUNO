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
import { useCreateCustomerMutation, useUpdateCustomerMutation } from './customers.service';
import { Customer, toCustomer } from './models/customer';

type CustomerFormFieldName = 'firstName' | 'lastName' | 'email' | 'phoneNumber';

interface CustomerPayload {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
}

const BLANK_FORM_VALUE = {
  firstName: '',
  lastName: '',
  email: '',
  phoneNumber: '',
};

/** Digits only, at most 10 -- the same rule `PhoneNumberRules` enforces server-side (a 400). */
const PHONE_NUMBER_PATTERN = /^[0-9]{1,10}$/;

/**
 * The "+ New Customer" / "Edit Customer" form — one component serving both create
 * and edit (Story 3.2, generalized from Story 3.1's create-only `CreateCustomerModal`,
 * mirroring `CreateVehicleModal` -> `VehicleFormModal`'s exact evolution). `customer()`
 * is `null` for create, or the `Customer` being edited; its presence branches the HTTP
 * verb (POST/PUT), the success toast copy, and the Modal title, and pre-populates the
 * form. Owns the `FormGroup` and all business-rule/validation-vs-server error mapping
 * — the shared components it composes stay ignorant of any of this (SRP). Client-side
 * `Validators` mirror exactly what `CreateCustomerCommandValidator`/
 * `UpdateCustomerCommandValidator` check server-side (FirstName/LastName/PhoneNumber
 * required, Email required + email-shaped) — the only place field-shape rules are
 * duplicated on the frontend, per the DRY requirement.
 *
 * `created` (spec-4-1's Scope decision 7) emits the newly-created `Customer` on a
 * successful create only -- never on update, and never subscribed to by this
 * component's own pre-existing consumer (`CustomersPage`, which has no reason to
 * care) -- a minimal, backward-compatible addition so `BookingFormModal` can open
 * this component nested inside its own Modal and auto-select the customer it just
 * created, without this component needing to know anything about Bookings (SRP).
 *
 * Create and update each go through their own service-level mutation
 * (`useCreateCustomerMutation`/`useUpdateCustomerMutation`, both already owning their
 * own list-invalidation) rather than one `mutationFn` branching internally the way
 * `VehicleFormModal` does — Customer's mutations already lived in `customers.service.ts`
 * before this story (unlike Vehicle's, which are inlined in `VehicleFormModal` itself),
 * so this form selects which already-defined mutation to call rather than duplicating
 * either's HTTP-call/invalidate logic (DRY).
 *
 * The discard-guard (spec-3-2) is copied directly from `VehicleFormModal`'s
 * already-proven composition, not reinvented: intercepting `Modal`'s `closeRequest`,
 * if the form is dirty a `ConfirmDialog` is shown instead of changing `Modal`'s `open`
 * state at all — `Modal` never actually closes (and never runs its own focus-return
 * logic) until the discard is confirmed, which is exactly why cancelling the discard
 * returns focus to the exact field the user was on without any bespoke "remember
 * which field" tracking.
 */
@Component({
  selector: 'app-customer-form-modal',
  imports: [Modal, Input, Button, ReactiveFormsModule, ConfirmDialog],
  templateUrl: './customer-form-modal.html',
})
export class CustomerFormModal {
  readonly open = input.required<boolean>();
  readonly customer = input<Customer | null>(null);
  readonly closeRequest = output<void>();
  readonly created = output<Customer>();

  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phoneNumber: ['', [Validators.required, Validators.pattern(PHONE_NUMBER_PATTERN)]],
  });

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<CustomerFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);
  protected readonly showDiscardConfirm = signal(false);

  protected readonly firstNameError = this.fieldErrorSignal('firstName');
  protected readonly lastNameError = this.fieldErrorSignal('lastName');
  protected readonly emailError = this.fieldErrorSignal('email');
  protected readonly phoneNumberError = this.fieldErrorSignal('phoneNumber');

  protected readonly modalTitle = computed(() => (this.customer() ? 'Edit Customer' : '+ New Customer'));
  protected readonly submitLabel = computed(() =>
    this.customer() ? 'Save Changes' : 'Create Customer',
  );

  protected readonly createMutation = useCreateCustomerMutation();
  protected readonly updateMutation = useUpdateCustomerMutation();

  protected readonly isSubmitting = computed(
    () => this.createMutation.isPending() || this.updateMutation.isPending(),
  );

  private wasOpen = false;

  constructor() {
    /**
     * Pre-populates the form from `customer()` (or blanks it, for create) every time
     * the Modal transitions from closed to open — not merely whenever `customer()`
     * changes, since `CustomersPage` mounts a single, reused `CustomerFormModal`
     * instance rather than recreating one per open. Keying only on `customer()`
     * would fail to re-populate when the same customer reference is edited twice in
     * a row. Mirrors `VehicleFormModal`'s exact `effect()`.
     */
    effect(() => {
      const isOpen = this.open();
      const customer = this.customer();

      if (isOpen && !this.wasOpen) {
        this.populateForm(customer);
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
    for (const name of Object.keys(this.form.controls) as CustomerFormFieldName[]) {
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
    const payload: CustomerPayload = {
      firstName: value.firstName,
      lastName: value.lastName,
      email: value.email,
      phoneNumber: value.phoneNumber,
    };

    const customer = this.customer();

    if (customer) {
      this.updateMutation.mutate(
        { customerId: customer.id, payload },
        {
          onSuccess: () => {
            this.toastService.success('Customer updated.');
            this.resetAndClose();
          },
          onError: (error: NormalizedApiError) => this.applyError(error),
        },
      );
      return;
    }

    this.createMutation.mutate(payload, {
      onSuccess: (dto) => {
        this.toastService.success('Customer created.');
        this.created.emit(toCustomer(dto));
        this.resetAndClose();
      },
      onError: (error: NormalizedApiError) => this.applyError(error),
    });
  }

  /**
   * Intercepts `Modal`'s close request (Escape/backdrop/close button): an untouched
   * form closes immediately as before; a dirty form shows the discard-confirmation
   * dialog instead, leaving `Modal`'s `open` state untouched. Mirrors
   * `VehicleFormModal`'s exact composition.
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

  private populateForm(customer: Customer | null): void {
    this.form.reset(
      customer
        ? {
            firstName: customer.firstName,
            lastName: customer.lastName,
            email: customer.email,
            phoneNumber: customer.phoneNumber,
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
   * never see raw error shapes). Mirrors `VehicleFormModal`'s exact pattern: a 400's
   * `errors` dictionary maps each key to its control; a 409's single-field case uses
   * `fieldFromType`; a `ServerError`/`NotFoundError` never touches a field at all.
   * Identical for both create (POST) and edit (PUT) — a 409 against a different
   * customer's Email renders inline exactly the same way.
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
      const mapped: Partial<Record<CustomerFormFieldName, string>> = {};
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

    const field = fieldFromType(error.type) as CustomerFormFieldName | undefined;
    if (field && field in this.form.controls) {
      this.serverFieldErrors.set({ [field]: error.detail });
    } else {
      this.serverErrorMessage.set(error.detail);
    }
  }

  private toControlName(pascalKey: string): CustomerFormFieldName {
    return (pascalKey.charAt(0).toLowerCase() + pascalKey.slice(1)) as CustomerFormFieldName;
  }

  /**
   * Builds a `computed` signal for one field's displayed error: a server-supplied
   * message (400/409) takes priority; otherwise, once a submit has been attempted, a
   * plain client-side validation message reflects that control's own live validity —
   * reactive to the control's own `valueChanges` so it clears the moment the user
   * fixes the field, without waiting for another submit.
   */
  private fieldErrorSignal(name: CustomerFormFieldName) {
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
      if (control.errors?.['email']) {
        return 'Enter a valid email address.';
      }
      if (control.errors?.['pattern']) {
        return 'Phone number must be digits only, up to 10 digits.';
      }
      return undefined;
    };
  }
}
