import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { ToastService } from '../../shared/toast/toast.service';
import { BookingEditModal } from './booking-edit-modal';
import { currencyFormatter } from './booking-formatters';
import { toBooking } from './models/booking';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
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
    endDate: '2026-10-05', // 4 days for 1400 => 350/day
    totalPrice: 1400,
    status: 'Active',
    createdDate: '2026-09-01T10:30:00Z',
    ...overrides,
  };
}

describe('BookingEditModal', () => {
  let fixture: ComponentFixture<BookingEditModal>;
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;
  let toastService: ToastService;

  beforeEach(async () => {
    // Pin "today" (Date only, so timers/microtasks stay real): the picker blocks past dates.
    vi.useFakeTimers({ toFake: ['Date'], now: new Date(2026, 8, 15, 12) });

    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    await TestBed.configureTestingModule({
      imports: [BookingEditModal],
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(BookingEditModal);
    fixture.componentRef.setInput('booking', toBooking(bookingDto()));
    fixture.componentRef.setInput('open', true);
    httpMock = TestBed.inject(HttpTestingController);
    toastService = TestBed.inject(ToastService);
  });

  afterEach(() => {
    httpMock.verify();
    vi.useRealTimers();
  });

  async function settle(): Promise<void> {
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
  }

  function dateInputs(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('input[type="date"]'));
  }

  function setValue(el: HTMLInputElement, value: string): void {
    el.value = value;
    el.dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function totalPrice(): string {
    return fixture.nativeElement.querySelector('[data-testid="total-price"]').textContent;
  }

  async function submitForm(): Promise<void> {
    fixture.nativeElement.querySelector('form').dispatchEvent(new Event('submit', { cancelable: true }));
    await settle();
  }

  it('pre-fills the form with the booking dates and its current price', async () => {
    await settle();

    const [start, end] = dateInputs();
    expect(start.value).toBe('2026-10-01');
    expect(end.value).toBe('2026-10-05');
    expect(totalPrice()).toContain(currencyFormatter.format(1400));
  });

  it('blocks past start dates and keeps the end date after the start', async () => {
    await settle();

    const [start, end] = dateInputs();
    expect(start.min).toBe('2026-09-15');
    expect(end.min).toBe('2026-10-02');

    setValue(start, '2026-10-10');
    expect(end.min).toBe('2026-10-11');
  });

  it('recomputes the price live at the booking original daily rate', async () => {
    await settle();

    const [, end] = dateInputs();
    setValue(end, '2026-10-08'); // 1 -> 8 Oct = 7 days x 350

    expect(totalPrice()).toContain(currencyFormatter.format(2450));
  });

  it('does not call the API when the start date is in the past, and shows why', async () => {
    await settle();

    const [start] = dateInputs();
    setValue(start, '2026-09-14');
    await submitForm();

    httpMock.expectNone('/api/bookings/b1');
    expect(fixture.nativeElement.textContent).toContain('Bookings cannot start in the past.');
  });

  it('PUTs the new dates, then toasts, emits updated and closes', async () => {
    await settle();
    const toastSpy = vi.spyOn(toastService, 'success');
    const closeSpy = vi.fn();
    const updatedSpy = vi.fn();
    fixture.componentInstance.closeRequest.subscribe(closeSpy);
    fixture.componentInstance.updated.subscribe(updatedSpy);
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');

    const [start, end] = dateInputs();
    setValue(start, '2026-10-03');
    setValue(end, '2026-10-06');
    await submitForm();

    const req = httpMock.expectOne('/api/bookings/b1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ startDate: '2026-10-03', endDate: '2026-10-06' });
    req.flush(bookingDto({ startDate: '2026-10-03', endDate: '2026-10-06', totalPrice: 1050 }));
    await settle();

    expect(toastSpy).toHaveBeenCalledWith('Booking updated.');
    expect(updatedSpy).toHaveBeenCalledTimes(1);
    expect(closeSpy).toHaveBeenCalledTimes(1);
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'detail', 'b1'] });
  });

  it('shows an overlap 409 in the banner and stays open', async () => {
    await settle();
    const closeSpy = vi.fn();
    fixture.componentInstance.closeRequest.subscribe(closeSpy);

    await submitForm();
    httpMock.expectOne('/api/bookings/b1').flush(
      {
        type: 'urn:bruno:booking:overlap',
        title: 'A domain rule was violated.',
        status: 409,
        detail: 'This vehicle is already booked 1 Nov – 5 Nov',
      },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();

    const alert = fixture.nativeElement.querySelector('[role="alert"]');
    expect(alert.textContent).toContain('This vehicle is already booked 1 Nov – 5 Nov');
    expect(closeSpy).not.toHaveBeenCalled();
  });

  it('maps a 400 EndDate error onto the End Date field', async () => {
    await settle();

    await submitForm();
    httpMock.expectOne('/api/bookings/b1').flush(
      {
        type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1',
        title: 'One or more validation errors occurred.',
        status: 400,
        detail: 'Validation failed.',
        errors: { EndDate: ['A booking cannot be longer than 365 days.'] },
      },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(fixture.nativeElement.textContent).toContain('A booking cannot be longer than 365 days.');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });

  it('a changed date clears a stale server error', async () => {
    await settle();

    await submitForm();
    httpMock.expectOne('/api/bookings/b1').flush(
      { type: 'urn:bruno:booking:overlap', title: 'x', status: 409, detail: 'Clash.' },
      { status: 409, statusText: 'Conflict' },
    );
    await settle();
    expect(fixture.nativeElement.querySelector('[role="alert"]')).not.toBeNull();

    setValue(dateInputs()[1], '2026-10-09');

    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
});
