import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';
import { BehaviorSubject } from 'rxjs';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { VehicleDto } from '../../core/models/vehicle-dto';
import { currencyFormatter, dateFormatter } from './vehicle-formatters';
import { VehicleDetailPage } from './vehicle-detail-page';

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

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('VehicleDetailPage', () => {
  let fixture: ComponentFixture<VehicleDetailPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;

  function createComponent(id: string): void {
    const paramMap$ = new BehaviorSubject(convertToParamMap({ id }));

    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    TestBed.configureTestingModule({
      imports: [VehicleDetailPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$ } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(VehicleDetailPage);
    httpMock = TestBed.inject(HttpTestingController);
  }

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  it('renders Skeleton in a card container while the request is in flight, not a table', async () => {
    createComponent('v1');
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
  });

  it('renders "This vehicle no longer exists" and a working link back to the list on a 404', async () => {
    createComponent('missing-id');
    await settle();

    const req = httpMock.expectOne('/api/vehicles/missing-id');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/not-found',
        title: 'Not Found',
        status: 404,
        detail: "Vehicle 'missing-id' was not found.",
      },
      { status: 404, statusText: 'Not Found' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain('This vehicle no longer exists');
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a');
    expect(link).not.toBeNull();
    expect(link.getAttribute('href')).toBe('/vehicles');
  });

  it('renders the full record for an active vehicle, showing "Active"', async () => {
    createComponent('v1');
    await settle();

    httpMock.expectOne('/api/vehicles/v1').flush(
      vehicleDto({
        registrationNumber: 'CA111111',
        make: 'Toyota',
        model: 'Corolla',
        year: 2022,
        dailyRate: 350,
        createdDate: '2026-01-15T10:30:00Z',
        isDeleted: false,
      }),
    );
    await settle();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('CA111111');
    expect(text).toContain('Toyota');
    expect(text).toContain('Corolla');
    expect(text).toContain('2022');
    expect(text).toContain(currencyFormatter.format(350));
    expect(text).toContain(dateFormatter.format(new Date('2026-01-15T10:30:00Z')));
    expect(text).toContain('Active');
    expect(text).not.toContain('Inactive');
  });

  it('renders "Inactive" in dimmed styling for a soft-deleted vehicle', async () => {
    createComponent('v2');
    await settle();

    httpMock
      .expectOne('/api/vehicles/v2')
      .flush(vehicleDto({ id: 'v2', registrationNumber: 'CA222222', isDeleted: true }));
    await settle();

    expect(fixture.nativeElement.textContent).toContain('Inactive');

    const statusEl = Array.from<HTMLElement>(
      fixture.nativeElement.querySelectorAll('dd'),
    ).find((el) => el.textContent?.trim() === 'Inactive');
    expect(statusEl).toBeTruthy();
    expect(statusEl!.className).toContain('text-text-disabled');
  });

  it('renders a generic fallback message for a non-not-found error, never a blank page', async () => {
    createComponent('v3');
    await settle();

    httpMock.expectOne('/api/vehicles/v3').flush('boom', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await settle();

    expect(fixture.nativeElement.textContent?.trim().length).toBeGreaterThan(0);
    expect(fixture.nativeElement.textContent).not.toContain('This vehicle no longer exists');
  });
});
