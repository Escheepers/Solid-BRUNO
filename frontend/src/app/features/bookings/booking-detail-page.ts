import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { QueryClient } from '@tanstack/angular-query-experimental';
import { map } from 'rxjs';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Badge } from '../../shared/badge/badge';
import { Button } from '../../shared/button/button';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ConfirmableAction, createConfirmableAction } from '../../shared/confirm-dialog/confirmable-action';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { ToastService } from '../../shared/toast/toast.service';
import { currencyFormatter, dateFormatter } from './booking-formatters';
import { useBookingQuery, useCancelBookingMutation } from './bookings.service';
import { Booking, isCancellable, toBooking } from './models/booking';

const DEFAULT_CANCEL_MESSAGE =
  'This booking will stay in records as Cancelled. This is not reversible.';

/**
 * The Bookings feature's single-record view (spec-4-5), mirroring
 * `VehicleDetailPage`'s exact three-state shape -- loading (`Skeleton`), not-found
 * (a dedicated message + a link back to the list), and found (the full record) --
 * the same SRP-scoped responsibility (Story 2.5), applied to a Booking instead of a
 * Vehicle.
 *
 * Unlike `VehicleDetailPage`, this page also offers a Cancel action -- reusing
 * `isCancellable` (moved to `models/booking.ts` by this same story, spec-4-5's Scope
 * decision 3) and `createConfirmableAction` exactly as `BookingsPage` does for its
 * own row-level Cancel, so this page's eligibility and confirm/toast/error-message
 * behavior never drifts from the list's (DRY, and the Boundaries' "its Cancel action
 * must agree with the list's, always").
 *
 * A successful cancel also invalidates this booking's own `['bookings', 'detail',
 * id]` query (in addition to `useCancelBookingMutation`'s own `['bookings', 'list']`
 * invalidation) so the page immediately reflects the new Cancelled status and
 * retracts the now-ineligible Cancel action, rather than showing stale data until a
 * manual refresh.
 */
@Component({
  selector: 'app-booking-detail-page',
  imports: [Skeleton, RouterLink, Badge, Button, ConfirmDialog],
  templateUrl: './booking-detail-page.html',
})
export class BookingDetailPage {
  private readonly toastService = inject(ToastService);
  private readonly queryClient = inject(QueryClient);
  private readonly cancelMutation = useCancelBookingMutation();

  private readonly bookingId = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? undefined)),
  );

  protected readonly query = useBookingQuery(() => this.bookingId());

  protected readonly booking = computed(() => {
    const dto = this.query.data();
    return dto ? toBooking(dto) : null;
  });

  /**
   * Narrows down to the one error case this story's AC actually requires
   * distinguishing (a real `NotFoundError`) -- mirrors `VehicleDetailPage`'s exact
   * `isNotFound` reasoning. Any other error falls through to the template's generic
   * fallback -- never a blank page.
   */
  protected readonly isNotFound = computed(() => this.query.error()?.kind === 'not-found');

  protected readonly cancellable = computed(() => {
    const booking = this.booking();
    return booking !== null && isCancellable(booking);
  });

  protected readonly vehicleDisplayName = computed(() => {
    const booking = this.booking();
    return booking
      ? `${booking.vehicleMake} ${booking.vehicleModel} — ${booking.vehicleRegistrationNumber}`
      : '';
  });

  protected readonly customerDisplayName = computed(() => {
    const booking = this.booking();
    if (!booking) {
      return '';
    }
    return booking.customerIsAnonymized
      ? 'Customer (anonymized)'
      : `${booking.customerFirstName} ${booking.customerLastName}`;
  });

  protected readonly formattedStartDate = computed(() => {
    const booking = this.booking();
    return booking ? dateFormatter.format(booking.startDate) : '';
  });

  protected readonly formattedEndDate = computed(() => {
    const booking = this.booking();
    return booking ? dateFormatter.format(booking.endDate) : '';
  });

  protected readonly formattedTotalPrice = computed(() => {
    const booking = this.booking();
    return booking ? currencyFormatter.format(booking.totalPrice) : '';
  });

  protected readonly cancelAction: ConfirmableAction<Booking> = createConfirmableAction<
    Booking,
    NormalizedApiError
  >({
    mutate: (booking, callbacks) => this.cancelMutation.mutate(booking.id, callbacks),
    defaultMessage: DEFAULT_CANCEL_MESSAGE,
    toErrorMessage: (error) => this.toErrorMessage(error),
    onSuccess: (booking) => {
      this.queryClient.invalidateQueries({ queryKey: ['bookings', 'detail', booking.id] });
      this.toastService.success('Booking cancelled.');
    },
  });

  protected openCancelDialog(): void {
    const booking = this.booking();
    if (booking) {
      this.cancelAction.open(booking);
    }
  }

  /**
   * A deliberately shallow mapping -- mirrors `BookingsPage.toErrorMessage`'s exact
   * reasoning: just enough to show a plain string in the `ConfirmDialog`'s message
   * area in place of its normal copy, never parsed/classified any deeper here.
   */
  private toErrorMessage(error: NormalizedApiError): string {
    return error.kind === 'server-error' ? error.message : error.detail;
  }
}
