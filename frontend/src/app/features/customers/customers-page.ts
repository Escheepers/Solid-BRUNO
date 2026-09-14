import { Component, computed, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { CustomerFormModal } from './customer-form-modal';
import { dateFormatter } from './customer-formatters';
import { useCustomersQuery } from './customers.service';
import { Customer, toCustomer } from './models/customer';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The Customers feature (Story 3.1's list + create, Story 3.2's edit): a debounced
 * search input, a paginated `DataTable`, and the two distinct empty-state messages
 * required by `EXPERIENCE.md` — mirrors `VehiclesPage`'s exact shape (spec-3-1's Code
 * Map, extended by spec-3-2). The search Signal is debounced via `toObservable` ->
 * `debounceTime` -> `distinctUntilChanged` -> `toSignal` (no manual `setTimeout`) per
 * AD-3, and only the debounced value participates in the query key. `DataTable`'s
 * `rowMuted`/`rowKey`/`rowError` inputs are left at their defaults — Customer has no
 * active/inactive split yet (Story 3.4), so only `actions` (a single-entry "Edit" per
 * row) is wired.
 */
@Component({
  selector: 'app-customers-page',
  imports: [DataTable, Button, CustomerFormModal],
  templateUrl: './customers-page.html',
})
export class CustomersPage {
  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingCustomer = signal<Customer | null>(null);

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
   * A single-entry array for every row (spec-3-2's Code Map) — Customer has no
   * active/inactive split yet (unlike Vehicle's per-row branching, spec-2-4), so
   * unlike `VehiclesPage.actions` there is nothing to branch on.
   */
  protected readonly actions = (customer: Customer): RowAction<Customer>[] => [
    { label: 'Edit', onClick: (c) => this.openEditModal(c) },
  ];

  protected readonly customers = computed<Customer[]>(() => {
    const data = this.query.data();
    return data ? data.items.map(toCustomer) : [];
  });

  protected readonly totalCount = computed(() => this.query.data()?.totalCount ?? 0);

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
}
