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
  /**
   * FluentValidation's per-field messages, present whenever the response body
   * carried an `errors` dictionary (every 400 from `ValidationBehavior`). Absent for
   * a single-field 409 (a domain-rule violation has no per-field shape) — use
   * `fieldFromType` instead for that case.
   */
  errors?: Record<string, string[]>;
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
 * A 404 response whose body is a valid ProblemDetails shape — the record genuinely
 * doesn't exist (as opposed to a `ServerError`, which covers a 404 with an untrusted
 * body, per this module's fail-safe philosophy). Deferred by Stories 2.2/2.4 until a
 * story actually needed to render a real "not found" state; Story 2.5 is that story.
 */
export interface NotFoundError {
  kind: 'not-found';
  detail: string;
}

/**
 * Every error `ApiClient` requests can produce, normalized into exactly one of these
 * three structurally-distinguishable shapes via the `kind` discriminant.
 */
export type NormalizedApiError = BusinessRuleError | ServerError | NotFoundError;

/**
 * Recovers the single field name a business-rule URI refers to, for the 409 case
 * where no `errors` dictionary is present. Every business-rule `type` follows
 * `urn:bruno:{entity}:{rule-kebab-case}` (Story 1.5) — this takes the last
 * `:`-delimited segment and converts it from kebab-case to camelCase, e.g.
 * `"urn:bruno:vehicle:registration-number"` -> `"registrationNumber"`. Deliberately
 * naive about cross-entity collisions (see spec-2-1's Design Notes) — this story's
 * forms only ever touch one entity's fields at a time.
 */
export function fieldFromType(type: string): string | undefined {
  const lastSegment = type.split(':').pop();
  if (!lastSegment) {
    return undefined;
  }

  const words = lastSegment.split('-').filter((word) => word.length > 0);
  if (words.length === 0) {
    return undefined;
  }

  return words
    .map((word, index) => (index === 0 ? word : word.charAt(0).toUpperCase() + word.slice(1)))
    .join('');
}
