import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ToastService } from '../../shared/toast/toast.service';
import { CustomerFormModal } from './customer-form-modal';
import { dateFormatter } from './customer-formatters';
import { useCustomersQuery, useHardDeleteCustomerMutation } from './customers.service';
import { Customer, toCustomer } from './models/customer';

const DEFAULT_DELETE_MESSAGE =
  'This will permanently delete this customer. This cannot be undone.';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The Customers feature (Story 3.1's list + create, Story 3.2's edit, Story 3.3's
 * hard-delete): a debounced search input, a paginated `DataTable`, and the two
 * distinct empty-state messages required by `EXPERIENCE.md` — mirrors `VehiclesPage`'s
 * exact shape (spec-3-1's Code Map, extended by spec-3-2/spec-3-3). The search Signal
 * is debounced via `toObservable` -> `debounceTime` -> `distinctUntilChanged` ->
 * `toSignal` (no manual `setTimeout`) per AD-3, and only the debounced value
 * participates in the query key. `DataTable`'s `rowMuted`/`rowKey`/`rowError` inputs
 * are left at their defaults — Customer has no active/inactive split yet (Story 3.4),
 * so only `actions` (Edit + Delete per row) is wired. Delete mirrors `VehiclesPage`'s
 * Deactivate mechanism exactly (`deletingCustomer`/`deleteErrorMessage` signals, the
 * "dialog stays open showing the error on failure" pattern) but uses the neutral
 * `ConfirmDialog` (spec-3-3's Scope decision 3 -- the destructive variant doesn't
 * exist yet, deferred to Story 3.5's Erase action) with copy that states the
 * permanent, irreversible nature of a hard-delete in the message itself.
 */
@Component({
  selector: 'app-customers-page',
  imports: [DataTable, Button, CustomerFormModal, ConfirmDialog],
  templateUrl: './customers-page.html',
})
export class CustomersPage {
  private readonly toastService = inject(ToastService);
  private readonly hardDeleteMutation = useHardDeleteCustomerMutation();

  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingCustomer = signal<Customer | null>(null);
  protected readonly deletingCustomer = signal<Customer | null>(null);
  protected readonly deleteErrorMessage = signal<string | null>(null);

  protected readonly debouncedSearch = toSignal(
    toObservable(this.searchInput).pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged()),
    { initialValue: '' },
  );

  /**
   * `page` resets to 1 every time `debouncedSearch` changes (a filtered search
   * shouldn't stay on a stale page number from the previous result set), but is
   * otherwise freely settable via `onPageChange` — mirrors `VehiclesPage`'s exact
   * `linkedSignal`-based mechanism.
   */
  protected readonly page = linkedSignal({
    source: this.debouncedSearch,
    computation: () => 1,
  });

  protected readonly query = useCustomersQuery(() => ({
    page: this.page(),
    pageSize: this.pageSize,
    search: this.debouncedSearch(),
  }));

  protected readonly columns: ColumnDef<Customer>[] = [
    { header: 'First Name', cell: (customer) => customer.firstName },
    { header: 'Last Name', cell: (customer) => customer.lastName },
    { header: 'Email', cell: (customer) => customer.email },
    { header: 'Phone Number', cell: (customer) => customer.phoneNumber },
    { header: 'Created', cell: (customer) => dateFormatter.format(customer.createdDate) },
  ];

  /**
   * A two-entry array for every row (spec-3-2's Edit, spec-3-3's Delete) — Customer
   * has no active/inactive split yet (unlike Vehicle's per-row branching, spec-2-4),
   * so unlike `VehiclesPage.actions` there is nothing to branch on. Per spec-3-3's
   * Scope decision 4, every customer gets the same "Delete" option regardless of
   * whether it would actually succeed -- the UI doesn't pre-guess which customers
   * have bookings; it lets the backend guard do its job and surfaces the 409
   * gracefully when it happens.
   */
  protected readonly actions = (customer: Customer): RowAction<Customer>[] => [
    { label: 'Edit', onClick: (c) => this.openEditModal(c) },
    { label: 'Delete', onClick: (c) => this.openDeleteDialog(c) },
  ];

  protected readonly customers = computed<Customer[]>(() => {
    const data = this.query.data();
    return data ? data.items.map(toCustomer) : [];
  });

  protected readonly totalCount = computed(() => this.query.data()?.totalCount ?? 0);

  protected readonly deleteDialogMessage = computed(
    () => this.deleteErrorMessage() ?? DEFAULT_DELETE_MESSAGE,
  );

  private readonly hasSearchFilter = computed(() => this.debouncedSearch().trim().length > 0);
  private readonly isEmptyResult = computed(
    () => !this.query.isPending() && this.totalCount() === 0,
  );

  protected readonly isGenuinelyEmpty = computed(
    () => this.isEmptyResult() && !this.hasSearchFilter(),
  );
  protected readonly isFilteredEmpty = computed(
    () => this.isEmptyResult() && this.hasSearchFilter(),
  );

  protected onSearchInput(value: string): void {
    this.searchInput.set(value);
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
  }

  protected clearFilters(): void {
    this.searchInput.set('');
  }

  protected openCreateModal(): void {
    this.editingCustomer.set(null);
    this.isFormModalOpen.set(true);
  }

  protected openEditModal(customer: Customer): void {
    this.editingCustomer.set(customer);
    this.isFormModalOpen.set(true);
  }

  protected onFormModalClose(): void {
    this.isFormModalOpen.set(false);
  }

  protected openDeleteDialog(customer: Customer): void {
    this.deleteErrorMessage.set(null);
    this.deletingCustomer.set(customer);
  }

  protected onCancelDelete(): void {
    this.deletingCustomer.set(null);
    this.deleteErrorMessage.set(null);
  }

  protected onConfirmDelete(): void {
    const customer = this.deletingCustomer();
    if (!customer) {
      return;
    }

    this.hardDeleteMutation.mutate(customer.id, {
      onSuccess: () => {
        this.deletingCustomer.set(null);
        this.deleteErrorMessage.set(null);
        this.toastService.success('Customer deleted.');
      },
      onError: (error) => {
        this.deleteErrorMessage.set(this.toErrorMessage(error));
      },
    });
  }

  /**
   * A deliberately shallow mapping (mirrors `VehiclesPage.toErrorMessage`'s exact
   * reasoning: "do NOT try to parse/classify the NormalizedApiError deeply here") --
   * just enough to show a plain string in the `ConfirmDialog`'s message area in place
   * of its normal copy. This is the same shallow mapping shape the spec's Design
   * Notes require reusing rather than duplicating with new logic.
   */
  private toErrorMessage(error: NormalizedApiError): string {
    return error.kind === 'server-error' ? error.message : error.detail;
  }
}
