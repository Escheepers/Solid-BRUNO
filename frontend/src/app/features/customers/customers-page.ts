import { Component, computed, inject, linkedSignal, signal } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { debounceTime, distinctUntilChanged } from 'rxjs';

import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { Button } from '../../shared/button/button';
import { ColumnDef, DataTable, RowAction } from '../../shared/data-table/data-table';
import { ConfirmDialog } from '../../shared/confirm-dialog/confirm-dialog';
import { ConfirmableAction, createConfirmableAction } from '../../shared/confirm-dialog/confirmable-action';
import { ToastService } from '../../shared/toast/toast.service';
import { CustomerFormModal } from './customer-form-modal';
import { dateFormatter } from './customer-formatters';
import {
  useAnonymizeCustomerMutation,
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

const DEFAULT_ERASE_MESSAGE =
  "This will permanently erase this customer's personal data. This cannot be undone. " +
  'Their booking history stays intact.';

/** Shared cell treatment for an anonymized customer's PII columns (spec-3-5's
 * Scope decision 4/DESIGN.md's `anonymized-text` token) -- a single constant so
 * the four affected columns below apply the identical class string (DRY). */
const ANONYMIZED_CELL_CLASS = 'italic text-anonymized-text';

const PAGE_SIZE = 20;
const SEARCH_DEBOUNCE_MS = 300;

/**
 * The Customers feature (Story 3.1's list + create, Story 3.2's edit, Story 3.3's
 * hard-delete, Story 3.4's deactivate/restore, Story 3.5's erase/anonymize): a
 * debounced search input, a paginated `DataTable`, and the two distinct empty-state
 * messages required by `EXPERIENCE.md` — mirrors `VehiclesPage`'s exact shape
 * (spec-3-1's Code Map, extended by spec-3-2/spec-3-3/spec-3-4/spec-3-5). The search
 * Signal is debounced via `toObservable` -> `debounceTime` -> `distinctUntilChanged`
 * -> `toSignal` (no manual `setTimeout`) per AD-3, and only the debounced value
 * participates in the query key.
 *
 * Delete, Deactivate, and Erase (spec-3-5) each get their own `ConfirmableAction`
 * (`createConfirmableAction`, spec-3-5's Scope decision 5 -- extracted once Erase
 * became the third instance of the identical open/cancel/confirm +
 * current-item/error-message shape that spec-3-4's Design Notes flagged as the
 * trigger to revisit). Delete and Deactivate's own behavior is unchanged by this
 * refactor -- same messages, same "dialog stays open showing the error on failure"
 * pattern, same neutral `ConfirmDialog`. Erase is the first consumer of the
 * destructive `ConfirmDialog` variant, per spec-3-3's Scope decision 3.
 */
@Component({
  selector: 'app-customers-page',
  imports: [DataTable, Button, CustomerFormModal, ConfirmDialog],
  templateUrl: './customers-page.html',
})
export class CustomersPage {
  private readonly toastService = inject(ToastService);
  private readonly router = inject(Router);
  private readonly hardDeleteMutation = useHardDeleteCustomerMutation();
  private readonly deactivateMutation = useDeactivateCustomerMutation();
  private readonly restoreMutation = useRestoreCustomerMutation();
  private readonly anonymizeMutation = useAnonymizeCustomerMutation();

  protected readonly searchInput = signal('');
  protected readonly pageSize = PAGE_SIZE;
  protected readonly isFormModalOpen = signal(false);
  protected readonly editingCustomer = signal<Customer | null>(null);
  protected readonly showInactive = signal(false);
  protected readonly restoreErrorRowKey = signal<string | null>(null);
  protected readonly restoreErrorMessage = signal<string | null>(null);

  protected readonly hardDeleteAction: ConfirmableAction<Customer> = createConfirmableAction<
    Customer,
    NormalizedApiError
  >({
    mutate: (customer, callbacks) => this.hardDeleteMutation.mutate(customer.id, callbacks),
    defaultMessage: DEFAULT_DELETE_MESSAGE,
    toErrorMessage: (error) => this.toErrorMessage(error),
    onSuccess: () => this.toastService.success('Customer deleted.'),
  });

  protected readonly deactivateAction: ConfirmableAction<Customer> = createConfirmableAction<
    Customer,
    NormalizedApiError
  >({
    mutate: (customer, callbacks) => this.deactivateMutation.mutate(customer.id, callbacks),
    defaultMessage: DEFAULT_DEACTIVATE_MESSAGE,
    toErrorMessage: (error) => this.toErrorMessage(error),
    onSuccess: () => this.toastService.success('Customer deactivated.'),
  });

  protected readonly eraseAction: ConfirmableAction<Customer> = createConfirmableAction<
    Customer,
    NormalizedApiError
  >({
    mutate: (customer, callbacks) => this.anonymizeMutation.mutate(customer.id, callbacks),
    defaultMessage: DEFAULT_ERASE_MESSAGE,
    toErrorMessage: (error) => this.toErrorMessage(error),
    onSuccess: () => this.toastService.success("Customer's personal data erased."),
  });

  protected readonly debouncedSearch = toSignal(
    toObservable(this.searchInput).pipe(debounceTime(SEARCH_DEBOUNCE_MS), distinctUntilChanged()),
    { initialValue: '' },
  );

  /**
   * `page` resets to 1 every time `debouncedSearch` or the "Show inactive" toggle changes (a
   * different filter shouldn't stay on a stale page number from the previous result set), but is
   * otherwise freely settable via `onPageChange` — mirrors `VehiclesPage`'s exact
   * `linkedSignal`-based mechanism.
   */
  protected readonly page = linkedSignal({
    source: () => `${this.debouncedSearch()}|${this.showInactive()}`,
    computation: () => 1,
  });

  /** Bumped after a create so `DataTable` drops any column sort the user clicked earlier. */
  protected readonly sortResetToken = signal(0);

  protected readonly query = useCustomersQuery(() => ({
    page: this.page(),
    pageSize: this.pageSize,
    search: this.debouncedSearch(),
    showInactive: this.showInactive(),
  }));

  /**
   * First Name/Last Name/Email/Phone Number all carry `cellClass` (spec-3-5's Scope
   * decision 4) so an anonymized row's PII columns render in the muted-italic
   * `anonymized-text` treatment instead of the normal body style. Last Name is the
   * one column that also swaps its rendered text for the literal "(anonymized)"
   * suffix (`EXPERIENCE.md`: "never just... the placeholder value alone, so it
   * reads unambiguously as 'this was erased'") -- First Name/Email/Phone Number
   * already show `Customer.Anonymize()`'s own placeholder values, which the
   * `cellClass` styling alone is enough to flag as not-a-real-value.
   */
  protected readonly columns: ColumnDef<Customer>[] = [
    {
      header: 'First Name',
      cell: (customer) => customer.firstName,
      cellClass: (customer) => (customer.isAnonymized ? ANONYMIZED_CELL_CLASS : ''),
    },
    {
      header: 'Last Name',
      cell: (customer) =>
        customer.isAnonymized ? `${customer.lastName} (anonymized)` : customer.lastName,
      cellClass: (customer) => (customer.isAnonymized ? ANONYMIZED_CELL_CLASS : ''),
    },
    {
      header: 'Email',
      cell: (customer) => customer.email,
      cellClass: (customer) => (customer.isAnonymized ? ANONYMIZED_CELL_CLASS : ''),
    },
    {
      header: 'Phone Number',
      cell: (customer) => customer.phoneNumber,
      cellClass: (customer) => (customer.isAnonymized ? ANONYMIZED_CELL_CLASS : ''),
    },
    {
      header: 'Created',
      cell: (customer) => dateFormatter.format(customer.createdDate),
      sortable: true,
      sortValue: (customer) => customer.createdDate.getTime(),
    },
  ];

  /**
   * A per-row function (spec-3-4's Story-2.4-mirrored change, extended to a
   * three-way split by spec-3-5, then to a four-way "Summary" addition by spec-5-1's
   * Scope decision 2) so a row's actions match its state exactly: anonymized rows now
   * get exactly `[Summary]` (previously none at all -- spec-5-1's own AC requires the
   * summary to work for an anonymized customer, and viewing it is the one thing still
   * meaningful once PII is scrubbed); soft-deleted-only rows get `[Summary, Restore,
   * Erase personal data]` (erasing doesn't require restoring first, per spec-3-5's
   * Scope decision 2); active rows keep the full `[Summary, Edit, Delete, Deactivate,
   * Erase personal data]` set. "Summary" leads every branch, mirroring `VehiclesPage`'s
   * own "View" leading its two branches. None of Delete, Deactivate or Erase is gated in the
   * UI -- the backend guards are the single source of truth (Delete: any booking blocks it;
   * Deactivate/Erase: an Active booking blocks them) and their 409 message is shown in the
   * confirm dialog.
   */
  protected readonly actions = (customer: Customer): RowAction<Customer>[] => {
    const summaryAction: RowAction<Customer> = {
      label: 'Summary',
      onClick: (c) => this.onSummaryClick(c),
    };

    if (customer.isAnonymized) {
      return [summaryAction];
    }

    if (customer.isDeleted) {
      return [
        summaryAction,
        { label: 'Restore', onClick: (c) => this.onRestoreClick(c) },
        { label: 'Erase personal data', onClick: (c) => this.eraseAction.open(c) },
      ];
    }

    return [
      summaryAction,
      { label: 'Edit', onClick: (c) => this.openEditModal(c) },
      { label: 'Delete', onClick: (c) => this.hardDeleteAction.open(c) },
      { label: 'Deactivate', onClick: (c) => this.deactivateAction.open(c) },
      { label: 'Erase personal data', onClick: (c) => this.eraseAction.open(c) },
    ];
  };

  /** An anonymized row gets its own distinct `cellClass` treatment (above) instead
   * of the generic dimmed one -- only a soft-deleted-but-not-anonymized row is
   * muted here (spec-3-5's Boundaries). */
  protected readonly rowMuted = (customer: Customer): boolean =>
    customer.isDeleted && !customer.isAnonymized;
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

  /** Mirrors `VehiclesPage.onCreated`: after a successful create, make the new row visible by going
   * back to page 1, clearing the search and dropping any leftover column sort. */
  protected onCreated(): void {
    this.searchInput.set('');
    this.page.set(1);
    this.sortResetToken.update((token) => token + 1);
  }

  /** Mirrors `VehiclesPage.onViewClick`'s exact shape -- a plain `Router.navigate`,
   * available on every row regardless of state (spec-5-1's Scope decision 2). */
  protected onSummaryClick(customer: Customer): void {
    this.router.navigate(['/customers', customer.id, 'summary']);
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
