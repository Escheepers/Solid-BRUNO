import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { PagedResult } from '../../core/models/paged-result';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { VehicleFormModal } from './vehicle-form-modal';
import { VehiclesPage } from './vehicles-page';

function vehicleDto(overrides: Partial<VehicleDto> = {}): VehicleDto {
  return {
    id: 'v1',
    registrationNumber: 'CA123456',
    make: 'Toyota',
    model: 'Corolla',
    year: 2022,
    dailyRate: 350,
    createdDate: '2026-01-15T10:30:00Z',
    isDeleted: false,
    ...overrides,
  };
}

function pagedResult(
  items: VehicleDto[],
  overrides: Partial<PagedResult<VehicleDto>> = {},
): PagedResult<VehicleDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 20, ...overrides };
}

/**
 * Matches the same macrotask-flush pattern established by `top-app-bar.spec.ts`:
 * some framework-internal work (here, TanStack Query's Angular `effect()`-based
 * reactivity, which schedules asynchronously) needs a real event-loop turn to run,
 * not just a resolved microtask.
 */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('VehiclesPage', () => {
  let fixture: ComponentFixture<VehiclesPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [VehiclesPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VehiclesPage);
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

  function expectVehiclesRequest(matchSearch?: string, matchShowInactive?: string) {
    return httpMock.expectOne(
      (req) =>
        req.url === '/api/vehicles' &&
        (matchSearch === undefined || req.params.get('search') === matchSearch) &&
        (matchShowInactive === undefined || req.params.get('showInactive') === matchShowInactive),
    );
  }

  it('shows Skeleton rows (via DataTable) while the first request is in flight', async () => {
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    expectVehiclesRequest().flush(pagedResult([]));
  });

  it('shows "No vehicles yet" when no vehicles exist and no search filter is active', async () => {
    await settle();
    expectVehiclesRequest('').flush(pagedResult([]));
    await settle();

    expect(fixture.nativeElement.textContent).toContain('No vehicles yet');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();
  });

  it('renders a populated DataTable with one row per vehicle once data resolves', async () => {
    await settle();
    expectVehiclesRequest('').flush(
      pagedResult([
        vehicleDto({ id: 'v1', registrationNumber: 'CA111111' }),
        vehicleDto({ id: 'v2', registrationNumber: 'CA222222' }),
      ]),
    );
    await settle();

    const rows = fixture.nativeElement.querySelectorAll('tbody tr');
    expect(rows.length).toBe(2);
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('CA111111');
    expect(fixture.nativeElement.textContent).toContain('CA222222');
  });

  it(
    'shows "No vehicles match these filters" with a working Clear all filters action ' +
      'when a search yields zero results',
    async () => {
      await settle();
      expectVehiclesRequest('').flush(pagedResult([vehicleDto()]));
      await settle();

      setSearch('zzz-nomatch');
      fixture.detectChanges();

      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();

      expectVehiclesRequest('zzz-nomatch').flush(pagedResult([]));
      await settle();

      expect(fixture.nativeElement.textContent).toContain('No vehicles match these filters');
      const clearButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.includes('Clear all filters'));
      expect(clearButton).toBeTruthy();

      clearButton!.click();
      fixture.detectChanges();

      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();

      expectVehiclesRequest('').flush(pagedResult([vehicleDto()]));
    },
  );

  it('debounces the search input, issuing exactly one re-fetch ~300ms after the user stops typing', async () => {
    await settle();
    expectVehiclesRequest('').flush(pagedResult([vehicleDto()]));
    await settle();

    setSearch('t');
    fixture.detectChanges();
    setSearch('to');
    fixture.detectChanges();
    setSearch('toy');
    fixture.detectChanges();

    // No fetch yet — rapid keystrokes shouldn't trigger a request per keystroke.
    httpMock.expectNone((req) => req.url === '/api/vehicles' && req.params.get('search') === 'toy');

    await new Promise((resolve) => setTimeout(resolve, 350));
    await settle();

    expectVehiclesRequest('toy').flush(pagedResult([]));
  });

  it('resets to page 1 when the (debounced) search term changes', async () => {
    await settle();
    expectVehiclesRequest('').flush(pagedResult([vehicleDto()], { totalCount: 100 }));
    await settle();

    const nextButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Next');
    nextButton!.click();
    await settle();

    expectVehiclesRequest('').flush(pagedResult([vehicleDto()], { totalCount: 100, page: 2 }));
    await settle();

    setSearch('toyota');
    fixture.detectChanges();

    await new Promise((resolve) => setTimeout(resolve, 350));
    await settle();

    const req = expectVehiclesRequest('toyota');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(pagedResult([vehicleDto()]));
  });

  it('opens the Create Vehicle modal when "+ New Vehicle" is clicked', async () => {
    await settle();
    expectVehiclesRequest('').flush(pagedResult([]));
    await settle();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    const newVehicleButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === '+ New Vehicle');
    newVehicleButton!.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('clicking "Edit" on a row opens the modal pre-populated with that row’s data', async () => {
    await settle();
    expectVehiclesRequest('').flush(
      pagedResult([vehicleDto({ id: 'v7', registrationNumber: 'CA777777', make: 'Isuzu' })]),
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
    expect(dialog.textContent).toContain('Edit Vehicle');

    const registrationInput: HTMLInputElement = fixture.nativeElement.querySelector(
      'app-input input',
    );
    expect(registrationInput.value).toBe('CA777777');
  });

  it('clicking "View" on a row navigates to that vehicle\'s detail route', async () => {
    await settle();
    expectVehiclesRequest('').flush(
      pagedResult([vehicleDto({ id: 'v7', registrationNumber: 'CA777777' })]),
    );
    await settle();

    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate');

    const viewButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).find((b) => b.textContent?.trim() === 'View');
    expect(viewButton).toBeTruthy();

    viewButton!.click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith(['/vehicles', 'v7']);
  });

  describe('deactivate row action', () => {
    function alertDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="alertdialog"]');
    }

    async function seedOneRowAndOpenDeactivateDialog(): Promise<void> {
      await settle();
      expectVehiclesRequest('').flush(
        pagedResult([vehicleDto({ id: 'v9', registrationNumber: 'CA999000' })]),
      );
      await settle();

      const deactivateButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      expect(deactivateButton).toBeTruthy();

      deactivateButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Deactivate" on a row opens the ConfirmDialog for that vehicle', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).toContain('Deactivate this vehicle?');
    });

    it('cancelling closes the dialog and never calls the mutation', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const cancelButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Keep active');
      cancelButton!.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      httpMock.expectNone((req) => req.url === '/api/vehicles/v9/deactivate');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and invalidates the list', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/vehicles/v9/deactivate');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      /**
       * `invalidateQueries()` (called from `useDeactivateVehicleMutation`'s own
       * `onSuccess`) awaits the active list query's refetch before it resolves --
       * and `Mutation.execute()` awaits that same `onSuccess` call before dispatching
       * its own 'success' state. So the refetch this triggers must be flushed BEFORE
       * the mutation's per-call `onSuccess` (which closes the dialog/toasts) can run,
       * not afterwards.
       */
      expectVehiclesRequest().flush(pagedResult([]));
      await settle();

      expect(alertDialog()).toBeNull();
      expect(toastSpy).toHaveBeenCalledWith('Vehicle deactivated.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['vehicles', 'list'] });
    });

    it('a failed deactivate keeps the dialog open and shows an error message instead of the normal one', async () => {
      await seedOneRowAndOpenDeactivateDialog();

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Deactivate');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/vehicles/v9/deactivate');
      req.flush(
        {
          type: 'https://bruno-vehicle-hire/problems/not-found',
          title: 'Not Found',
          status: 404,
          detail: "Vehicle 'v9' was not found.",
        },
        { status: 404, statusText: 'Not Found' },
      );
      await settle();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).not.toContain(
        'This vehicle will disappear from availability searches',
      );
      // Story 2.5 gives `NormalizedApiError` a real `NotFoundError` kind for a 404 with
      // a valid ProblemDetails body -- `toErrorMessage` (a deliberately shallow mapping,
      // per the spec's Design Notes) shows its `detail` message rather than a generic
      // ServerError string.
      expect(dialog!.textContent).toContain("Vehicle 'v9' was not found.");
      expect(dialog!.querySelector('[role="alert"]')?.textContent).toContain("Vehicle 'v9' was not found.");
    });
  });

  describe('show inactive toggle', () => {
    function showInactiveCheckbox(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[type="checkbox"]');
    }

    it('passes showInactive=false to the query by default', async () => {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([]));
    });

    it('passes showInactive=true to the query once the toggle is checked', async () => {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectVehiclesRequest('', 'true').flush(pagedResult([]));
    });

    it('goes back to page 1 when the toggle changes, instead of keeping a page that may no longer exist', async () => {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([vehicleDto()], { totalCount: 100 }));
      await settle();

      nextButton().click();
      await settle();
      expectVehiclesRequest('', 'false').flush(
        pagedResult([vehicleDto()], { totalCount: 100, page: 2 }),
      );
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      const req = expectVehiclesRequest('', 'true');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(pagedResult([vehicleDto()]));
    });
  });

  function nextButton(): HTMLButtonElement {
    return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).find(
      (b) => b.textContent?.trim() === 'Next',
    )!;
  }

  describe('after a successful create', () => {
    function vehicleModal(): VehicleFormModal {
      return fixture.debugElement.query(By.directive(VehicleFormModal)).componentInstance;
    }

    it('goes back to page 1 so the newest-first list shows the new row', async () => {
      await settle();
      expectVehiclesRequest('').flush(pagedResult([vehicleDto()], { totalCount: 100 }));
      await settle();
      nextButton().click();
      await settle();
      expectVehiclesRequest('').flush(pagedResult([vehicleDto()], { totalCount: 100, page: 2 }));
      await settle();

      vehicleModal().created.emit();
      await settle();

      const req = expectVehiclesRequest('');
      expect(req.request.params.get('page')).toBe('1');
      req.flush(pagedResult([vehicleDto()], { totalCount: 100 }));
    });

    it('clears an active search so a new row that does not match it is not silently hidden', async () => {
      await settle();
      expectVehiclesRequest('').flush(pagedResult([vehicleDto()]));
      await settle();
      setSearch('toyota');
      fixture.detectChanges();
      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();
      expectVehiclesRequest('toyota').flush(pagedResult([vehicleDto()]));
      await settle();

      vehicleModal().created.emit();
      fixture.detectChanges();
      expect(searchInput().value).toBe('');

      await new Promise((resolve) => setTimeout(resolve, 350));
      await settle();
      expectVehiclesRequest('').flush(pagedResult([vehicleDto()]));
    });

    it('drops a column sort the user had clicked, so the new row is not buried by it', async () => {
      await settle();
      expectVehiclesRequest('').flush(
        pagedResult([vehicleDto({ id: 'v1' }), vehicleDto({ id: 'v2', registrationNumber: 'CA222222' })]),
      );
      await settle();

      const rateHeader = () =>
        Array.from<HTMLElement>(fixture.nativeElement.querySelectorAll('th')).find((th) =>
          th.textContent?.includes('Daily Rate'),
        )!;
      rateHeader().querySelector('button')!.click();
      fixture.detectChanges();
      expect(rateHeader().getAttribute('aria-sort')).toBe('ascending');

      vehicleModal().created.emit();
      await settle();

      expect(rateHeader().getAttribute('aria-sort')).toBe('none');
    });
  });

  it('moves back to the last valid page when the current page no longer exists (e.g. its last row was removed)', async () => {
    await settle();
    expectVehiclesRequest('').flush(pagedResult([vehicleDto()], { totalCount: 100 }));
    await settle();
    nextButton().click();
    await settle();

    // The result set shrank while the user was on page 2: no rows there any more, 20 in total.
    expectVehiclesRequest('').flush(pagedResult([], { totalCount: 20, page: 2 }));
    await settle();

    const req = expectVehiclesRequest('');
    expect(req.request.params.get('page')).toBe('1');
    req.flush(pagedResult([vehicleDto()], { totalCount: 20 }));
  });

  describe('restore row action', () => {
    function showInactiveCheckbox(): HTMLInputElement {
      return fixture.nativeElement.querySelector('input[type="checkbox"]');
    }

    async function seedActiveAndInactiveRows(): Promise<void> {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectVehiclesRequest('', 'true').flush(
        pagedResult([
          vehicleDto({ id: 'active-1', registrationNumber: 'CA111111', isDeleted: false }),
          vehicleDto({ id: 'inactive-1', registrationNumber: 'CA222222', isDeleted: true }),
        ]),
      );
      await settle();
    }

    it('an inactive row shows only Restore and is dimmed; an active row keeps Edit/Deactivate and is not dimmed', async () => {
      await seedActiveAndInactiveRows();

      const rows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(rows.length).toBe(2);

      const activeButtons = Array.from<HTMLButtonElement>(rows[0].querySelectorAll('button')).map(
        (b) => b.textContent?.trim(),
      );
      const inactiveButtons = Array.from<HTMLButtonElement>(
        rows[1].querySelectorAll('button'),
      ).map((b) => b.textContent?.trim());

      expect(activeButtons).toEqual(['View', 'Edit', 'Deactivate']);
      expect(inactiveButtons).toEqual(['View', 'Restore']);

      const activeCell = rows[0].querySelector('td') as HTMLElement;
      const inactiveCell = rows[1].querySelector('td') as HTMLElement;
      expect(activeCell.className).toContain('text-text-body');
      expect(inactiveCell.className).toContain('text-text-disabled');
    });

    it('clicking Restore on a row that succeeds clears any error state and shows a success toast', async () => {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectVehiclesRequest('', 'true').flush(
        pagedResult([vehicleDto({ id: 'v5', registrationNumber: 'CA555555', isDeleted: true })]),
      );
      await settle();

      const toastSpy = vi.spyOn(toastService, 'success');

      const restoreButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Restore');
      restoreButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/vehicles/v5/restore');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      // Invalidation triggers a refetch of the currently-mounted (showInactive=true) list query.
      expectVehiclesRequest('', 'true').flush(pagedResult([]));
      await settle();

      expect(toastSpy).toHaveBeenCalledWith('Vehicle restored.');
      expect(fixture.nativeElement.textContent).not.toContain('Already active');
    });

    it("clicking Restore on a row that fails (409) sets that specific row's error state only, even with multiple inactive rows", async () => {
      await settle();
      expectVehiclesRequest('', 'false').flush(pagedResult([]));
      await settle();

      showInactiveCheckbox().click();
      fixture.detectChanges();
      await settle();

      expectVehiclesRequest('', 'true').flush(
        pagedResult([
          vehicleDto({ id: 'v10', registrationNumber: 'CA101010', isDeleted: true }),
          vehicleDto({ id: 'v11', registrationNumber: 'CA111011', isDeleted: true }),
        ]),
      );
      await settle();

      const restoreButtons = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).filter((b) => b.textContent?.trim() === 'Restore');
      expect(restoreButtons.length).toBe(2);

      restoreButtons[0].click();
      await settle();

      const req = httpMock.expectOne('/api/vehicles/v10/restore');
      req.flush(
        {
          type: 'urn:bruno:vehicle:is-deleted',
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
});
