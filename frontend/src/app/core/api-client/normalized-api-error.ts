/**
 * A 400 or 409 ProblemDetails response — both are treated identically downstream
 * (per ARCHITECTURE-SPINE.md AD-8: "the user doesn't need to know which").
 */
export interface BusinessRuleError {
  kind: 'business-rule';
  status: number;
  title: string;
  detail: string;
  type: string;
}

/**
 * A 5xx response, a network/connection failure, or any error response whose body
 * can't be safely trusted as ProblemDetails (fail-safe: never guess a business-rule
 * shape from an unparseable body).
 */
export interface ServerError {
  kind: 'server-error';
  status?: number;
  message: string;
}

/**
 * Every error `ApiClient` requests can produce, normalized into exactly one of these
 * two structurally-distinguishable shapes via the `kind` discriminant.
 */
export type NormalizedApiError = BusinessRuleError | ServerError;
