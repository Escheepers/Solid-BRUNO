import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { PagedResult } from '../../core/models/paged-result';
import { VehicleDto } from '../../core/models/vehicle-dto';
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

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [VehiclesPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(new QueryClient({ defaultOptions: { queries: { retry: false } } })),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VehiclesPage);
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

  function expectVehiclesRequest(matchSearch?: string) {
    return httpMock.expectOne(
      (req) =>
        req.url === '/api/vehicles' &&
        (matchSearch === undefined || req.params.get('search') === matchSearch),
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
});
