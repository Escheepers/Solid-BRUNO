/**
 * Raw RFC 9457 ProblemDetails DTO shape, matching the backend's
 * `GlobalExceptionHandler` output exactly (see
 * src/BrunoVehicleHire.Api/ExceptionHandling/GlobalExceptionHandler.cs).
 */
export interface ProblemDetails {
  type: string;
  title: string;
  status: number;
  detail: string;
}
