import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { PagedResult } from '../../core/models/paged-result';
import { ToastService } from '../../shared/toast/toast.service';
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
  let toastService: ToastService;

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
        provideRouter([]),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BookingsPage);
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
    // Every row always gets a "View" action (spec-4-5), so DataTable renders its trailing
    // (sr-only-labelled) Actions column regardless of this row's own Cancel eligibility.
    expect(headers).toEqual(['Vehicle', 'Customer', 'Start', 'End', 'Total', 'Status', 'Actions']);

    const row = fixture.nativeElement.querySelector('tbody tr');
    expect(row.textContent).toContain('Toyota Corolla — CA123456');
    expect(row.textContent).toContain('Thabo Nkosi');
    expect(row.textContent).toContain(currencyFormatter.format(1400));
  });

  it('offers a Cancel action for a future Active booking (spec-4-3)', async () => {
    await settle();
    expectBookingsRequest().flush(
      pagedResult([bookingDto({ status: 'Active', endDate: '2099-01-05' })]),
    );
    flushPickerQueries();
    await settle();

    const cancelButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).find((b) => b.textContent?.trim() === 'Cancel');
    expect(cancelButton).toBeTruthy();
  });

  /**
   * spec-4-5 adds an always-present "View" action to every row (regardless of Cancel
   * eligibility), so these three cases -- previously "no row action at all" -- now
   * assert "View, but never Cancel" instead. The eligibility rule itself
   * (`isCancellable`, moved to `models/booking.ts` by this same story) is unchanged;
   * only the row's total action set grew.
   */
  it('offers View but never Cancel for a Completed booking', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ status: 'Completed' })]));
    flushPickerQueries();
    await settle();

    const buttons = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['View']);
  });

  it('offers View but never Cancel for a Cancelled booking', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ status: 'Cancelled' })]));
    flushPickerQueries();
    await settle();

    const buttons = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['View']);
  });

  it('offers View but never Cancel for a still-Active booking whose EndDate is already in the past', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ status: 'Active', endDate: '2020-01-05' })]));
    flushPickerQueries();
    await settle();

    const buttons = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).map((b) => b.textContent?.trim());
    expect(buttons).toEqual(['View']);
  });

  it('clicking "View" on a row navigates to that booking\'s detail route', async () => {
    await settle();
    expectBookingsRequest().flush(pagedResult([bookingDto({ id: 'b7' })]));
    flushPickerQueries();
    await settle();

    const router = TestBed.inject(Router);
    const navigateSpy = vi.spyOn(router, 'navigate');

    const viewButton = Array.from<HTMLButtonElement>(
      fixture.nativeElement.querySelectorAll('tbody button'),
    ).find((b) => b.textContent?.trim() === 'View');
    expect(viewButton).toBeTruthy();

    viewButton!.click();
    fixture.detectChanges();

    expect(navigateSpy).toHaveBeenCalledWith(['/bookings', 'b7']);
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

  describe('cancel row action', () => {
    function alertDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="alertdialog"]');
    }

    async function seedOneCancellableRowAndOpenCancelDialog(): Promise<void> {
      await settle();
      expectBookingsRequest().flush(
        pagedResult([bookingDto({ id: 'b9', status: 'Active', endDate: '2099-01-05' })]),
      );
      flushPickerQueries();
      await settle();

      const cancelButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('tbody button'),
      ).find((b) => b.textContent?.trim() === 'Cancel');
      expect(cancelButton).toBeTruthy();

      cancelButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Cancel" on a row opens the ConfirmDialog for that booking', async () => {
      await seedOneCancellableRowAndOpenCancelDialog();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).toContain('Cancel this booking?');
      expect(dialog!.textContent).toContain('stay in records as Cancelled');
      expect(dialog!.textContent).toContain('not reversible');
    });

    it('cancelling (keeping the booking) closes the dialog and never calls the mutation', async () => {
      await seedOneCancellableRowAndOpenCancelDialog();

      const keepButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Keep booking');
      keepButton!.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      httpMock.expectNone((req) => req.url === '/api/bookings/b9/cancel');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and invalidates the list', async () => {
      await seedOneCancellableRowAndOpenCancelDialog();

      const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
      const toastSpy = vi.spyOn(toastService, 'success');

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Cancel booking');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/bookings/b9/cancel');
      expect(req.request.method).toBe('POST');
      req.flush(null, { status: 204, statusText: 'No Content' });
      await settle();

      // `invalidateQueries()` (called from `useCancelBookingMutation`'s own `onSuccess`)
      // awaits the active list query's refetch before it resolves -- mirrors
      // `CustomersPage`'s Deactivate test's exact ordering requirement.
      expectBookingsRequest().flush(pagedResult([]));
      await settle();

      expect(alertDialog()).toBeNull();
      expect(toastSpy).toHaveBeenCalledWith('Booking cancelled.');
      expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
    });

    it('a failed (409, stale UI/race) cancel keeps the dialog open and shows the exact message instead of the normal copy', async () => {
      await seedOneCancellableRowAndOpenCancelDialog();

      const confirmButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Cancel booking');
      confirmButton!.click();
      await settle();

      const req = httpMock.expectOne('/api/bookings/b9/cancel');
      req.flush(
        {
          type: 'urn:bruno:booking:already-completed',
          title: 'A domain rule was violated.',
          status: 409,
          detail: 'Cannot cancel — booking already completed.',
        },
        { status: 409, statusText: 'Conflict' },
      );
      await settle();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).not.toContain('stay in records as Cancelled');
      expect(dialog!.textContent).toContain('Cannot cancel — booking already completed.');
    });
  });
});
