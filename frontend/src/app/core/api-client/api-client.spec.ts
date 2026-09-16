import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { environment } from '../../../environments/environment';
import { ProblemDetails } from '../models/problem-details';
import { ApiClient } from './api-client';
import { apiKeyInterceptor } from './api-key.interceptor';
import { errorNormalizationInterceptor } from './error-normalization.interceptor';
import { BusinessRuleError, NotFoundError, ServerError } from './normalized-api-error';

describe('ApiClient', () => {
  let apiClient: ApiClient;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    apiClient = TestBed.inject(ApiClient);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  describe('X-Api-Key header attachment', () => {
    it('attaches X-Api-Key to a GET request', () => {
      apiClient.get<unknown>('vehicles').subscribe();

      const req = httpMock.expectOne('/api/vehicles');
      expect(req.request.headers.get('X-Api-Key')).toBe(environment.apiKey);
      req.flush({});
    });

    it('attaches X-Api-Key to a POST request', () => {
      apiClient.post<unknown, { name: string }>('vehicles', { name: 'Test' }).subscribe();

      const req = httpMock.expectOne('/api/vehicles');
      expect(req.request.headers.get('X-Api-Key')).toBe(environment.apiKey);
      req.flush({});
    });

    it('attaches X-Api-Key to a PUT request', () => {
      apiClient.put<unknown, { name: string }>('vehicles/1', { name: 'Test' }).subscribe();

      const req = httpMock.expectOne('/api/vehicles/1');
      expect(req.request.headers.get('X-Api-Key')).toBe(environment.apiKey);
      req.flush({});
    });

    it('attaches X-Api-Key to a DELETE request', () => {
      apiClient.delete<unknown>('vehicles/1').subscribe();

      const req = httpMock.expectOne('/api/vehicles/1');
      expect(req.request.headers.get('X-Api-Key')).toBe(environment.apiKey);
      req.flush({});
    });
  });

  describe('error normalization', () => {
    const problemDetails: ProblemDetails = {
      type: 'https://bruno-vehicle-hire/problems/vehicle/already-booked',
      title: 'A domain rule was violated.',
      status: 409,
      detail: 'Vehicle is already booked for the requested period.',
    };

    it('normalizes a 400 ProblemDetails response into a BusinessRuleError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush({ ...problemDetails, status: 400 }, { status: 400, statusText: 'Bad Request' });

      const error = captured as BusinessRuleError;
      expect(error.kind).toBe('business-rule');
      expect(error.status).toBe(400);
      expect(error.title).toBe(problemDetails.title);
      expect(error.detail).toBe(problemDetails.detail);
      expect(error.type).toBe(problemDetails.type);
    });

    it('normalizes a 409 ProblemDetails response into a BusinessRuleError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush(problemDetails, { status: 409, statusText: 'Conflict' });

      const error = captured as BusinessRuleError;
      expect(error.kind).toBe('business-rule');
      expect(error.status).toBe(409);
      expect(error.title).toBe(problemDetails.title);
      expect(error.detail).toBe(problemDetails.detail);
      expect(error.type).toBe(problemDetails.type);
    });

    it('normalizes a 500 response into a ServerError, never a BusinessRuleError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush(
        { type: 'about:blank', title: 'An unexpected error occurred.', status: 500, detail: 'boom' },
        { status: 500, statusText: 'Internal Server Error' },
      );

      const error = captured as ServerError;
      expect(error.kind).toBe('server-error');
      expect((error as unknown as BusinessRuleError).kind).not.toBe('business-rule');
    });

    it('normalizes a network failure into a ServerError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });

      const error = captured as ServerError;
      expect(error.kind).toBe('server-error');
    });

    it('normalizes a malformed (non-JSON-object) 400 body into a ServerError, not a BusinessRuleError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush('<html>Bad Request</html>', { status: 400, statusText: 'Bad Request' });

      const error = captured as ServerError;
      expect(error.kind).toBe('server-error');
    });

    it('normalizes a 400 body missing required ProblemDetails fields into a ServerError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush({ message: 'oops' }, { status: 400, statusText: 'Bad Request' });

      const error = captured as ServerError;
      expect(error.kind).toBe('server-error');
    });

    it('populates errors from a 400 ValidationProblemDetails body carrying a field errors dictionary', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush(
        {
          ...problemDetails,
          status: 400,
          errors: { Make: ['Make must not be empty.'], DailyRate: ['Daily rate must be positive.'] },
        },
        { status: 400, statusText: 'Bad Request' },
      );

      const error = captured as BusinessRuleError;
      expect(error.kind).toBe('business-rule');
      expect(error.errors).toEqual({
        Make: ['Make must not be empty.'],
        DailyRate: ['Daily rate must be positive.'],
      });
    });

    it('leaves errors undefined on a 409 body with no errors dictionary', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush(problemDetails, { status: 409, statusText: 'Conflict' });

      const error = captured as BusinessRuleError;
      expect(error.kind).toBe('business-rule');
      expect(error.errors).toBeUndefined();
    });

    it('leaves errors undefined on a 500 ServerError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles').subscribe({ error: (err: unknown) => (captured = err) });

      const req = httpMock.expectOne('/api/vehicles');
      req.flush(
        { type: 'about:blank', title: 'An unexpected error occurred.', status: 500, detail: 'boom' },
        { status: 500, statusText: 'Internal Server Error' },
      );

      const error = captured as ServerError;
      expect(error.kind).toBe('server-error');
      expect((error as unknown as BusinessRuleError).errors).toBeUndefined();
    });

    it('normalizes a 404 ProblemDetails response into a NotFoundError', () => {
      let captured: unknown;
      apiClient.get<unknown>('vehicles/does-not-exist').subscribe({
        error: (err: unknown) => (captured = err),
      });

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

    it('normalizes a malformed (non-ProblemDetails) 404 body into a NotFoundError with a generic message', () => {
      // Per spec-not-found-and-routing-fixes: a 404 always means "doesn't exist,"
      // regardless of body shape -- including ASP.NET's bodyless `{id:guid}`
      // route-constraint rejection for a malformed id, which is indistinguishable
      // from this malformed-body case from the frontend's point of view.
      let captured: unknown;
      apiClient.get<unknown>('vehicles/does-not-exist').subscribe({
        error: (err: unknown) => (captured = err),
      });

      const req = httpMock.expectOne('/api/vehicles/does-not-exist');
      req.flush('<html>Not Found</html>', { status: 404, statusText: 'Not Found' });

      const error = captured as NotFoundError;
      expect(error.kind).toBe('not-found');
      expect(error.detail).toBe('This record no longer exists.');
    });
  });
});
