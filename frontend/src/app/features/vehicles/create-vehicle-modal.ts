import { Component, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { QueryClient, injectMutation } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { NormalizedApiError, fieldFromType } from '../../core/api-client/normalized-api-error';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { Button } from '../../shared/button/button';
import { Input } from '../../shared/input/input';
import { Modal } from '../../shared/modal/modal';
import { ToastService } from '../../shared/toast/toast.service';

type VehicleFormFieldName = 'registrationNumber' | 'make' | 'model' | 'year' | 'dailyRate';

interface CreateVehiclePayload {
  registrationNumber: string;
  make: string;
  model: string;
  year: number;
  dailyRate: number;
}

/** Matches `useVehiclesQuery`'s query-key convention (AD-3) exactly, minus the params
 * — `invalidateQueries` matches every params variant sharing this key prefix. */
const VEHICLES_LIST_QUERY_KEY = ['vehicles', 'list'] as const;

/**
 * The "+ New Vehicle" form (this story's real, first consumer of `Modal`/`Input`/
 * `Button`/`Toast`). Owns the `FormGroup` and all business-rule/validation-vs-server
 * error mapping — the shared components it composes stay ignorant of any of this
 * (SRP). Client-side `Validators` mirror exactly what
 * `CreateVehicleCommandValidator` checks server-side (RegistrationNumber/Make/Model
 * required, DailyRate positive) — the only place field-shape rules are duplicated on
 * the frontend, per the DRY requirement. `Year` is deliberately left with no more
 * than `required`, matching the backend, which validates it as a domain invariant
 * (409), not a FluentValidation shape check (spec-2-1's Design Notes).
 */
@Component({
  selector: 'app-create-vehicle-modal',
  imports: [Modal, Input, Button, ReactiveFormsModule],
  templateUrl: './create-vehicle-modal.html',
})
export class CreateVehicleModal {
  readonly open = input.required<boolean>();
  readonly closeRequest = output<void>();

  private readonly apiClient = inject(ApiClient);
  private readonly queryClient = inject(QueryClient);
  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);

  protected readonly form = this.fb.nonNullable.group({
    registrationNumber: ['', Validators.required],
    make: ['', Validators.required],
    model: ['', Validators.required],
    year: ['', Validators.required],
    dailyRate: ['', [Validators.required, Validators.min(0.01)]],
  });

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<VehicleFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);

  protected readonly registrationNumberError = this.fieldErrorSignal('registrationNumber');
  protected readonly makeError = this.fieldErrorSignal('make');
  protected readonly modelError = this.fieldErrorSignal('model');
  protected readonly yearError = this.fieldErrorSignal('year');
  protected readonly dailyRateError = this.fieldErrorSignal('dailyRate');

  protected readonly mutation = injectMutation<VehicleDto, NormalizedApiError, CreateVehiclePayload>(
    () => ({
      mutationFn: (payload) =>
        firstValueFrom(this.apiClient.post<VehicleDto, CreateVehiclePayload>('vehicles', payload)),
      onSuccess: () => {
        this.queryClient.invalidateQueries({ queryKey: VEHICLES_LIST_QUERY_KEY });
        this.toastService.success('Vehicle created.');
        this.resetAndClose();
      },
      onError: (error) => this.applyError(error),
    }),
  );

  protected onSubmit(): void {
    this.serverErrorMessage.set(null);
    this.serverFieldErrors.set({});
    this.submitAttempted.set(true);

    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    this.mutation.mutate({
      registrationNumber: value.registrationNumber,
      make: value.make,
      model: value.model,
      year: Number(value.year),
      dailyRate: Number(value.dailyRate),
    });
  }

  protected onModalCloseRequest(): void {
    this.resetAndClose();
  }

  private resetAndClose(): void {
    this.form.reset({
      registrationNumber: '',
      make: '',
      model: '',
      year: '',
      dailyRate: '',
    });
    this.submitAttempted.set(false);
    this.serverFieldErrors.set({});
    this.serverErrorMessage.set(null);
    this.closeRequest.emit();
  }

  /**
   * Maps a `NormalizedApiError` onto either the specific field(s) it belongs to or a
   * top-of-form banner — the one place this classification happens (`Input`/`Modal`
   * never see raw error shapes). A 400's `errors` dictionary (FluentValidation, keyed
   * PascalCase) maps each key to its control by lowercasing its first letter; a
   * 409's single-field case uses `fieldFromType`; a `ServerError` never touches a
   * field at all.
   */
  private applyError(error: NormalizedApiError): void {
    if (error.kind === 'server-error') {
      this.serverErrorMessage.set(error.message);
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
      if (control.errors?.['min']) {
        return 'Daily rate must be greater than zero.';
      }
      return undefined;
    };
  }
}
