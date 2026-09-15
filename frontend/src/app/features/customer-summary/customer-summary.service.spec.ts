import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { apiKeyInterceptor } from '../../core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from '../../core/api-client/error-normalization.interceptor';
import { CustomerSummaryDto } from '../../core/models/customer-summary-dto';
import { useCustomerSummaryQuery } from './customer-summary.service';

/** Matches the macrotask-flush pattern used elsewhere for TanStack Query's Angular reactivity. */
function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function customerSummaryDto(overrides: Partial<CustomerSummaryDto> = {}): CustomerSummaryDto {
  return {
    id: 'c1',
    firstName: 'Thabo',
    lastName: 'Nkosi',
    email: 'thabo@example.com',
    phoneNumber: '0821234567',
    isAnonymized: false,
    bookings: [],
    ...overrides,
  };
}

describe('useCustomerSummaryQuery', () => {
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

  it('GETs customers/{id}/summary for a defined id', async () => {
    TestBed.runInInjectionContext(() => useCustomerSummaryQuery(() => 'c1'));
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/customers/c1/summary');
    expect(req.request.method).toBe('GET');
    req.flush(customerSummaryDto());
  });

  it('issues no request while id is undefined', async () => {
    TestBed.runInInjectionContext(() => useCustomerSummaryQuery(() => undefined));
    await flushMicrotasks();

    httpMock.expectNone(() => true);
  });

  it('resolves with the fetched CustomerSummaryDto on success', async () => {
    const query = TestBed.runInInjectionContext(() => useCustomerSummaryQuery(() => 'c1'));
    await flushMicrotasks();

    httpMock.expectOne('/api/customers/c1/summary').flush(customerSummaryDto({ firstName: 'Zola' }));
    await flushMicrotasks();

    expect(query.data()?.firstName).toBe('Zola');
  });

  it('normalizes a 404 into a NotFoundError on the query', async () => {
    const query = TestBed.runInInjectionContext(() => useCustomerSummaryQuery(() => 'missing-id'));
    await flushMicrotasks();

    const req = httpMock.expectOne('/api/customers/missing-id/summary');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/not-found',
        title: 'Not Found',
        status: 404,
        detail: "Customer 'missing-id' was not found.",
      },
      { status: 404, statusText: 'Not Found' },
    );
    await flushMicrotasks();

    expect(query.isError()).toBe(true);
    expect(query.error()?.kind).toBe('not-found');
  });
});
