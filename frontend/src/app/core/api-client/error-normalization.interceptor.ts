import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

import { ProblemDetails } from '../models/problem-details';
import { NormalizedApiError } from './normalized-api-error';

const GENERIC_SERVER_ERROR_MESSAGE = 'An unexpected error occurred. Please try again later.';

/**
 * Type-guards an unknown error body against the exact ProblemDetails shape.
 * Deliberately conservative: HTML, plain text, `null`, or a body missing any
 * required field all fail this check, so the caller falls through to a
 * `ServerError` instead of a wrongly-shaped `BusinessRuleError`.
 */
function isProblemDetails(body: unknown): body is ProblemDetails {
  if (typeof body !== 'object' || body === null) {
    return false;
  }

  const candidate = body as Record<string, unknown>;

  return (
    typeof candidate['type'] === 'string' &&
    typeof candidate['title'] === 'string' &&
    typeof candidate['status'] === 'number' &&
    typeof candidate['detail'] === 'string'
  );
}

function normalize(error: unknown): NormalizedApiError {
  if (!(error instanceof HttpErrorResponse)) {
    return { kind: 'server-error', message: GENERIC_SERVER_ERROR_MESSAGE };
  }

  const isBusinessRuleStatus = error.status === 400 || error.status === 409;

  if (isBusinessRuleStatus && isProblemDetails(error.error)) {
    const problem = error.error;
    return {
      kind: 'business-rule',
      status: problem.status,
      title: problem.title,
      detail: problem.detail,
      type: problem.type,
    };
  }

  return {
    kind: 'server-error',
    status: error.status || undefined,
    message: GENERIC_SERVER_ERROR_MESSAGE,
  };
}

/**
 * Normalizes every error response into exactly one of two shapes: a
 * `BusinessRuleError` (400 or 409 with a body matching the ProblemDetails shape) or
 * a `ServerError` (5xx, a network/connection failure, or any body that can't be
 * safely trusted as ProblemDetails). Never assumes `error.error` is safely readable.
 */
export const errorNormalizationInterceptor: HttpInterceptorFn = (req, next) =>
  next(req).pipe(catchError((error: unknown) => throwError(() => normalize(error))));
