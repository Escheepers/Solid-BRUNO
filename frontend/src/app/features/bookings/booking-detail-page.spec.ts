import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';
import { BehaviorSubject } from 'rxjs';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { BookingDetailPage } from './booking-detail-page';
import { currencyFormatter } from './booking-formatters';

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

describe('BookingDetailPage', () => {
  let fixture: ComponentFixture<BookingDetailPage>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  function createComponent(id: string): void {
    const paramMap$ = new BehaviorSubject(convertToParamMap({ id }));

    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    TestBed.configureTestingModule({
      imports: [BookingDetailPage],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
        { provide: ActivatedRoute, useValue: { paramMap: paramMap$ } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BookingDetailPage);
    httpMock = TestBed.inject(HttpTestingController);
    toastService = TestBed.inject(ToastService);
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

  it('renders Skeleton in a card container while the request is in flight, not a record', async () => {
    createComponent('b1');
    await settle();

    expect(fixture.nativeElement.querySelector('app-skeleton')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('dl')).toBeNull();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto());
  });

  it('renders "This booking no longer exists" and a working link back to the list on a 404', async () => {
    createComponent('missing-id');
    await settle();

    const req = httpMock.expectOne('/api/bookings/missing-id');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/not-found',
        title: 'Not Found',
        status: 404,
        detail: "Booking 'missing-id' was not found.",
      },
      { status: 404, statusText: 'Not Found' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain('This booking no longer exists');
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();

    const link: HTMLAnchorElement = fixture.nativeElement.querySelector('a');
    expect(link).not.toBeNull();
    expect(link.getAttribute('href')).toBe('/bookings');
  });

  it('shows vehicle, customer, dates, TotalPrice, and a Status Badge for a valid booking', async () => {
    createComponent('b1');
    await settle();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto());
    await settle();

    const text = fixture.nativeElement.textContent;
    expect(text).toContain('Toyota Corolla — CA123456');
    expect(text).toContain('Thabo Nkosi');
    expect(text).toContain(currencyFormatter.format(1400));

    const badge = fixture.nativeElement.querySelector('app-badge');
    expect(badge).not.toBeNull();
    expect(badge.textContent).toContain('Active');
  });

  it('renders "Customer (anonymized)" when the booking\'s customer has since been anonymized', async () => {
    createComponent('b1');
    await settle();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ customerIsAnonymized: true }));
    await settle();

    expect(fixture.nativeElement.textContent).toContain('Customer (anonymized)');
    expect(fixture.nativeElement.textContent).not.toContain('Thabo Nkosi');
  });

  it("links the Customer field to that customer's Summary page (spec-5-1)", async () => {
    createComponent('b1');
    await settle();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ customerId: 'c9' }));
    await settle();

    const links: HTMLAnchorElement[] = Array.from(fixture.nativeElement.querySelectorAll('a'));
    const customerLink = links.find((a) => a.textContent?.includes('Thabo Nkosi'));
    expect(customerLink).toBeTruthy();
    expect(customerLink!.getAttribute('href')).toBe('/customers/c9/summary');
  });

  it("still links the Customer field to the Summary page for an anonymized customer", async () => {
    createComponent('b1');
    await settle();

    httpMock.expectOne('/api/bookings/b1').flush(
      bookingDto({ customerId: 'c9', customerIsAnonymized: true }),
    );
    await settle();

    const links: HTMLAnchorElement[] = Array.from(fixture.nativeElement.querySelectorAll('a'));
    const customerLink = links.find((a) => a.textContent?.includes('Customer (anonymized)'));
    expect(customerLink).toBeTruthy();
    expect(customerLink!.getAttribute('href')).toBe('/customers/c9/summary');
  });

  it('renders a generic fallback message for a non-not-found error, never a blank page', async () => {
    createComponent('b3');
    await settle();

    httpMock.expectOne('/api/bookings/b3').flush('boom', {
      status: 500,
      statusText: 'Internal Server Error',
    });
    await settle();

    expect(fixture.nativeElement.textContent?.trim().length).toBeGreaterThan(0);
    expect(fixture.nativeElement.textContent).not.toContain('This booking no longer exists');
  });

  describe('Cancel action eligibility (must agree with BookingsPage\'s own isCancellable)', () => {
    function cancelButton(): HTMLButtonElement | undefined {
      return Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).find(
        (b) => b.textContent?.trim() === 'Cancel Booking',
      );
    }

    it('shows Cancel for an Active booking with a future EndDate', async () => {
      createComponent('b1');
      await settle();
      httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ status: 'Active', endDate: '2099-01-05' }));
      await settle();

      expect(cancelButton()).toBeTruthy();
    });

    it('hides Cancel for a Completed booking', async () => {
      createComponent('b1');
      await settle();
      httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ status: 'Completed' }));
      await settle();

      expect(cancelButton()).toBeFalsy();
    });

    it('hides Cancel for a Cancelled booking', async () => {
      createComponent('b1');
      await settle();
      httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ status: 'Cancelled' }));
      await settle();

      expect(cancelButton()).toBeFalsy();
    });

    it('hides Cancel for a still-Active booking whose EndDate is already in the past', async () => {
      createComponent('b1');
      await settle();
      httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ status: 'Active', endDate: '2020-01-05' }));
      await settle();

      expect(cancelButton()).toBeFalsy();
    });
  });

  describe('cancel flow', () => {
    function alertDialog(): HTMLElement | null {
      return fixture.nativeElement.querySelector('[role="alertdialog"]');
    }

    async function seedCancellableBookingAndOpenCancelDialog(): Promise<void> {
      createComponent('b9');
      await settle();
      httpMock
        .expectOne('/api/bookings/b9')
        .flush(bookingDto({ id: 'b9', status: 'Active', endDate: '2099-01-05' }));
      await settle();

      const cancelButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('button'),
      ).find((b) => b.textContent?.trim() === 'Cancel Booking');
      expect(cancelButton).toBeTruthy();

      cancelButton!.click();
      fixture.detectChanges();
    }

    it('clicking "Cancel Booking" opens the ConfirmDialog for this booking', async () => {
      await seedCancellableBookingAndOpenCancelDialog();

      const dialog = alertDialog();
      expect(dialog).not.toBeNull();
      expect(dialog!.textContent).toContain('Cancel this booking?');
      expect(dialog!.textContent).toContain('stay in records as Cancelled');
    });

    it('cancelling (keeping the booking) closes the dialog and never calls the mutation', async () => {
      await seedCancellableBookingAndOpenCancelDialog();

      const keepButton = Array.from<HTMLButtonElement>(
        fixture.nativeElement.querySelectorAll('[role="alertdialog"] button'),
      ).find((b) => b.textContent?.trim() === 'Keep booking');
      keepButton!.click();
      fixture.detectChanges();

      expect(alertDialog()).toBeNull();
      httpMock.expectNone((req) => req.url === '/api/bookings/b9/cancel');
    });

    it('confirming calls the mutation and, on success, closes the dialog, toasts, and refreshes this booking', async () => {
      await seedCancellableBookingAndOpenCancelDialog();

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

      // The mutation's own onSuccess invalidates ['bookings', 'list']; this component's
      // cancelAction additionally invalidates this booking's own ['bookings', 'detail', id]
      // so the page refetches and reflects the new Cancelled status.
      httpMock.expectOne('/api/bookings/b9').flush(bookingDto({ id: 'b9', status: 'Cancelled' }));
      await settle();

      expect(alertDialog()).toBeNull();
      expect(toastSpy).toHaveBeenCalledWith('Booking cancelled.');

      const badge = fixture.nativeElement.querySelector('app-badge');
      expect(badge.textContent).toContain('Cancelled');
      expect(
        Array.from<HTMLButtonElement>(fixture.nativeElement.querySelectorAll('button')).find(
          (b) => b.textContent?.trim() === 'Cancel Booking',
        ),
      ).toBeFalsy();
    });

    it('a failed (409, stale UI/race) cancel keeps the dialog open and shows the exact message instead of the normal copy', async () => {
      await seedCancellableBookingAndOpenCancelDialog();

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
