import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerDto } from '../../core/models/customer-dto';
import { PagedResult } from '../../core/models/paged-result';
import { ToastService } from '../../shared/toast/toast.service';
import { CustomersPage } from './customers-page';

function customerDto(overrides: Partial<CustomerDto> = {}): CustomerDto {
  return {
    id: 'c1',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane.doe@example.com',
    phoneNumber: '0821234567',
    createdDate: '2026-01-15T10:30:00Z',
    isDeleted: false,
    isAnonymized: false,
    ...overrides,
  };
}

function pagedResult(
  items: CustomerDto[],
  overrides: Partial<PagedResult<CustomerDto>> = {},
): PagedResult<CustomerDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 20, ...overrides };
}

/**
 * Matches the same macrotask-flush pattern established by `vehicles-page.spec.ts`:
 * some framework-internal work (here, TanStack Query's Angular `effect()`-based
 * reactivity, which schedules asynchronously) needs a real event-loop turn to run,
 * not just a resolved microtask.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('CustomersPage', () => {
  let fixture: ComponentFixture<CustomersPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [CustomersPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(CustomersPage);
    httpMock = TestBed.inject(HttpTestingController);
    toastService = TestBed.inject(ToastService);
  });

  afterEach(() => {
    httpMock.verify();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  function searchInput(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="search"]');
  }

  function setSearch(value: string): void {
    const input = searchInput();
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  function expectCustomersRequest(matchSearch?: string, matchShowInactive?: string) {
    return httpMock.expectOne(
      (req) =>
        req.url === '/api/customers' &&
        (matchSearch === undefined || req.params.get('search') === matchSearch) &&
        (matchShowInactive === undefined || req.params.get('showInactive') === matchShowInactive),
    );
  }

  it('shows Skeleton rows (via DataTable) while the first request is in flight', async () => {
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    expectCustomersRequest().flush(pagedResult([]));
  });

  it('shows "No customers yet" when no customers exist and no search filter is active', async () => {
    await settle();
    expectCustomersRequest('').flush(pagedResult([]));
    await settle();

    expect(fixture.nativeElement.textContent).toContain('No customers yet');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();
  });

  it('renders a populated DataTable with one row per customer once data resolves', async () => {
    await settle();
    expectCustomersRequest('').flush(
      pagedResult([
        customerDto({ id: 'c1', firstName: 'Jane', email: 'jane@example.com' }),
        customerDto({ id: 'c2', firstName: 'Zola', email: 'zola@example.com' }),
      ]),
    );
    await settle();

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('jane@example.com');
    expect(fixture.nativeElement.textContent).toContain('zola@example.com');
  });

  it(
    'shows "No customers match these filters" with a working Clear all filters action ' +
      'when a search yields zero results',
    async () => {
      await settle();
      expectCustomersRequest('').flush(pagedResult([customerDto()]));
      await settle();

      setSearch('zzz-nomatch');
      fixture.detectChanges();

      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();

      expectCustomersRequest('zzz-nomatch').flush(pagedResult([]));
      await settle();

      expect(fixture.nativeElement.textContent).toContain('No customers match these filters');
      const clearButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.includes('Clear all filters'));
      expect(clearButton).toBeTruthy();

      clearButton!.click();
      fixture.detectChanges();

      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();

      expectCustomersRequest('').flush(pagedResult([customerDto()]));
    },
  );

  it('debounces the search input, issuing exactly one re-fetch ~300ms after the user stops typing', async () => {
    await settle();
    expectCustomersRequest('').flush(pagedResult([customerDto()]));
    await settle();

    setSearch('j');
    fixture.detectChanges();
    setSearch('ja');
    fixture.detectChanges();
    setSearch('jan');
    fixture.detectChanges();

    // No fetch yet — rapid keystrokes shouldn't trigger a request per keystroke.
    httpMock.expectNone((req) => req.url === '/api/customers' && req.params.get('search') === 'jan');

    await new Promise((resolve) => setTimeout(resolve, 350));
    await settle();

    expectCustomersRequest('jan').flush(pagedResult([]));
  });

  it('resets to page 1 when the (debounced) search term changes', async () => {
    await settle();
    expectCustomersRequest('').flush(pagedResult([customerDto()], { totalCount: 100 }));
    await settle();

    const nextButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Next');
    nextButton!.click();
    await settle();

    expectCustomersRequest('').flush(pagedResult([customerDto()], { totalCount: 100, page: 2 }));
    await settle();

    setSearch('jane');
    fixture.detectChanges();

    await new Promise((resolve) => setTimeout(resolve, 350));
    await settle();

    const req = expectCustomersRequest('jane');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(pagedResult([customerDto()]));
  });

  it('opens the Create Customer modal when "+ New Customer" is clicked', async () => {
    await settle();
    expectCustomersRequest('').flush(pagedResult([]));
    await settle();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    const newCustomerButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === '+ New Customer');
    newCustomerButton!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('clicking "Edit" on a row opens the modal pre-populated with that row’s data', async () => {
    await settle();
    expectCustomersRequest('').flush(
      pagedResult([customerDto({ id: 'c7', firstName: 'Zola', email: 'zola@example.com' })]),
    );
    await settle();

    const editButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).find((b) => b.textContent?.trim() === 'Edit');
    expect(editButton).toBeTruthy();

    editButton!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();
    expect(dialog.textContent).toContain('Edit Customer');

    const firstNameInput: HTMLInputElement = fixture.nativeElement.querySelector('app-input input');
    expect(firstNameInput.value).toBe('Zola');
  });

  it('closing the Create Customer modal closes the dialog', async () => {
    await settle();
    expectCustomersRequest('').flush(pagedResult([]));
    await settle();

    const newCustomerButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === '+ New Customer');
    newCustomerButton!.click();
    fixture.detectChanges();

    const dialog = fixture.nativeElement.querySelector('[role="dialog"]');
    expect(dialog).not.toBeNull();

    dialog.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }),
    );
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
  });

  describe('delete row action', () => {
    function alertDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="alertdialog"]');
    }

    async function seedOneRowAndOpenDeleteDialog(): Promise<void> {
      await settle();
      expectCustomersRequest('').flush(
        pagedResult([customerDto({ id: 'c9', firstName: 'Nomvula', email: 'nomvula@example.com' })]),
      );
      await settle();

      const deleteButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Delete');
      expect(deleteButton).toBeTruthy();

      deleteButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Delete" on a row opens the ConfirmDialog for that customer', async () => {
      await seedOneRowAndOpenDeleteDialog();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).toContain('Delete this customer?');
      expect(dialog!.textContent).toContain(
        'This will permanently delete this customer. This cannot be undone.',
      );
    });

    it('cancelling closes the dialog and never calls the mutation', async () => {
      await seedOneRowAndOpenDeleteDialog();

      const cancelButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Cancel');
      cancelButton!.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      httpMock.expectNone((req) => req.url === '/api/customers/c9');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and invalidates the list', async () => {
      await seedOneRowAndOpenDeleteDialog();

      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Delete');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9');
      expect(req.request.method).toBe('DELETE');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      // `invalidateQueries()` (called from `useHardDeleteCustomerMutation`'s own
      // `onSuccess`) awaits the active list query's refetch before it resolves --
      // mirrors `VehiclesPage`'s Deactivate test's exact ordering requirement.
      expectCustomersRequest().flush(pagedResult([]));
      await settle();

      expect(alertDialog()).toBeNull();
      expect(toastSpy).toHaveBeenCalledWith('Customer deleted.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
    });

    it('a failed (409, has bookings) delete keeps the dialog open and shows the exact message instead of the normal copy', async () => {
      await seedOneRowAndOpenDeleteDialog();

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Delete');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9');
      req.flush(
        {
          type: 'urn:bruno:customer:has-bookings',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'This customer has bookings — deactivate or erase their data instead.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).not.toContain(
        'This will permanently delete this customer. This cannot be undone.',
      );
      expect(dialog!.textContent).toContain(
        'This customer has bookings — deactivate or erase their data instead.',
      );
    });
  });

  describe('deactivate row action', () => {
    function alertDialogs(): HTMLElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('[role="alertdialog"]'));
    }

    function deactivateDialog(): HTMLElement | undefined {
      return alertDialogs().find((d) => d.textContent?.includes('Deactivate this customer?'));
    }

    async function seedOneRowAndOpenDeactivateDialog(): Promise<void> {
      await settle();
      expectCustomersRequest('').flush(
        pagedResult([customerDto({ id: 'c9', firstName: 'Nomvula', email: 'nomvula@example.com' })]),
      );
      await settle();

      const deactivateButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      expect(deactivateButton).toBeTruthy();

      deactivateButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Deactivate" on a row opens the ConfirmDialog for that customer', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const dialog = deactivateDialog();
      expect(dialog).not.toBeUndefined();
      expect(dialog!.textContent).toContain(
        'This customer will disappear from default listings',
      );
    });

    it('cancelling closes the dialog and never calls the mutation', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const cancelButton = Array.from<HTMLButtonElement>(
        deactivateDialog()!.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Keep active');
      cancelButton!.click();
      fixture.detectChanges();

      expect(deactivateDialog()).toBeUndefined();
      httpMock.expectNone((req) => req.url === '/api/customers/c9/deactivate');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and invalidates the list', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const confirmButton = Array.from<HTMLButtonElement>(
        deactivateDialog()!.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9/deactivate');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      // `invalidateQueries()` (called from `useDeactivateCustomerMutation`'s own
      // `onSuccess`) awaits the active list query's refetch before it resolves --
      // mirrors `VehiclesPage`'s Deactivate test's exact ordering requirement.
      expectCustomersRequest().flush(pagedResult([]));
      await settle();

      expect(deactivateDialog()).toBeUndefined();
      expect(toastSpy).toHaveBeenCalledWith('Customer deactivated.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
    });

    it('a failed deactivate keeps the dialog open and shows an error message instead of the normal one', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const confirmButton = Array.from<HTMLButtonElement>(
        deactivateDialog()!.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9/deactivate');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/not-found',
          title: 'Not Found',
          status: 404,
          detail: "Customer 'c9' was not found.",
        },
        { status: 404, statusText: 'Not Found' },
      );
      await settle();

      const dialog = deactivateDialog();
      expect(dialog).not.toBeUndefined();
      expect(dialog!.textContent).not.toContain('This customer will disappear from default listings');
      expect(dialog!.textContent).toContain("Customer 'c9' was not found.");
    });
  });

  describe('show inactive toggle', () => {
    function showInactiveCheckbox(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[type="checkbox"]');
    }

    it('passes showInactive=false to the query by default', async () => {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
    });

    it('passes showInactive=true to the query once the toggle is checked', async () => {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectCustomersRequest('', 'true').flush(pagedResult([]));
    });
  });

  describe('restore row action', () => {
    function showInactiveCheckbox(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[type="checkbox"]');
    }

    async function seedActiveAndInactiveRows(): Promise<void> {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectCustomersRequest('', 'true').flush(
        pagedResult([
          customerDto({ id: 'active-1', firstName: 'Active', isDeleted: false }),
          customerDto({ id: 'inactive-1', firstName: 'Inactive', isDeleted: true }),
        ]),
      );
      await settle();
    }

    it('an inactive row shows Restore and Erase and is dimmed; an active row keeps Edit/Delete/Deactivate/Erase and is not dimmed', async () => {
      await seedActiveAndInactiveRows();

      const rows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(rows.length).toBe(2);

      const activeButtons = Array.from<HTMLButtonElement>(rows[0].querySelectorAll('button')).map(
        (b) => b.textContent?.trim(),
      );
      const inactiveButtons = Array.from<HTMLButtonElement>(
        rows[1].querySelectorAll('button'),
      ).map((b) => b.textContent?.trim());

      expect(activeButtons).toEqual(['Edit', 'Delete', 'Deactivate', 'Erase personal data']);
      expect(inactiveButtons).toEqual(['Restore', 'Erase personal data']);

      const activeCell = rows[0].querySelector('td') as HTMLElement;
      const inactiveCell = rows[1].querySelector('td') as HTMLElement;
      expect(activeCell.className).toContain('text-text-body');
      expect(inactiveCell.className).toContain('text-text-disabled');
    });

    it('clicking Restore on a row that succeeds clears any error state and shows a success toast', async () => {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectCustomersRequest('', 'true').flush(
        pagedResult([customerDto({ id: 'c5', firstName: 'Five', isDeleted: true })]),
      );
      await settle();

      const toastSpy = vi.spyOn(toastService, 'success');

      const restoreButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Restore');
      restoreButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c5/restore');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      // Invalidation triggers a refetch of the currently-mounted (showInactive=true) list query.
      expectCustomersRequest('', 'true').flush(pagedResult([]));
      await settle();

      expect(toastSpy).toHaveBeenCalledWith('Customer restored.');
      expect(fixture.nativeElement.textContent).not.toContain('Already active');
    });

    it("clicking Restore on a row that fails (409) sets that specific row's error state only, even with multiple inactive rows", async () => {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectCustomersRequest('', 'true').flush(
        pagedResult([
          customerDto({ id: 'c10', firstName: 'Ten', isDeleted: true }),
          customerDto({ id: 'c11', firstName: 'Eleven', isDeleted: true }),
        ]),
      );
      await settle();

      const restoreButtons = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).filter((b) => b.textContent?.trim() === 'Restore');
      expect(restoreButtons.length).toBe(2);

      restoreButtons[0].click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c10/restore');
      req.flush(
        {
          type: 'urn:bruno:customer:is-deleted',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'Already active.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      const rows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(rows[0].textContent).toContain('Already active.');
      expect(rows[1].textContent).not.toContain('Already active.');
    });
  });

  describe('erase row action', () => {
    function alertDialogs(): HTMLElement[] {
      return Array.from(fixture.nativeElement.querySelectorAll('[role="alertdialog"]'));
    }

    function eraseDialog(): HTMLElement | undefined {
      return alertDialogs().find((d) => d.textContent?.includes("Erase this customer's personal data?"));
    }

    async function seedOneRowAndOpenEraseDialog(): Promise<void> {
      await settle();
      expectCustomersRequest('').flush(
        pagedResult([customerDto({ id: 'c9', firstName: 'Nomvula', email: 'nomvula@example.com' })]),
      );
      await settle();

      const eraseButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Erase personal data');
      expect(eraseButton).toBeTruthy();

      eraseButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Erase personal data" on a row opens the destructive ConfirmDialog for that customer', async () => {
      await seedOneRowAndOpenEraseDialog();

      const dialog = eraseDialog();
      expect(dialog).not.toBeUndefined();
      expect(dialog!.textContent).toContain('permanently');
      expect(dialog!.textContent).toContain('cannot be undone');
      expect(dialog!.textContent).toContain('booking history stays intact');
      expect(dialog!.querySelector('[data-testid="confirm-dialog-destructive-confirm"]')).not.toBeNull();
    });

    it('cancelling closes the dialog and never calls the mutation', async () => {
      await seedOneRowAndOpenEraseDialog();

      const cancelButton = Array.from<HTMLButtonElement>(
        eraseDialog()!.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Cancel');
      cancelButton!.click();
      fixture.detectChanges();

      expect(eraseDialog()).toBeUndefined();
      httpMock.expectNone((req) => req.url === '/api/customers/c9/anonymize');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and invalidates the list', async () => {
      await seedOneRowAndOpenEraseDialog();

      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const confirmButton = eraseDialog()!.querySelector(
        '[data-testid="confirm-dialog-destructive-confirm"]',
      ) as HTMLButtonElement;
      confirmButton.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9/anonymize');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      expectCustomersRequest().flush(pagedResult([]));
      await settle();

      expect(eraseDialog()).toBeUndefined();
      expect(toastSpy).toHaveBeenCalledWith("Customer's personal data erased.");
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
    });

    it('a failed erase keeps the dialog open and shows the exact message instead of the normal copy', async () => {
      await seedOneRowAndOpenEraseDialog();

      const confirmButton = eraseDialog()!.querySelector(
        '[data-testid="confirm-dialog-destructive-confirm"]',
      ) as HTMLButtonElement;
      confirmButton.click();
      await settle();

      const req = httpMock.expectOne('/api/customers/c9/anonymize');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/not-found',
          title: 'Not Found',
          status: 404,
          detail: "Customer 'c9' was not found.",
        },
        { status: 404, statusText: 'Not Found' },
      );
      await settle();

      const dialog = eraseDialog();
      expect(dialog).not.toBeUndefined();
      expect(dialog!.textContent).not.toContain('permanently');
      expect(dialog!.textContent).toContain("Customer 'c9' was not found.");
    });
  });

  describe('anonymized row rendering', () => {
    function showInactiveCheckbox(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[type="checkbox"]');
    }

    it('shows an anonymized customer with muted-italic placeholder text and no row actions when "show inactive" is on', async () => {
      await settle();
      expectCustomersRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectCustomersRequest('', 'true').flush(
        pagedResult([
          customerDto({
            id: 'anon-1',
            firstName: 'Anonymized',
            lastName: 'Customer',
            email: 'erased-anon-1@anonymized.local',
            phoneNumber: '0000000000',
            isDeleted: true,
            isAnonymized: true,
          }),
        ]),
      );
      await settle();

      const row = fixture.nativeElement.querySelector('tbody tr') as HTMLElement;
      expect(row.textContent).toContain('Customer (anonymized)');
      expect(row.querySelectorAll('button').length).toBe(0);

      const nameCell = row.querySelector('td') as HTMLElement;
      expect(nameCell.className).toContain('italic');
      expect(nameCell.className).toContain('text-anonymized-text');
    });
  });
});
