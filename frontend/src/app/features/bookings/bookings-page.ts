import { Component, OnInit, TemplateRef, ViewChild, computed, signal } from '@angular/core';

import { Badge } from '../../shared/badge/badge';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable } from '../../shared/data-table/data-table';
import { BookingFormModal } from './booking-form-modal';
import { currencyFormatter, dateFormatter } from './booking-formatters';
import { useBookingsQuery } from './bookings.service';
import { Booking, toBooking } from './models/booking';

const PAGE_SIZE = 20;

/** Mirrors `CustomersPage`'s `ANONYMIZED_CELL_CLASS` precedent exactly -- the one
 * shared class string for a row whose customer is anonymized (spec-3-5's
 * `anonymized-text` token), applied to Booking's Customer column here. */
const ANONYMIZED_CELL_CLASS = 'italic text-anonymized-text';

/**
 * Story 4.1's real Bookings feature, replacing Story 1.6's placeholder: a
 * paginated `DataTable` (Vehicle/Customer/Start/End/Total/Status columns, no
 * Actions column yet -- Scope decision 5) and the `BookingFormModal` for create.
 * No search/filter bar exists yet (Scope decision 3 -- nothing in this story's AC
 * names a field to search), so this is deliberately simpler than
 * `CustomersPage`/`VehiclesPage`'s debounced-search shape.
 *
 * The Status column is the one column that can't be expressed as a plain string
 * (`ColumnDef.cell`) -- it renders a real `Badge` component per row, via
 * `ColumnDef.cellTemplate` (a minimal, additive `DataTable` extension added by this
 * story; see `data-table.ts`'s own doc comment). The `<ng-template>` is declared in
 * this component's own template and captured via a `{static: true}` `@ViewChild`
 * so it's resolved before `ngOnInit` builds `columns` -- mirrors `Modal`/
 * `ConfirmDialog`'s existing `@ViewChild('panel')` precedent in this codebase.
 */
@Component({
  selector: 'app-bookings-page',
  imports: [DataTable, Button, BookingFormModal, Badge],
  templateUrl: './bookings-page.html',
})
export class BookingsPage implements OnInit {
  @ViewChild('statusCell', { static: true })
  private readonly statusCellRef!: TemplateRef<{ $implicit: Booking }>;

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly isFormModalOpen = signal(false);

  protected readonly query = useBookingsQuery(() => ({
    page: this.page(),
    pageSize: this.pageSize,
  }));

  protected columns: ColumnDef<Booking>[] = [];

  ngOnInit(): void {
    this.columns = [
      {
        header: 'Vehicle',
        cell: (booking) =>
          `${booking.vehicleMake} ${booking.vehicleModel} — ${booking.vehicleRegistrationNumber}`,
      },
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

  protected readonly bookings = computed<Booking[]>(() => {
    const data = this.query.data();
    return data ? data.items.map(toBooking) : [];
  });

  protected readonly totalCount = computed(() => this.query.data()?.totalCount ?? 0);

  protected readonly isEmpty = computed(
    () => !this.query.isPending() && this.totalCount() === 0,
  );

  protected readonly rowKey = (booking: Booking): string => booking.id;

  protected onPageChange(page: number): void {
    this.page.set(page);
  }

  protected openCreateModal(): void {
    this.isFormModalOpen.set(true);
  }

  protected onFormModalClose(): void {
    this.isFormModalOpen.set(false);
  }
}
