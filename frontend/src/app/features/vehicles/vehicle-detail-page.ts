import { Component, OnInit, TemplateRef, ViewChild, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { Badge } from '../../shared/badge/badge';
import { ColumnDef, DataTable } from '../../shared/data-table/data-table';
import { Skeleton } from '../../shared/skeleton/skeleton';
import { useBookingsQuery } from '../bookings/bookings.service';
import { Booking, toBooking } from '../bookings/models/booking';
import { toVehicle } from './models/vehicle';
import { currencyFormatter, dateFormatter } from './vehicle-formatters';
import { useVehicleQuery } from './vehicles.service';

/**
 * The largest `pageSize` `GetBookingsQueryValidator` allows (spec-4-5's Scope
 * decision 4) -- mirrors `BookingFormModal`'s own `PICKER_PAGE_SIZE` precedent
 * exactly. The booking-history section has no pagination controls of its own; a
 * generously large page is fetched instead, reasonable at this assessment's data
 * volumes (a real production system would paginate a vehicle with hundreds of
 * bookings, but nothing here does yet).
 */
const BOOKING_HISTORY_PAGE_SIZE = 100;

/** Mirrors `BookingsPage`'s own `ANONYMIZED_CELL_CLASS` precedent exactly -- the one
 * shared class string for a row whose customer is anonymized (spec-3-5's
 * `anonymized-text` token), duplicated here rather than imported (this codebase's
 * established "formatters/small constants stay feature-local" convention -- see
 * `booking-formatters.ts`'s own doc comment). */
const ANONYMIZED_CELL_CLASS = 'italic text-anonymized-text';

/**
 * The Vehicles feature's single-record view (Story 2.5, the last story in Epic 2;
 * its booking-history section added by spec-4-5). Reads the route's `id` param,
 * drives `useVehicleQuery`, and renders exactly the three states its AC describes --
 * loading (`Skeleton`, shaped for a detail card), not-found (a dedicated message + a
 * link back to the list), and found (the full record, reusing the list's own
 * currency/date formatting). Deliberately has no Edit/Deactivate/Restore actions of
 * its own (spec-2-5's Scope decision 2 -- those stay on the list) -- this
 * component's only added responsibility beyond rendering the vehicle's own record is
 * the booking-history section (SRP still holds: it renders, it doesn't decide what a
 * booking's fields mean).
 *
 * The booking-history section (spec-4-5) reuses `useBookingsQuery` (Bookings
 * feature) filtered on this vehicle's id, and `DataTable`/`Badge` exactly as
 * `BookingsPage` does for its own Status column (the `ColumnDef.cellTemplate`
 * mechanism) -- no new list-rendering pattern invented. Both this page's own
 * `query` and the booking-history `bookingsQuery` are unconditional class fields, so
 * both fire immediately on mount regardless of which template branch ends up
 * visible -- mirrors `BookingFormModal`'s own always-mounted vehicle/customer picker
 * queries' precedent (see `bookings-page.spec.ts`'s `flushPickerQueries` comment).
 */
@Component({
  selector: 'app-vehicle-detail-page',
  imports: [Skeleton, RouterLink, DataTable, Badge],
  templateUrl: './vehicle-detail-page.html',
})
export class VehicleDetailPage implements OnInit {
  @ViewChild('statusCell', { static: true })
  private readonly statusCellRef!: TemplateRef<{ $implicit: Booking }>;

  private readonly vehicleId = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? undefined)),
  );

  protected readonly query = useVehicleQuery(() => this.vehicleId());

  protected readonly vehicle = computed(() => {
    const dto = this.query.data();
    return dto ? toVehicle(dto) : null;
  });

  /**
   * Narrows down to the one error case this story's AC actually requires
   * distinguishing (a real `NotFoundError`, Story 2.5's own addition to
   * `NormalizedApiError`). Any other error (a genuine `ServerError`) falls through to
   * the template's generic fallback -- not explicitly tested by this story's AC, but
   * still never a blank page.
   */
  protected readonly isNotFound = computed(() => this.query.error()?.kind === 'not-found');

  protected readonly formattedDailyRate = computed(() => {
    const vehicle = this.vehicle();
    return vehicle ? currencyFormatter.format(vehicle.dailyRate) : '';
  });

  protected readonly formattedCreatedDate = computed(() => {
    const vehicle = this.vehicle();
    return vehicle ? dateFormatter.format(vehicle.createdDate) : '';
  });

  protected readonly bookingsQuery = useBookingsQuery(() => ({
    page: 1,
    pageSize: BOOKING_HISTORY_PAGE_SIZE,
    vehicleId: this.vehicleId(),
  }));

  protected readonly bookings = computed<Booking[]>(() => {
    const data = this.bookingsQuery.data();
    return data ? data.items.map(toBooking) : [];
  });

  protected readonly bookingsTotalCount = computed(() => this.bookingsQuery.data()?.totalCount ?? 0);

  protected readonly hasNoBookings = computed(
    () => !this.bookingsQuery.isPending() && this.bookingsTotalCount() === 0,
  );

  protected readonly bookingHistoryPageSize = BOOKING_HISTORY_PAGE_SIZE;

  protected bookingColumns: ColumnDef<Booking>[] = [];

  ngOnInit(): void {
    this.bookingColumns = [
      {
        header: 'Customer',
        cell: (booking) =>
          booking.customerIsAnonymized
            ? 'Customer (anonymized)'
            : `${booking.customerFirstName} ${booking.customerLastName}`,
        cellClass: (booking) => (booking.customerIsAnonymized ? ANONYMIZED_CELL_CLASS : ''),
      },
      { header: 'Start', cell: (booking) => dateFormatter.format(booking.startDate) },
      { header: 'End', cell: (booking) => dateFormatter.format(booking.endDate) },
      { header: 'Total', cell: (booking) => currencyFormatter.format(booking.totalPrice) },
      {
        header: 'Status',
        cell: (booking) => booking.status,
        cellTemplate: this.statusCellRef,
      },
    ];
  }

  protected readonly bookingRowKey = (booking: Booking): string => booking.id;
}
