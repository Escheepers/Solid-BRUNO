import { Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { Input } from '../../shared/input/input';
import { Modal } from '../../shared/modal/modal';
import { ToastService } from '../../shared/toast/toast.service';
import { addDays, daysBetween, notInThePast, todayIso, toIsoDate } from './booking-dates';
import { currencyFormatter } from './booking-formatters';
import { useUpdateBookingMutation } from './bookings.service';
import { Booking } from './models/booking';

type EditFormFieldName = 'startDate' | 'endDate';

/**
 * The "Edit dates" form for an UPCOMING booking: two date pickers pre-filled from the booking, with the
 * past blocked, and a live total price. Only the booking's dates can change; the price is rescaled at
 * the booking's original daily rate (derived from its current price and length), exactly as the server
 * does -- the server's answer is what is saved, this is only the preview. The owning page only offers
 * this for a booking `isCancellable` says is still upcoming, so the server's "cannot edit" 409 is
 * the stale-page case and just shows in the banner. Server errors: a 400 maps onto the date field it
 * names; everything else (an overlap 409, a concurrency 409, a 404) shows in the banner, since each
 * is about the booking as a whole. Any change to a date clears them.
 */
@Component({
  selector: 'app-booking-edit-modal',
  imports: [Modal, Input, Button, ReactiveFormsModule],
  templateUrl: './booking-edit-modal.html',
})
export class BookingEditModal {
  readonly open = input.required<boolean>();
  readonly booking = input<Booking | null>(null);
  readonly closeRequest = output<void>();
  /** Fires only after a successful save, so the owning page can react (the toast is shown here). */
  readonly updated = output<void>();

  private readonly toastService = inject(ToastService);
  private readonly fb = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly form = this.fb.nonNullable.group({
    startDate: ['', [Validators.required, notInThePast]],
    endDate: ['', Validators.required],
  });

  protected readonly updateMutation = useUpdateBookingMutation();
  protected readonly isSubmitting = computed(() => this.updateMutation.isPending());

  /** Today's local date: the Start Date picker's `min`. Refreshed each time the modal opens. */
  protected readonly today = signal(todayIso());

  private readonly submitAttempted = signal(false);
  private readonly serverFieldErrors = signal<Partial<Record<EditFormFieldName, string>>>({});
  protected readonly serverErrorMessage = signal<string | null>(null);

  protected readonly startDateError = this.fieldErrorSignal('startDate');
  protected readonly endDateError = this.fieldErrorSignal('endDate');

  private readonly formValue = toSignal(this.form.valueChanges, {
    initialValue: this.form.getRawValue(),
  });

  /** End Date can't be on/before the chosen Start Date (or today when none is chosen yet). */
  protected readonly endDateMin = computed(() => {
    const { startDate } = this.formValue();
    return addDays(startDate || this.today(), 1);
  });

  /** The booking's per-day rate, recovered from its current price and length (price = rate x days). */
  private readonly dailyRate = computed<number | null>(() => {
    const booking = this.booking();
    if (!booking) {
      return null;
    }
    const days = daysBetween(toIsoDate(booking.startDate), toIsoDate(booking.endDate));
    return days > 0 ? booking.totalPrice / days : null;
  });

  protected readonly totalPriceDisplay = computed(() => {
    const { startDate, endDate } = this.formValue();
    const rate = this.dailyRate();
    if (rate === null || !startDate || !endDate) {
      return null;
    }
    const days = daysBetween(startDate, endDate);
    return days > 0 ? currencyFormatter.format(rate * days) : null;
  });

  private wasOpen = false;

  constructor() {
    // Pre-fills the form from the booking every time the modal goes from closed to open.
    effect(() => {
      const isOpen = this.open();
      const booking = this.booking();

      if (isOpen && !this.wasOpen && booking) {
        this.populateForm(booking);
      }
      this.wasOpen = isOpen;
    });

    // A stale server error disappears the moment the user changes either date.
    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.serverFieldErrors.set({});
      this.serverErrorMessage.set(null);
    });
  }

  protected onSubmit(): void {
    const booking = this.booking();
    this.serverErrorMessage.set(null);
    this.serverFieldErrors.set({});
    this.submitAttempted.set(true);

    if (!booking || this.form.invalid) {
      return;
    }

    this.updateMutation.mutate(
      { id: booking.id, payload: this.form.getRawValue() },
      {
        onSuccess: () => {
          this.toastService.success('Booking updated.');
          this.updated.emit();
          this.closeRequest.emit();
        },
        onError: (error) => this.applyError(error),
      },
    );
  }

  protected onModalCloseRequest(): void {
    this.closeRequest.emit();
  }

  private populateForm(booking: Booking): void {
    this.today.set(todayIso());
    this.form.reset({
      startDate: toIsoDate(booking.startDate),
      endDate: toIsoDate(booking.endDate),
    });
    this.submitAttempted.set(false);
    this.serverFieldErrors.set({});
    this.serverErrorMessage.set(null);
  }

  private applyError(error: NormalizedApiError): void {
    if (error.kind === 'server-error') {
      this.serverErrorMessage.set(error.message);
      return;
    }

    if (error.kind === 'not-found') {
      this.serverErrorMessage.set(error.detail);
      return;
    }

    const mapped: Partial<Record<EditFormFieldName, string>> = {};
    for (const [key, messages] of Object.entries(error.errors ?? {})) {
      const name = (key.charAt(0).toLowerCase() + key.slice(1)) as EditFormFieldName;
      if (messages.length > 0 && name in this.form.controls) {
        mapped[name] = messages[0];
      }
    }

    if (Object.keys(mapped).length > 0) {
      this.serverFieldErrors.set(mapped);
    } else {
      this.serverErrorMessage.set(error.detail);
    }
  }

  private fieldErrorSignal(name: EditFormFieldName) {
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
      if (control.errors?.['pastDate']) {
        return 'Bookings cannot start in the past.';
      }
      return undefined;
    };
  }
}
