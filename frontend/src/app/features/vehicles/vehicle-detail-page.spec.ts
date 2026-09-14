import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';
import { BehaviorSubject } from 'rxjs';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
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

function bookingDto(overrides: Partial<BookingDto> = {}): BookingDto {
  return {
    id: 'b1',
    vehicleId: 'v1',
    vehicleMake: 'Toyota',
    vehicleModel: 'Corolla',
    vehicleRegistrationNumber: 'CA123456',
    customerId: 'c1',
    customerFirstName: 'Thabo',
    customerLastName: 'Nkosi',
    customerIsAnonymized: false,
    startDate: '2026-10-01',
    endDate: '2026-10-05',
    totalPrice: 1400,
    status: 'Active',
    createdDate: '2026-09-20T10:30:00Z',
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

  /**
   * spec-4-5's booking-history section is an unconditional class field (`bookingsQuery`),
   * so it fires immediately alongside the vehicle's own detail request regardless of which
   * template branch (loading/not-found/found) ends up rendered -- mirrors
   * `BookingFormModal`'s own always-mounted picker queries (see `bookings-page.spec.ts`'s
   * `flushPickerQueries`). Flushed here in every test so `httpMock.verify()` doesn't fail
   * on an unhandled request for tests that don't care about it.
   */
  function flushBookingHistoryRequest(items: BookingDto[] = []): void {
    httpMock
      .expectOne((r) => r.url === '/api/bookings')
      .flush({ items, totalCount: items.length, page: 1, pageSize: 100 });
  }

  it('renders Skeleton in a card container while the request is in flight, not a table', async () => {
    createComponent('v1');
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
    flushBookingHistoryRequest();
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
    flushBookingHistoryRequest();
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
    flushBookingHistoryRequest();
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
    flushBookingHistoryRequest();
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
    flushBookingHistoryRequest();
    await settle();

    expect(fixture.nativeElement.textContent?.trim().length).toBeGreaterThan(0);
    expect(fixture.nativeElement.textContent).not.toContain('This vehicle no longer exists');
  });

  describe('booking-history section (spec-4-5)', () => {
    it('requests bookings filtered by this vehicle\'s id, with a generously large pageSize', async () => {
      createComponent('v1');
      await settle();

      httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
      const req = httpMock.expectOne(
        (r) => r.url === '/api/bookings' && r.params.get('vehicleId') === 'v1',
      );
      expect(req.request.params.get('pageSize')).toBe('100');
      req.flush({ items: [], totalCount: 0, page: 1, pageSize: 100 });
      await settle();
    });

    it('shows "No bookings yet for this vehicle" when the vehicle has none', async () => {
      createComponent('v1');
      await settle();

      httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
      flushBookingHistoryRequest([]);
      await settle();

      expect(fixture.nativeElement.textContent).toContain('No bookings yet for this vehicle');
      expect(fixture.nativeElement.querySelector('table')).toBeNull();
    });

    it('lists real bookings for this vehicle, reusing DataTable/Badge exactly as BookingsPage does', async () => {
      createComponent('v1');
      await settle();

      httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
      flushBookingHistoryRequest([bookingDto(), bookingDto({ id: 'b2', status: 'Completed' })]);
      await settle();

      const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th) =>
        (th as HTMLElement).textContent?.trim(),
      );
      expect(headers).toEqual(['Customer', 'Start', 'End', 'Total', 'Status']);

      const rows = fixture.nativeElement.querySelectorAll('tbody tr');
      expect(rows.length).toBe(2);
      expect(rows[0].textContent).toContain('Thabo Nkosi');
      expect(rows[0].textContent).toContain(currencyFormatter.format(1400));

      const badges = fixture.nativeElement.querySelectorAll('tbody tr app-badge');
      expect(badges.length).toBe(2);
      expect(badges[0].textContent).toContain('Active');
      expect(badges[1].textContent).toContain('Completed');
    });

    it('renders no row actions in the booking-history table (Never section: view-only)', async () => {
      createComponent('v1');
      await settle();

      httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
      flushBookingHistoryRequest([bookingDto()]);
      await settle();

      expect(fixture.nativeElement.querySelectorAll('tbody button').length).toBe(0);
    });

    it('renders the anonymized-customer placeholder for a booking whose customer is anonymized', async () => {
      createComponent('v1');
      await settle();

      httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto());
      flushBookingHistoryRequest([bookingDto({ customerIsAnonymized: true })]);
      await settle();

      const row = fixture.nativeElement.querySelector('tbody tr');
      expect(row.textContent).toContain('Customer (anonymized)');
    });
  });
});
