import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { VehicleDto } from '../../core/models/vehicle-dto';
import {
  useDeactivateVehicleMutation,
  useRestoreVehicleMutation,
  useVehicleQuery,
} from './vehicles.service';

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

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

describe('useVehicleQuery', () => {
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

  it('GETs vehicles/{id} for a defined id', async () => {
    TestBed.runInInjectionContext(() => useVehicleQuery(() => 'v1'));
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/vehicles/v1');
    expect(req.request.method).toBe('GET');
    req.flush(vehicleDto());
  });

  it('issues no request while id is undefined', async () => {
    TestBed.runInInjectionContext(() => useVehicleQuery(() => undefined));
    await flushMicrotasks();

    httpMock.expectNone(() => true);
  });

  it('resolves with the fetched VehicleDto on success', async () => {
    const query = TestBed.runInInjectionContext(() => useVehicleQuery(() => 'v1'));
    await flushMicrotasks();

    httpMock.expectOne('/api/vehicles/v1').flush(vehicleDto({ registrationNumber: 'CA999999' }));
    await flushMicrotasks();

    expect(query.data()?.registrationNumber).toBe('CA999999');
  });

  it('normalizes a 404 into a NotFoundError on the query', async () => {
    const query = TestBed.runInInjectionContext(() => useVehicleQuery(() => 'missing-id'));
    await flushMicrotasks();

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
    await flushMicrotasks();

    expect(query.isError()).toBe(true);
    expect(query.error()?.kind).toBe('not-found');
  });
});

describe('useDeactivateVehicleMutation', () => {
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

  it('POSTs to vehicles/{id}/deactivate with no body', async () => {
    const mutation = TestBed.runInInjectionContext(() => useDeactivateVehicleMutation());

    mutation.mutate('v1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/vehicles/v1/deactivate');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeFalsy();

    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('invalidates the vehicles list query key on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useDeactivateVehicleMutation());

    mutation.mutate('v1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/vehicles/v1/deactivate');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['vehicles', 'list'] });
  });
});

describe('useRestoreVehicleMutation', () => {
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

  it('POSTs to vehicles/{id}/restore with no body', async () => {
    const mutation = TestBed.runInInjectionContext(() => useRestoreVehicleMutation());

    mutation.mutate('v1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/vehicles/v1/restore');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toBeFalsy();

    req.flush(null, { status: 204, statusText: 'No Content' });
  });

  it('invalidates the vehicles list query key on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useRestoreVehicleMutation());

    mutation.mutate('v1');
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/vehicles/v1/restore');
    req.flush(null, { status: 204, statusText: 'No Content' });
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['vehicles', 'list'] });
  });
});
