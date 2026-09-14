import { Component, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { NormalizedApiError, fieldFromType } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { Input } from '../../shared/input/input';
import { Modal } from '../../shared/modal/modal';
import { ToastService } from '../../shared/toast/toast.service';
import { useCreateCustomerMutation } from './customers.service';

type CustomerFormFieldName = 'firstName' | 'lastName' | 'email' | 'phoneNumber';

const BLANK_FORM_VALUE = {
  firstName: '',
  lastName: '',
  email: '',
  phoneNumber: '',
};

/**
 * The "+ New Customer" form — create-only, mirroring `CreateVehicleModal`'s
 * Story-2.1-era shape exactly (spec-3-1's Design decision 4: this stays create-only
 * until Story 3.2 needs Edit for real, the identical evolution `CreateVehicleModal`
 * -> `VehicleFormModal` followed). No `customer` input, no discard-guard, no
 * `ConfirmDialog` — Escape/backdrop closes the `Modal` immediately, since that
 * generalization is explicitly deferred to a later story. Owns the `FormGroup` and
 * all business-rule/validation-vs-server error mapping — `Modal`/`Input`/`Button`
 * stay entirely ignorant of any of this (SRP). Client-side `Validators` mirror
 * exactly what `CreateCustomerCommandValidator` checks server-side
 * (FirstName/LastName/PhoneNumber required, Email required + email-shaped) — the
 * only place field-shape rules are duplicated on the frontend, per the DRY
 * requirement.
 */
@Component({
  selector: 'app-create-customer-modal',
  imports: [Modal, Input, Button, ReactiveFormsModule],
  templateUrl: './create-customer-modal.html',
})
export class CreateCustomerModal {
  readonly open = input.required<boolean>();
  readonly closeRequest = output<void>();

  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.nonNullable.group({
    firstName: ['', Validators.required],
    lastName: ['', Validators.required],
    email: ['', [Validators.required, Validators.email]],
    phoneNumber: ['', Validators.required],
  });

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<CustomerFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);

  protected readonly firstNameError = this.fieldErrorSignal('firstName');
  protected readonly lastNameError = this.fieldErrorSignal('lastName');
  protected readonly emailError = this.fieldErrorSignal('email');
  protected readonly phoneNumberError = this.fieldErrorSignal('phoneNumber');

  protected readonly mutation = useCreateCustomerMutation();

  protected onSubmit(): void {
    this.serverErrorMessage.set(null);
    this.serverFieldErrors.set({});
    this.submitAttempted.set(true);

    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    this.mutation.mutate(
      {
        firstName: value.firstName,
        lastName: value.lastName,
        email: value.email,
        phoneNumber: value.phoneNumber,
      },
      {
        onSuccess: () => {
          this.toastService.success('Customer created.');
          this.resetAndClose();
        },
        onError: (error) => this.applyError(error),
      },
    );
  }

  /**
   * `Modal`'s close request (Escape/backdrop/close button) closes immediately — no
   * discard-guard (see class doc).
   */
  protected onModalCloseRequest(): void {
    this.resetAndClose();
  }

  private resetAndClose(): void {
    this.form.reset(BLANK_FORM_VALUE);
    this.submitAttempted.set(false);
    this.serverFieldErrors.set({});
    this.serverErrorMessage.set(null);
    this.closeRequest.emit();
  }

  /**
   * Maps a `NormalizedApiError` onto either the specific field(s) it belongs to or a
   * top-of-form banner — the one place this classification happens (`Input`/`Modal`
   * never see raw error shapes), mirroring `VehicleFormModal`'s exact pattern. A
   * 400's `errors` dictionary (FluentValidation, keyed PascalCase) maps each key to
   * its control by lowercasing its first letter; a 409's single-field case (the
   * duplicate-email conflict) uses `fieldFromType`; a `ServerError`/`NotFoundError`
   * never touches a field at all.
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
      return undefined;
    };
  }
}
