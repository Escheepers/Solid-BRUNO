import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import { useBookingsQuery, useCreateBookingMutation } from './bookings.service';

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
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
    endDate: '2026-10-05',
    totalPrice: 1400,
    status: 'Active',
    createdDate: '2026-09-20T10:30:00Z',
    ...overrides,
  };
}

describe('useBookingsQuery', () => {
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('GETs bookings with only page/pageSize params (no search/showInactive, per Scope decision 3)', async () => {
    TestBed.runInInjectionContext(() => useBookingsQuery(() => ({ page: 2, pageSize: 20 })));
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bookings' && r.params.get('page') === '2' && r.params.get('pageSize') === '20',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush({ items: [bookingDto()], totalCount: 1, page: 2, pageSize: 20 });
  });

  it('resolves with the fetched PagedResult<BookingDto> on success', async () => {
    const query = TestBed.runInInjectionContext(() => useBookingsQuery(() => ({ page: 1, pageSize: 20 })));
    await flushMicrotasks();

    httpMock
      .expectOne('/api/bookings?page=1&pageSize=20')
      .flush({ items: [bookingDto({ vehicleMake: 'Ford' })], totalCount: 1, page: 1, pageSize: 20 });
    await flushMicrotasks();

    expect(query.data()?.items[0].vehicleMake).toBe('Ford');
  });
});

describe('useCreateBookingMutation', () => {
  let httpMock: HttpTestingController;
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });

    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
        provideTanStackQuery(queryClient),
      ],
    });

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    TestBed.resetTestingModule();
  });

  it('POSTs the payload to bookings', async () => {
    const mutation = TestBed.runInInjectionContext(() => useCreateBookingMutation());

    mutation.mutate({
      vehicleId: 'v1',
      customerId: 'c1',
      startDate: '2026-10-01',
      endDate: '2026-10-05',
    });
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      vehicleId: 'v1',
      customerId: 'c1',
      startDate: '2026-10-01',
      endDate: '2026-10-05',
    });

    req.flush(bookingDto(), { status: 201, statusText: 'Created' });
  });

  it('invalidates the bookings list query key on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useCreateBookingMutation());

    mutation.mutate({
      vehicleId: 'v1',
      customerId: 'c1',
      startDate: '2026-10-01',
      endDate: '2026-10-05',
    });
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings');
    req.flush(bookingDto(), { status: 201, statusText: 'Created' });
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
  });
});
