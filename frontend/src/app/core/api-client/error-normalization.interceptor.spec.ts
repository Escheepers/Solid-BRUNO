import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { errorNormalizationInterceptor } from './error-normalization.interceptor';
import { NotFoundError } from './normalized-api-error';

/**
 * Direct interceptor-level tests for the 404 branch. `ApiClient`'s own spec covers
 * this interceptor indirectly for the other status codes; this file adds the case
 * spec-not-found-and-routing-fixes introduces -- a 404 with no parseable
 * ProblemDetails body (e.g. ASP.NET's bodyless `{id:guid}` route-constraint
 * rejection for a malformed id) must still normalize to `NotFoundError`, not fall
 * through to `ServerError`.
 */
describe('errorNormalizationInterceptor - 404 handling', () => {
  let http: HttpClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([errorNormalizationInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    http = TestBed.inject(HttpClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  it('normalizes a bodyless 404 (e.g. a malformed-id route-constraint rejection) into a NotFoundError', () => {
    let captured: unknown;
    http.get('/api/vehicles/not-a-guid').subscribe({ error: (err: unknown) => (captured = err) });

    const req = httpMock.expectOne('/api/vehicles/not-a-guid');
    req.flush(null, { status: 404, statusText: 'Not Found' });

    const error = captured as NotFoundError;
    expect(error.kind).toBe('not-found');
    expect(error.detail).toBe('This record no longer exists.');
  });

  it('normalizes a 404 with an unparseable (non-ProblemDetails) body into a NotFoundError, not a ServerError', () => {
    let captured: unknown;
    http.get('/api/vehicles/does-not-exist').subscribe({ error: (err: unknown) => (captured = err) });

    const req = httpMock.expectOne('/api/vehicles/does-not-exist');
    req.flush('<html>Not Found</html>', { status: 404, statusText: 'Not Found' });

    const error = captured as NotFoundError;
    expect(error.kind).toBe('not-found');
    expect(error.detail).toBe('This record no longer exists.');
  });

  it('still uses the ProblemDetails detail when the 404 body is parseable', () => {
    let captured: unknown;
    http.get('/api/vehicles/does-not-exist').subscribe({ error: (err: unknown) => (captured = err) });

    const req = httpMock.expectOne('/api/vehicles/does-not-exist');
    req.flush(
      {
        type: 'https://bruno-vehicle-hire/problems/not-found',
        title: 'Not Found',
        status: 404,
        detail: "Vehicle 'does-not-exist' was not found.",
      },
      { status: 404, statusText: 'Not Found' },
    );

    const error = captured as NotFoundError;
    expect(error.kind).toBe('not-found');
    expect(error.detail).toBe("Vehicle 'does-not-exist' was not found.");
  });
});
