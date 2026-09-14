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
import {
  useCustomersQuery,
  useDeactivateCustomerMutation,
  useHardDeleteCustomerMutation,
  useRestoreCustomerMutation,
} from './customers.service';
import { Customer, toCustomer } from './models/customer';

const DEFAULT_DELETE_MESSAGE =
  'This will permanently delete this customer. This cannot be undone.';

const DEFAULT_DEACTIVATE_MESSAGE =
  'This customer will disappear from default listings. This is reversible — you can restore them later.';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The Customers feature (Story 3.1's list + create, Story 3.2's edit, Story 3.3's
 * hard-delete, Story 3.4's deactivate/restore): a debounced search input, a paginated
 * `DataTable`, and the two distinct empty-state messages required by `EXPERIENCE.md`
 * — mirrors `VehiclesPage`'s exact shape (spec-3-1's Code Map, extended by
 * spec-3-2/spec-3-3/spec-3-4). The search Signal is debounced via `toObservable` ->
 * `debounceTime` -> `distinctUntilChanged` -> `toSignal` (no manual `setTimeout`) per
 * AD-3, and only the debounced value participates in the query key. Delete mirrors
 * `VehiclesPage`'s Deactivate mechanism exactly (`deletingCustomer`/`deleteErrorMessage`
 * signals, the "dialog stays open showing the error on failure" pattern) but uses the
 * neutral `ConfirmDialog` (spec-3-3's Scope decision 3 -- the destructive variant
 * doesn't exist yet, deferred to Story 3.5's Erase action) with copy that states the
 * permanent, irreversible nature of a hard-delete in the message itself. Deactivate
 * (spec-3-4) gets its own separate `deactivatingCustomer`/`deactivateErrorMessage`
 * signal pair and `ConfirmDialog` block, alongside (not merged with) Delete's --
 * spec-3-4's Scope decision 4 deliberately defers extracting a shared "confirmable
 * action" abstraction until a third instance justifies it (rule of three).
 */
@Component({
  selector: 'app-customers-page',
  imports: [DataTable, Button, CustomerFormModal, ConfirmDialog],
  templateUrl: './customers-page.html',
})
export class CustomersPage {
  private readonly toastService = inject(ToastService);
  private readonly hardDeleteMutation = useHardDeleteCustomerMutation();
  private readonly deactivateMutation = useDeactivateCustomerMutation();
  private readonly restoreMutation = useRestoreCustomerMutation();

  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingCustomer = signal<Customer | null>(null);
  protected readonly deletingCustomer = signal<Customer | null>(null);
  protected readonly deleteErrorMessage = signal<string | null>(null);
  protected readonly deactivatingCustomer = signal<Customer | null>(null);
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
    showInactive: this.showInactive(),
  }));

  protected readonly columns: ColumnDef<Customer>[] = [
    { header: 'First Name', cell: (customer) => customer.firstName },
    { header: 'Last Name', cell: (customer) => customer.lastName },
    { header: 'Email', cell: (customer) => customer.email },
    { header: 'Phone Number', cell: (customer) => customer.phoneNumber },
    { header: 'Created', cell: (customer) => dateFormatter.format(customer.createdDate) },
  ];

  /**
   * A per-row function (spec-3-4's Story-2.4-mirrored change) so an inactive
   * (soft-deleted) row shows only "Restore" *in place of* the usual Edit/Delete/
   * Deactivate set, mirroring `vehicles-page.ts`'s exact state-dependent `actions`
   * pattern. Per spec-3-3's Scope decision 4, every active customer gets the same
   * "Delete" option regardless of whether it would actually succeed -- the UI
   * doesn't pre-guess which customers have bookings; it lets the backend guard do
   * its job and surfaces the 409 gracefully when it happens. Deactivate is likewise
   * not gated on booking count (spec-3-4's Scope decision 2).
   */
  protected readonly actions = (customer: Customer): RowAction<Customer>[] =>
    customer.isDeleted
      ? [{ label: 'Restore', onClick: (c) => this.onRestoreClick(c) }]
      : [
          { label: 'Edit', onClick: (c) => this.openEditModal(c) },
          { label: 'Delete', onClick: (c) => this.openDeleteDialog(c) },
          { label: 'Deactivate', onClick: (c) => this.openDeactivateDialog(c) },
        ];

  protected readonly rowMuted = (customer: Customer): boolean => customer.isDeleted;
  protected readonly rowKey = (customer: Customer): string => customer.id;

  protected readonly rowError = computed<{ key: string; message: string } | null>(() => {
    const key = this.restoreErrorRowKey();
    const message = this.restoreErrorMessage();
    return key !== null && message !== null ? { key, message } : null;
  });

  protected readonly customers = computed<Customer[]>(() => {
    const data = this.query.data();
    return data ? data.items.map(toCustomer) : [];
  });

  protected readonly totalCount = computed(() => this.query.data()?.totalCount ?? 0);

  protected readonly deleteDialogMessage = computed(
    () => this.deleteErrorMessage() ?? DEFAULT_DELETE_MESSAGE,
  );

  protected readonly deactivateDialogMessage = computed(
    () => this.deactivateErrorMessage() ?? DEFAULT_DEACTIVATE_MESSAGE,
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

  protected onShowInactiveChange(value: boolean): void {
    this.showInactive.set(value);
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

  protected openDeactivateDialog(customer: Customer): void {
    this.deactivateErrorMessage.set(null);
    this.deactivatingCustomer.set(customer);
  }

  protected onCancelDeactivate(): void {
    this.deactivatingCustomer.set(null);
    this.deactivateErrorMessage.set(null);
  }

  protected onConfirmDeactivate(): void {
    const customer = this.deactivatingCustomer();
    if (!customer) {
      return;
    }

    this.deactivateMutation.mutate(customer.id, {
      onSuccess: () => {
        this.deactivatingCustomer.set(null);
        this.deactivateErrorMessage.set(null);
        this.toastService.success('Customer deactivated.');
      },
      onError: (error) => {
        this.deactivateErrorMessage.set(this.toErrorMessage(error));
      },
    });
  }

  /**
   * No `ConfirmDialog` step (mirrors `vehicles-page.ts`'s exact `onRestoreClick`
   * reasoning: gating the undo of an already-reversible, already-confirmed action
   * behind a second confirmation would be redundant friction, not safety). Clicking
   * "Restore" calls the mutation directly.
   */
  protected onRestoreClick(customer: Customer): void {
    this.restoreMutation.mutate(customer.id, {
      onSuccess: () => {
        this.restoreErrorRowKey.set(null);
        this.restoreErrorMessage.set(null);
        this.toastService.success('Customer restored.');
      },
      onError: (error) => {
        this.restoreErrorRowKey.set(customer.id);
        this.restoreErrorMessage.set(this.toErrorMessage(error));
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
