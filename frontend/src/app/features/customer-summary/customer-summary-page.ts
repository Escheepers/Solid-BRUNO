import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { Badge } from '../../shared/badge/badge';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { useCustomerSummaryQuery } from './customer-summary.service';
import { currencyFormatter, dateFormatter } from './customer-summary-formatters';
import { CustomerSummaryBooking, toCustomerSummary } from './models/customer-summary';

/** Mirrors `CustomersPage`/`VehicleDetailPage`'s own `ANONYMIZED_CELL_CLASS` precedent
 * exactly — the one shared class string for the muted-italic `anonymized-text`
 * treatment (spec-3-5's token), duplicated here rather than imported (this codebase's
 * established "formatters/small constants stay feature-local" convention). */
const ANONYMIZED_CLASS = 'italic text-anonymized-text';

/**
 * The read-only Customer Summary page (Story 5.1) — reachable only as a permanent
 * Customers-list row action and a link from `BookingDetailPage`'s Customer field
 * (spec-5-1's Scope decision 1); never linked to from anywhere with an edit intent
 * (Scope decision 6, "no dedicated Cancel/Edit/anything-mutating action anywhere on
 * this page"). Mirrors `VehicleDetailPage`/`BookingDetailPage`'s exact three-state
 * shape (loading via `Skeleton`, not-found, found) and their `isNotFound`
 * error-narrowing reasoning.
 *
 * Its booking-history list is a plain, minimal read-only `<table>` built directly in
 * this page's own template rather than `DataTable` (Scope decision 4 — `DataTable`
 * always renders a pagination footer, which this story's AC explicitly forbids as
 * "navigation chrome"; the query itself is already unbounded/unpaginated per the
 * Boundaries). `Badge` is still reused for the Status column — a status indicator, not
 * navigation chrome.
 *
 * `styles.css`'s `@media print` rule hides the global `app-top-app-bar` plus every
 * `.no-print`-flagged element on this page (the "Back to Customers" link and the
 * convenience Print button below) — leaving only the identity card and booking list in
 * printed output, per the Boundaries' "browser print strips the Top App Bar and all
 * button chrome."
 */
@Component({
  selector: 'app-customer-summary-page',
  imports: [Skeleton, RouterLink, Badge],
  templateUrl: './customer-summary-page.html',
})
export class CustomerSummaryPage {
  private readonly customerId = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? undefined)),
  );

  protected readonly query = useCustomerSummaryQuery(() => this.customerId());

  protected readonly summary = computed(() => {
    const dto = this.query.data();
    return dto ? toCustomerSummary(dto) : null;
  });

  /**
   * Narrows down to the one error case this story's AC actually requires
   * distinguishing (a real `NotFoundError`) — mirrors `VehicleDetailPage`/
   * `BookingDetailPage`'s exact `isNotFound` reasoning. Any other error falls through
   * to the template's generic fallback — never a blank page.
   */
  protected readonly isNotFound = computed(() => this.query.error()?.kind === 'not-found');

  protected readonly displayName = computed(() => {
    const summary = this.summary();
    if (!summary) {
      return '';
    }
    return summary.isAnonymized
      ? 'Customer (anonymized)'
      : `${summary.firstName} ${summary.lastName}`;
  });

  protected readonly hasNoBookings = computed(() => (this.summary()?.bookings.length ?? 0) === 0);

  protected readonly anonymizedClass = ANONYMIZED_CLASS;

  protected vehicleDisplayName(booking: CustomerSummaryBooking): string {
    return `${booking.vehicleMake} ${booking.vehicleModel} — ${booking.vehicleRegistrationNumber}`;
  }

  protected formattedStartDate(booking: CustomerSummaryBooking): string {
    return dateFormatter.format(booking.startDate);
  }

  protected formattedEndDate(booking: CustomerSummaryBooking): string {
    return dateFormatter.format(booking.endDate);
  }

  protected formattedTotalPrice(booking: CustomerSummaryBooking): string {
    return currencyFormatter.format(booking.totalPrice);
  }

  protected readonly bookingRowKey = (booking: CustomerSummaryBooking): string => booking.id;

  /** The optional convenience Print button (Boundaries: not AC-required, browser print
   * works regardless) — a thin wrapper over `window.print()` so this is the one place
   * in the component that touches the global, testable via a spy in the spec. */
  protected printPage(): void {
    window.print();
  }
}
