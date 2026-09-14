import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerDto } from '../../core/models/customer-dto';
import { PagedResult } from '../../core/models/paged-result';
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

  function expectCustomersRequest(matchSearch?: string) {
    return httpMock.expectOne(
      (req) =>
        req.url === '/api/customers' &&
        (matchSearch === undefined || req.params.get('search') === matchSearch),
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
});
