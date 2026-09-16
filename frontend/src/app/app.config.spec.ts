import { queryRetryPredicate } from './app.config';
import { BusinessRuleError, NotFoundError, ServerError } from './core/api-client/normalized-api-error';

describe('queryRetryPredicate', () => {
  const notFound: NotFoundError = { kind: 'not-found', detail: 'This record no longer exists.' };

  const businessRule: BusinessRuleError = {
    kind: 'business-rule',
    status: 409,
    title: 'A domain rule was violated.',
    detail: 'Vehicle is already booked for the requested period.',
    type: 'https://bruno-vehicle-hire/problems/vehicle/already-booked',
  };

  const serverError: ServerError = {
    kind: 'server-error',
    status: 500,
    message: 'An unexpected error occurred. Please try again later.',
  };

  it('never retries a NotFoundError, regardless of failure count', () => {
    expect(queryRetryPredicate(0, notFound)).toBe(false);
    expect(queryRetryPredicate(1, notFound)).toBe(false);
  });

  it('never retries a BusinessRuleError, regardless of failure count', () => {
    expect(queryRetryPredicate(0, businessRule)).toBe(false);
    expect(queryRetryPredicate(1, businessRule)).toBe(false);
  });

  it('retries a ServerError up to the bounded count, then stops', () => {
    expect(queryRetryPredicate(0, serverError)).toBe(true);
    expect(queryRetryPredicate(1, serverError)).toBe(true);
    expect(queryRetryPredicate(2, serverError)).toBe(false);
  });
});
