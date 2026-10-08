import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { BookingDto } from '../../core/models/booking-dto';
import {
  useBookingQuery,
  useBookingsQuery,
  useCancelBookingMutation,
  useCreateBookingMutation,
  useUpdateBookingMutation,
} from './bookings.service';

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

  it('GETs bookings with only page/pageSize params when no vehicleId/search is supplied', async () => {
    TestBed.runInInjectionContext(() => useBookingsQuery(() => ({ page: 2, pageSize: 20 })));
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bookings' && r.params.get('page') === '2' && r.params.get('pageSize') === '20',
    );
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush({ items: [bookingDto()], totalCount: 1, page: 2, pageSize: 20 });
  });

  it('includes search in the request when supplied (spec-bookings-search)', async () => {
    TestBed.runInInjectionContext(() =>
      useBookingsQuery(() => ({ page: 1, pageSize: 20, search: 'Ferrari' })),
    );
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bookings' && r.params.get('search') === 'Ferrari',
    );
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize', 'search']);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('omits search from the request when it is empty/whitespace-only', async () => {
    TestBed.runInInjectionContext(() =>
      useBookingsQuery(() => ({ page: 1, pageSize: 20, search: '   ' })),
    );
    await flushMicrotasks();

    const req = httpMock.expectOne((r) => r.url === '/api/bookings');
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize']);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
  });

  it('includes both vehicleId and search in the request when both are supplied (composable filters)', async () => {
    TestBed.runInInjectionContext(() =>
      useBookingsQuery(() => ({ page: 1, pageSize: 20, vehicleId: 'v1', search: 'Nkosi' })),
    );
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/bookings' && r.params.get('vehicleId') === 'v1' && r.params.get('search') === 'Nkosi',
    );
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize', 'search', 'vehicleId']);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
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

  it('includes vehicleId in the request when supplied (spec-4-5\'s Scope decision 1)', async () => {
    TestBed.runInInjectionContext(() =>
      useBookingsQuery(() => ({ page: 1, pageSize: 100, vehicleId: 'v1' })),
    );
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) => r.url === '/api/bookings' && r.params.get('vehicleId') === 'v1',
    );
    expect(req.request.params.keys().sort()).toEqual(['page', 'pageSize', 'vehicleId']);
    req.flush({ items: [], totalCount: 0, page: 1, pageSize: 100 });
  });
});

describe('useBookingQuery', () => {
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

  it('GETs bookings/{id} for a defined id', async () => {
    TestBed.runInInjectionContext(() => useBookingQuery(() => 'b1'));
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings/b1');
    expect(req.request.method).toBe('GET');
    req.flush(bookingDto());
  });

  it('issues no request while id is undefined', async () => {
    TestBed.runInInjectionContext(() => useBookingQuery(() => undefined));
    await flushMicrotasks();

    httpMock.expectNone(() => true);
  });

  it('resolves with the fetched BookingDto on success', async () => {
    const query = TestBed.runInInjectionContext(() => useBookingQuery(() => 'b1'));
    await flushMicrotasks();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto({ vehicleMake: 'Ford' }));
    await flushMicrotasks();

    expect(query.data()?.vehicleMake).toBe('Ford');
  });

  it('normalizes a 404 into a NotFoundError on the query', async () => {
    const query = TestBed.runInInjectionContext(() => useBookingQuery(() => 'missing-id'));
    await flushMicrotasks();

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
    await flushMicrotasks();

    expect(query.isError()).toBe(true);
    expect(query.error()?.kind).toBe('not-found');
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

describe('useCancelBookingMutation', () => {
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

  it('POSTs to bookings/{id}/cancel with no body', async () => {
    const mutation = TestBed.runInInjectionContext(() => useCancelBookingMutation());

    mutation.mutate('b1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings/b1/cancel');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeFalsy();

    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('invalidates the bookings list query key on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useCancelBookingMutation());

    mutation.mutate('b1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings/b1/cancel');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
  });
});

describe('useUpdateBookingMutation', () => {
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

  it('PUTs the new dates to bookings/{id}', async () => {
    const mutation = TestBed.runInInjectionContext(() => useUpdateBookingMutation());

    mutation.mutate({ id: 'b1', payload: { startDate: '2030-01-10', endDate: '2030-01-12' } });
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/bookings/b1');
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ startDate: '2030-01-10', endDate: '2030-01-12' });

    req.flush(bookingDto({ startDate: '2030-01-10', endDate: '2030-01-12' }));
  });

  it('invalidates the list and that booking detail query on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useUpdateBookingMutation());

    mutation.mutate({ id: 'b1', payload: { startDate: '2030-01-10', endDate: '2030-01-12' } });
    await flushMicrotasks();

    httpMock.expectOne('/api/bookings/b1').flush(bookingDto());
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'list'] });
    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['bookings', 'detail', 'b1'] });
  });
});
