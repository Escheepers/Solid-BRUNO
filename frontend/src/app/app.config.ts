import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter } from '@angular/router';
import { QueryClient, provideTanStackQuery } from '@tanstack/angular-query-experimental';

import { routes } from './app.routes';
import { apiKeyInterceptor } from './core/api-client/api-key.interceptor';
import { errorNormalizationInterceptor } from './core/api-client/error-normalization.interceptor';
import { NormalizedApiError } from './core/api-client/normalized-api-error';

/**
 * The number of times a `ServerError` (a genuine 5xx/network failure) is retried
 * before a query gives up. Deliberately small and unchanged from what a real backend
 * blip deserves (Design Notes, spec-not-found-and-routing-fixes) -- this is a
 * bug-fix for the deterministic-error case below, not a broader retry-policy
 * redesign.
 */
const MAX_SERVER_ERROR_RETRIES = 2;

/**
 * TanStack Query's own library default retries every failed query up to 3 times
 * with backoff, including a deterministic `NotFoundError`/`BusinessRuleError` that
 * can never succeed on retry (a 404/400/409 stays a 404/400/409). Reusing
 * `NormalizedApiError.kind` -- rather than a second, parallel HTTP-status-based
 * classification -- keeps this decision consistent with how every other part of the
 * frontend already reasons about errors. A genuine `ServerError` (or anything else
 * thrown) still gets a small bounded number of retries, unchanged reliability for a
 * real transient failure.
 *
 * The parameter is typed `unknown` rather than `NormalizedApiError` because
 * `QueryClient`'s `defaultOptions.queries.retry` is declared against TanStack's
 * library-wide `DefaultError` (which resolves to plain `Error` unless the app
 * globally augments TanStack's `Register` interface, which this codebase doesn't) --
 * `unknown` is the only parameter type contravariantly compatible with that slot.
 * Every actual query in this app is still typed `injectQuery<T, NormalizedApiError>`
 * (see e.g. `vehicles.service.ts`), so `error` is always genuinely a
 * `NormalizedApiError` at the call site; the cast below just reconciles that with the
 * wider library type. Exported for direct unit testing; not used outside this file's
 * `QueryClient`.
 */
export function queryRetryPredicate(failureCount: number, error: unknown): boolean {
  const kind = (error as Partial<NormalizedApiError> | undefined)?.kind;
  if (kind === 'not-found' || kind === 'business-rule') {
    return false;
  }

  return failureCount < MAX_SERVER_ERROR_RETRIES;
}

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(routes),
    provideHttpClient(withInterceptors([apiKeyInterceptor, errorNormalizationInterceptor])),
    provideTanStackQuery(
      new QueryClient({ defaultOptions: { queries: { retry: queryRetryPredicate } } }),
    ),
  ],
};
