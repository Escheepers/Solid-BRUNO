import { Component, computed, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { ColumnDef, DataTable } from '../../shared/data-table/data-table';
import { Vehicle, toVehicle } from './models/vehicle';
import { useVehiclesQuery } from './vehicles.service';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

const currencyFormatter = new Intl.NumberFormat('en-ZA', {
  style: 'currency',
  currency: 'ZAR',
});

const dateFormatter = new Intl.DateTimeFormat('en-ZA', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});

/**
 * The Vehicles feature (Story 1.7's walking-skeleton proof): a debounced search
 * input, a paginated `DataTable`, and the two distinct empty-state messages required
 * by `EXPERIENCE.md`. The search Signal is debounced via `toObservable` ->
 * `debounceTime` -> `distinctUntilChanged` -> `toSignal` (no manual `setTimeout`) per
 * AD-3, and only the debounced value participates in the query key.
 */
@Component({
  selector: 'app-vehicles-page',
  imports: [DataTable],
  templateUrl: './vehicles-page.html',
})
export class VehiclesPage {
  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;

  protected readonly debouncedSearch = toSignal(
    toObservable(this.searchInput).pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged()),
    { initialValue: '' },
  );

  /**
   * `page` resets to 1 every time `debouncedSearch` changes (a filtered search
   * shouldn't stay on a stale page number from the previous result set), but is
   * otherwise freely settable via `onPageChange`. `linkedSignal` resolves this
   * synchronously as part of the signal graph, so the very first query issued for a
   * new search term already reads page 1 — an `effect()`-based reset that calls
   * `page.set(1)` after the fact would still let one wasted request go out first for
   * `{ page: <stale>, search: <new> }` before a corrected one landed.
   */
  protected readonly page = linkedSignal({
    source: this.debouncedSearch,
    computation: () => 1,
  });

  protected readonly query = useVehiclesQuery(() => ({
    page: this.page(),
    pageSize: this.pageSize,
    search: this.debouncedSearch(),
  }));

  protected readonly columns: ColumnDef<Vehicle>[] = [
    { header: 'Registration Number', cell: (vehicle) => vehicle.registrationNumber },
    { header: 'Make', cell: (vehicle) => vehicle.make },
    { header: 'Model', cell: (vehicle) => vehicle.model },
    { header: 'Year', cell: (vehicle) => String(vehicle.year) },
    { header: 'Daily Rate', cell: (vehicle) => currencyFormatter.format(vehicle.dailyRate) },
    { header: 'Created', cell: (vehicle) => dateFormatter.format(vehicle.createdDate) },
  ];

  protected readonly vehicles = computed<Vehicle[]>(() => {
    const data = this.query.data();
    return data ? data.items.map(toVehicle) : [];
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
}
