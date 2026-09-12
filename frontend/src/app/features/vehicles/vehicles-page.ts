import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ToastService } from '../../shared/toast/toast.service';
import { Vehicle, toVehicle } from './models/vehicle';
import { VehicleFormModal } from './vehicle-form-modal';
import { useDeactivateVehicleMutation, useVehiclesQuery } from './vehicles.service';

const DEFAULT_DEACTIVATE_MESSAGE =
  'This vehicle will disappear from availability searches. This is reversible — you can restore it later.';

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
  imports: [DataTable, Button, VehicleFormModal, ConfirmDialog],
  templateUrl: './vehicles-page.html',
})
export class VehiclesPage {
  private readonly toastService = inject(ToastService);
  private readonly deactivateMutation = useDeactivateVehicleMutation();

  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingVehicle = signal<Vehicle | null>(null);
  protected readonly deactivatingVehicle = signal<Vehicle | null>(null);
  protected readonly deactivateErrorMessage = signal<string | null>(null);

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

  protected readonly actions: RowAction<Vehicle>[] = [
    { label: 'Edit', onClick: (vehicle) => this.openEditModal(vehicle) },
    { label: 'Deactivate', onClick: (vehicle) => this.openDeactivateDialog(vehicle) },
  ];

  protected readonly deactivateDialogMessage = computed(
    () => this.deactivateErrorMessage() ?? DEFAULT_DEACTIVATE_MESSAGE,
  );

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

  protected openCreateModal(): void {
    this.editingVehicle.set(null);
    this.isFormModalOpen.set(true);
  }

  protected openEditModal(vehicle: Vehicle): void {
    this.editingVehicle.set(vehicle);
    this.isFormModalOpen.set(true);
  }

  protected onFormModalClose(): void {
    this.isFormModalOpen.set(false);
  }

  protected openDeactivateDialog(vehicle: Vehicle): void {
    this.deactivateErrorMessage.set(null);
    this.deactivatingVehicle.set(vehicle);
  }

  protected onCancelDeactivate(): void {
    this.deactivatingVehicle.set(null);
    this.deactivateErrorMessage.set(null);
  }

  protected onConfirmDeactivate(): void {
    const vehicle = this.deactivatingVehicle();
    if (!vehicle) {
      return;
    }

    this.deactivateMutation.mutate(vehicle.id, {
      onSuccess: () => {
        this.deactivatingVehicle.set(null);
        this.deactivateErrorMessage.set(null);
        this.toastService.success('Vehicle deactivated.');
      },
      onError: (error) => {
        this.deactivateErrorMessage.set(this.toErrorMessage(error));
      },
    });
  }

  /**
   * A deliberately shallow mapping (per the spec's Design Notes: "do NOT try to
   * parse/classify the NormalizedApiError deeply here") -- just enough to show a
   * plain string in the `ConfirmDialog`'s message area in place of its normal copy.
   */
  private toErrorMessage(error: NormalizedApiError): string {
    return error.kind === 'server-error' ? error.message : error.detail;
  }
}
