import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { useDeactivateVehicleMutation } from './vehicles.service';

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

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
