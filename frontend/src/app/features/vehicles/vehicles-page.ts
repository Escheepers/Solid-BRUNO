import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ToastService } from '../../shared/toast/toast.service';
import { Vehicle, toVehicle } from './models/vehicle';
import { currencyFormatter, dateFormatter } from './vehicle-formatters';
import { VehicleFormModal } from './vehicle-form-modal';
import {
  useDeactivateVehicleMutation,
  useRestoreVehicleMutation,
  useVehiclesQuery,
} from './vehicles.service';

const DEFAULT_DEACTIVATE_MESSAGE =
  'This vehicle will disappear from availability searches. This is reversible — you can restore it later.';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

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
  private readonly router = inject(Router);
  private readonly deactivateMutation = useDeactivateVehicleMutation();
  private readonly restoreMutation = useRestoreVehicleMutation();

  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingVehicle = signal<Vehicle | null>(null);
  protected readonly deactivatingVehicle = signal<Vehicle | null>(null);
  protected readonly deactivateErrorMessage = signal<string | null>(null);
  protected readonly showInactive = signal(false);
  protected readonly restoreErrorRowKey = signal<string | null>(null);
  protected readonly restoreErrorMessage = signal<string | null>(null);

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
    showInactive: this.showInactive(),
  }));

  protected readonly columns: ColumnDef<Vehicle>[] = [
    { header: 'Registration Number', cell: (vehicle) => vehicle.registrationNumber },
    { header: 'Make', cell: (vehicle) => vehicle.make },
    { header: 'Model', cell: (vehicle) => vehicle.model },
    { header: 'Year', cell: (vehicle) => String(vehicle.year) },
    { header: 'Daily Rate', cell: (vehicle) => currencyFormatter.format(vehicle.dailyRate) },
    { header: 'Created', cell: (vehicle) => dateFormatter.format(vehicle.createdDate) },
  ];

  /**
   * A per-row function (spec-2-4's `DataTable.actions` change) so an inactive
   * (soft-deleted) row shows only "Restore" *in place of* the usual Edit/Deactivate
   * pair, per `EXPERIENCE.md`'s State Patterns row for "Soft-deleted vehicle". "View"
   * (spec-2-5) is added to both branches -- viewing a soft-deleted vehicle's detail
   * is a real, AC-required flow, not just the active case.
   */
  protected readonly actions = (vehicle: Vehicle): RowAction<Vehicle>[] =>
    vehicle.isDeleted
      ? [
          { label: 'View', onClick: (v) => this.onViewClick(v) },
          { label: 'Restore', onClick: (v) => this.onRestoreClick(v) },
        ]
      : [
          { label: 'View', onClick: (v) => this.onViewClick(v) },
          { label: 'Edit', onClick: (v) => this.openEditModal(v) },
          { label: 'Deactivate', onClick: (v) => this.openDeactivateDialog(v) },
        ];

  protected readonly rowMuted = (vehicle: Vehicle): boolean => vehicle.isDeleted;
  protected readonly rowKey = (vehicle: Vehicle): string => vehicle.id;

  protected readonly rowError = computed<{ key: string; message: string } | null>(() => {
    const key = this.restoreErrorRowKey();
    const message = this.restoreErrorMessage();
    return key !== null && message !== null ? { key, message } : null;
  });

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

  protected onShowInactiveChange(value: boolean): void {
    this.showInactive.set(value);
  }

  protected onPageChange(page: number): void {
    this.page.set(page);
  }

  protected clearFilters(): void {
    this.searchInput.set('');
  }

  protected onViewClick(vehicle: Vehicle): void {
    this.router.navigate(['/vehicles', vehicle.id]);
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
   * No `ConfirmDialog` step (spec-2-4's Scope decision 3 -- gating the undo of an
   * already-reversible, already-confirmed action behind a second confirmation would
   * be redundant friction, not safety). Clicking "Restore" calls the mutation
   * directly.
   */
  protected onRestoreClick(vehicle: Vehicle): void {
    this.restoreMutation.mutate(vehicle.id, {
      onSuccess: () => {
        this.restoreErrorRowKey.set(null);
        this.restoreErrorMessage.set(null);
        this.toastService.success('Vehicle restored.');
      },
      onError: (error) => {
        this.restoreErrorRowKey.set(vehicle.id);
        this.restoreErrorMessage.set(this.toErrorMessage(error));
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
