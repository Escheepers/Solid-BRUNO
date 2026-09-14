import { Component, computed, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable } from '../../shared/data-table/data-table';
import { CreateCustomerModal } from './create-customer-modal';
import { dateFormatter } from './customer-formatters';
import { useCustomersQuery } from './customers.service';
import { Customer, toCustomer } from './models/customer';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The Customers feature (Story 3.1's list + create): a debounced search input, a
 * paginated `DataTable`, and the two distinct empty-state messages required by
 * `EXPERIENCE.md` — mirrors `VehiclesPage`'s Story-1.7/2.1-era shape exactly (spec-3-1's
 * Code Map), not any of Vehicles' later per-row-action/show-inactive growth (Customer
 * has no such state yet). The search Signal is debounced via `toObservable` ->
 * `debounceTime` -> `distinctUntilChanged` -> `toSignal` (no manual `setTimeout`) per
 * AD-3, and only the debounced value participates in the query key. `DataTable`'s
 * `actions`/`rowMuted`/`rowKey`/`rowError` inputs are all left at their defaults —
 * Customer has no row actions yet, so no bespoke per-row function is needed.
 */
@Component({
  selector: 'app-customers-page',
  imports: [DataTable, Button, CreateCustomerModal],
  templateUrl: './customers-page.html',
})
export class CustomersPage {
  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isCreateModalOpen = signal(false);

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
    this.isCreateModalOpen.set(true);
  }

  protected onCreateModalClose(): void {
    this.isCreateModalOpen.set(false);
  }
}
