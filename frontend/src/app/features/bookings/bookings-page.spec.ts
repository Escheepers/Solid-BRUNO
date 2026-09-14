import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { PagedResult } from '../../core/models/paged-result';
import { currencyFormatter } from './booking-formatters';
import { BookingsPage } from './bookings-page';

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

function pagedResult(
  items: BookingDto[],
  overrides: Partial<PagedResult<BookingDto>> = {},
): PagedResult<BookingDto> {
  return { items, totalCount: items.length, page: 1, pageSize: 20, ...overrides };
}

/** Matches the macrotask-flush pattern established by `vehicles-page.spec.ts`/
 * `customers-page.spec.ts` for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('BookingsPage', () => {
  let fixture: ComponentFixture<BookingsPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;

  beforeEach(async () => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [BookingsPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BookingsPage);
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

  /** The vehicle/customer picker queries inside the always-mounted
   * `BookingFormModal` fire immediately too -- flushed here so `httpMock.verify()`
   * doesn't fail on an unhandled request for tests that don't care about them. */
  function flushPickerQueries(): void {
    httpMock.expectOne((r) => r.url === '/api/vehicles').flush({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 200,
    });
    httpMock.expectOne((r) => r.url === '/api/customers').flush({
      items: [],
      totalCount: 0,
      page: 1,
      pageSize: 200,
    });
  }

  function expectBookingsRequest() {
    return httpMock.expectOne((req) => req.url === '/api/bookings');
  }

  it('shows Skeleton rows (via DataTable) while the first request is in flight', async () => {
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('table')).toBeNull();

    expectBookingsRequest().flush(pagedResult([]));
    flushPickerQueries();
    await settle();
  });

  it('shows "No bookings yet" when no bookings exist', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([]));
    flushPickerQueries();
    await settle();

    expect(fixture.nativeElement.textContent).toContain('No bookings yet');
    expect(fixture.nativeElement.querySelector('table')).toBeNull();
  });

  it('renders a populated DataTable with Vehicle/Customer/Start/End/Total/Status columns', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto()]));
    flushPickerQueries();
    await settle();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th) =>
      (th as HTMLElement).textContent?.trim(),
    );
    expect(headers).toEqual(['Vehicle', 'Customer', 'Start', 'End', 'Total', 'Status']);

    const row = fixture.nativeElement.querySelector('tbody tr');
    expect(row.textContent).toContain('Toyota Corolla — CA123456');
    expect(row.textContent).toContain('Thabo Nkosi');
    expect(row.textContent).toContain(currencyFormatter.format(1400));
  });

  it('renders no Actions column (Scope decision 5 -- read + create only)', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto()]));
    flushPickerQueries();
    await settle();

    const headers = Array.from(fixture.nativeElement.querySelectorAll('thead th')).map((th) =>
      (th as HTMLElement).textContent?.trim(),
    );
    expect(headers).not.toContain('Actions');
  });

  it('renders a real Badge component for the Status column', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ status: 'Active' })]));
    flushPickerQueries();
    await settle();

    const badge = fixture.nativeElement.querySelector('tbody tr app-badge');
    expect(badge).not.toBeNull();
    expect(badge.textContent).toContain('Active');
  });

  it('renders a Cancelled Badge for a cancelled booking', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ status: 'Cancelled' })]));
    flushPickerQueries();
    await settle();

    const badge = fixture.nativeElement.querySelector('tbody tr app-badge');
    expect(badge.textContent).toContain('Cancelled');
  });

  it('renders the anonymized-customer placeholder and styling for a booking whose customer is anonymized', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ customerIsAnonymized: true })]));
    flushPickerQueries();
    await settle();

    const row = fixture.nativeElement.querySelector('tbody tr');
    expect(row.textContent).toContain('Customer (anonymized)');

    const customerCell = row.querySelectorAll('td')[1];
    expect(customerCell.className).toContain('text-anonymized-text');
  });

  it('opens BookingFormModal when "+ New Booking" is clicked', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([]));
    flushPickerQueries();
    await settle();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();

    const newBookingButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === '+ New Booking')!;
    newBookingButton.click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();
  });

  it('closes BookingFormModal when it emits closeRequest', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([]));
    flushPickerQueries();
    await settle();

    const newBookingButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === '+ New Booking')!;
    newBookingButton.click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="dialog"]')).not.toBeNull();

    fixture.nativeElement.querySelector('button[aria-label="Close"]').click();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('[role="dialog"]')).toBeNull();
  });

  it('requests the next page when DataTable emits pageChange', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto()], { totalCount: 100 }));
    flushPickerQueries();
    await settle();

    const nextButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('button'),
    ).find((b) => b.textContent?.trim() === 'Next')!;
    nextButton.click();
    await settle();

    const req = httpMock.expectOne((r) => r.url === '/api/bookings' && r.params.get('page') === '2');
    req.flush(pagedResult([bookingDto()], { totalCount: 100, page: 2 }));
    await settle();
  });
});
