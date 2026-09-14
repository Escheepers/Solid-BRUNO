import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerDto } from '../../core/models/customer-dto';
import { useCreateCustomerMutation, useCustomersQuery } from './customers.service';

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function customerDto(overrides: Partial<CustomerDto> = {}): CustomerDto {
  return {
    id: 'c1',
    firstName: 'Jane',
    lastName: 'Doe',
    email: 'jane.doe@example.com',
    phoneNumber: '0821234567',
    createdDate: '2026-01-15T10:30:00Z',
    isDeleted: false,
    isAnonymized: false,
    ...overrides,
  };
}

describe('useCustomersQuery', () => {
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

  it('GETs customers with page/pageSize/search params', async () => {
    TestBed.runInInjectionContext(() =>
      useCustomersQuery(() => ({ page: 2, pageSize: 20, search: 'jane' })),
    );
    await flushMicrotasks();

    const req = httpMock.expectOne(
      (r) =>
        r.url === '/api/customers' &&
        r.params.get('page') === '2' &&
        r.params.get('pageSize') === '20' &&
        r.params.get('search') === 'jane',
    );
    expect(req.request.method).toBe('GET');
    req.flush({ items: [customerDto()], totalCount: 1, page: 2, pageSize: 20 });
  });

  it('resolves with the fetched PagedResult<CustomerDto> on success', async () => {
    const query = TestBed.runInInjectionContext(() =>
      useCustomersQuery(() => ({ page: 1, pageSize: 20, search: '' })),
    );
    await flushMicrotasks();

    httpMock
      .expectOne('/api/customers?page=1&pageSize=20&search=')
      .flush({ items: [customerDto({ firstName: 'Zola' })], totalCount: 1, page: 1, pageSize: 20 });
    await flushMicrotasks();

    expect(query.data()?.items[0].firstName).toBe('Zola');
  });
});

describe('useCreateCustomerMutation', () => {
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

  it('POSTs the payload to customers', async () => {
    const mutation = TestBed.runInInjectionContext(() => useCreateCustomerMutation());

    mutation.mutate({
      firstName: 'Jane',
      lastName: 'Doe',
      email: 'jane.doe@example.com',
      phoneNumber: '0821234567',
    });
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/customers');
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({
      firstName: 'Jane',
      lastName: 'Doe',
      email: 'jane.doe@example.com',
      phoneNumber: '0821234567',
    });

    req.flush(customerDto(), { status: 201, statusText: 'Created' });
  });

  it('invalidates the customers list query key on success', async () => {
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries');
    const mutation = TestBed.runInInjectionContext(() => useCreateCustomerMutation());

    mutation.mutate({
      firstName: 'Jane',
      lastName: 'Doe',
      email: 'jane.doe@example.com',
      phoneNumber: '0821234567',
    });
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/customers');
    req.flush(customerDto(), { status: 201, statusText: 'Created' });
    await flushMicrotasks();

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: ['customers', 'list'] });
  });
});
