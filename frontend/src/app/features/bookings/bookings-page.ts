import { Component, OnInit, TemplateRef, ViewChild, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Badge } from '../../shared/badge/badge';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ConfirmableAction, createConfirmableAction } from '../../shared/confirm-dialog/confirmable-action';
import { ToastService } from '../../shared/toast/toast.service';
import { BookingFormModal } from './booking-form-modal';
import { currencyFormatter, dateFormatter } from './booking-formatters';
import { useBookingsQuery, useCancelBookingMutation } from './bookings.service';
import { Booking, isCancellable, toBooking } from './models/booking';

const PAGE_SIZE = 20;

/** Mirrors `CustomersPage`'s `ANONYMIZED_CELL_CLASS` precedent exactly -- the one
 * shared class string for a row whose customer is anonymized (spec-3-5's
 * `anonymized-text` token), applied to Booking's Customer column here. */
const ANONYMIZED_CELL_CLASS = 'italic text-anonymized-text';

const DEFAULT_CANCEL_MESSAGE =
  'This booking will stay in records as Cancelled. This is not reversible.';

/**
 * Story 4.1's real Bookings feature (extended by spec-4-3's Cancel action), replacing
 * Story 1.6's placeholder: a paginated `DataTable` (Vehicle/Customer/Start/End/Total/
 * Status columns, plus a per-row Cancel action for an eligible booking) and the
 * `BookingFormModal` for create. No search/filter bar exists yet (Scope decision 3 --
 * nothing in this story's AC names a field to search), so this is deliberately
 * simpler than `CustomersPage`/`VehiclesPage`'s debounced-search shape.
 *
 * The Status column is the one column that can't be expressed as a plain string
 * (`ColumnDef.cell`) -- it renders a real `Badge` component per row, via
 * `ColumnDef.cellTemplate` (a minimal, additive `DataTable` extension added by this
 * story; see `data-table.ts`'s own doc comment). The `<ng-template>` is declared in
 * this component's own template and captured via a `{static: true}` `@ViewChild`
 * so it's resolved before `ngOnInit` builds `columns` -- mirrors `Modal`/
 * `ConfirmDialog`'s existing `@ViewChild('panel')` precedent in this codebase.
 *
 * Cancel (spec-4-3) is `createConfirmableAction`'s fourth consumer (after Delete/
 * Deactivate/Erase in `CustomersPage`) -- exactly the cross-feature reuse the
 * extraction was meant to enable. `actions` is a per-row function gated by
 * `isCancellable` (now exported from `models/booking.ts`, spec-4-5's Scope decision
 * 3) so an ineligible row (Completed, Cancelled, or past-EndDate-Active) never
 * offers a Cancel link -- but every row, eligible or not, always gets a "View" link
 * (spec-4-5), navigating to this booking's own `BookingDetailPage` -- mirrors
 * `VehiclesPage`'s own always-present "View" action exactly.
 */
@Component({
  selector: 'app-bookings-page',
  imports: [DataTable, Button, BookingFormModal, Badge, ConfirmDialog],
  templateUrl: './bookings-page.html',
})
export class BookingsPage implements OnInit {
  @ViewChild('statusCell', { static: true })
  private readonly statusCellRef!: TemplateRef<{ $implicit: Booking }>;

  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);
  private readonly cancelMutation = useCancelBookingMutation();

  protected readonly pageSize = PAGE_SIZE;
  protected readonly page = signal(1);
  protected readonly isFormModalOpen = signal(false);

  protected readonly query = useBookingsQuery(() => ({
    page: this.page(),
    pageSize: this.pageSize,
  }));

  protected readonly cancelAction: ConfirmableAction<Booking> = createConfirmableAction<
    Booking,
    NormalizedApiError
  >({
    mutate: (booking, callbacks) => this.cancelMutation.mutate(booking.id, callbacks),
    defaultMessage: DEFAULT_CANCEL_MESSAGE,
    toErrorMessage: (error) => this.toErrorMessage(error),
    onSuccess: () => this.toastService.success('Booking cancelled.'),
  });

  protected readonly actions = (booking: Booking): RowAction<Booking>[] => [
    { label: 'View', onClick: (b) => this.onViewClick(b) },
    ...(isCancellable(booking) ? [{ label: 'Cancel', onClick: (b: Booking) => this.cancelAction.open(b) }] : []),
  ];

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
      {
        header: 'Start',
        cell: (booking) => dateFormatter.format(booking.startDate),
        sortable: true,
        sortValue: (booking) => booking.startDate.getTime(),
      },
      {
        header: 'End',
        cell: (booking) => dateFormatter.format(booking.endDate),
        sortable: true,
        sortValue: (booking) => booking.endDate.getTime(),
      },
      {
        header: 'Total',
        cell: (booking) => currencyFormatter.format(booking.totalPrice),
        sortable: true,
        sortValue: (booking) => booking.totalPrice,
      },
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

  protected onViewClick(booking: Booking): void {
    this.router.navigate(['/bookings', booking.id]);
  }

  protected openCreateModal(): void {
    this.isFormModalOpen.set(true);
  }

  protected onFormModalClose(): void {
    this.isFormModalOpen.set(false);
  }

  /**
   * A deliberately shallow mapping -- mirrors `CustomersPage.toErrorMessage`'s exact
   * reasoning: just enough to show a plain string in the `ConfirmDialog`'s message
   * area in place of its normal copy, never parsed/classified any deeper here.
   */
  private toErrorMessage(error: NormalizedApiError): string {
    return error.kind === 'server-error' ? error.message : error.detail;
  }
}
