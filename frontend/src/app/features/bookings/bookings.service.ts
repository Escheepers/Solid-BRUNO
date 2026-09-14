import { inject } from '@angular/core';
import { QueryClient, injectMutation, injectQuery } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { BookingDto } from '../../core/models/booking-dto';
import { PagedResult } from '../../core/models/paged-result';

export interface BookingsQueryParams {
  page: number;
  pageSize: number;
  /**
   * Optional Vehicle filter (spec-4-5's Scope decision 1) -- additive and
   * backward-compatible with every pre-existing caller (`BookingsPage` never
   * supplies it, so its own unfiltered list request is unaffected). Reused by both
   * the unfiltered Bookings list and Vehicle Detail's booking-history section,
   * rather than a second, near-duplicate query (DRY).
   */
  vehicleId?: string;
}

/**
 * The exact body `POST /api/bookings` expects, matching `CreateBookingCommand`
 * (`src/BrunoVehicleHire.Application/Bookings/Commands/CreateBookingCommand.cs`)
 * field-for-field. Deliberately carries no `totalPrice` -- it's always computed
 * server-side (spec-4-1's Boundaries), never trusted from the request.
 * `startDate`/`endDate` are `"yyyy-MM-dd"` strings -- exactly what an
 * `<input type="date">` control's value already is, so `BookingFormModal` sends the
 * form's raw string values through unchanged, no formatting step needed.
 */
export interface CreateBookingPayload {
  vehicleId: string;
  customerId: string;
  startDate: string;
  endDate: string;
}

/** Matches `useBookingsQuery`'s query-key convention (AD-3) exactly, minus the params
 * — `invalidateQueries` matches every params variant sharing this key prefix. */
export const BOOKINGS_LIST_QUERY_KEY = ['bookings', 'list'] as const;

/**
 * Wraps `injectQuery` over `ApiClient.get<PagedResult<BookingDto>>('bookings', ...)`,
 * mirroring `useVehiclesQuery`/`useCustomersQuery`'s exact shape -- minus `search`/
 * `showInactive` (spec-4-1's Scope decision 3: no free-text search or inactive
 * filter for this story's list; `GetBookingsQuery` takes only `page`/`pageSize`/
 * `vehicleId`). Query-key convention (AD-3):
 * `['bookings', 'list', { page, pageSize, vehicleId }]`. `params` is a function (not
 * a plain object) so `injectQuery`'s reactive context re-runs the query whenever any
 * Signal it reads (page/pageSize/vehicleId) changes.
 *
 * `vehicleId` (spec-4-5's Scope decision 1) is omitted from the actual HTTP request
 * entirely when absent -- rather than sent as the literal string `"undefined"` --
 * so `BookingsPage`'s own unfiltered call (which never supplies it) keeps issuing
 * the exact same `?page=&pageSize=` request it always has. Vehicle Detail's
 * booking-history section (spec-4-5) is the first caller to supply it.
 */
export function useBookingsQuery(params: () => BookingsQueryParams) {
  const apiClient = inject(ApiClient);

  return injectQuery(() => {
    const { page, pageSize, vehicleId } = params();

    return {
      queryKey: ['bookings', 'list', { page, pageSize, vehicleId }] as const,
      queryFn: () =>
        firstValueFrom(
          apiClient.get<PagedResult<BookingDto>>(
            'bookings',
            vehicleId ? { page, pageSize, vehicleId } : { page, pageSize },
          ),
        ),
    };
  });
}

/**
 * Wraps `injectQuery` over `ApiClient.get<BookingDto>('bookings/{id}')` for a single
 * booking's detail (spec-4-5), mirroring `useVehicleQuery`'s exact shape. Query-key
 * convention (AD-3): `['bookings', 'detail', id]`. `id` is a function (mirroring
 * `useBookingsQuery`'s own `params`) so the query reactively re-runs if the route's
 * `id` param changes without remounting `BookingDetailPage`. `enabled` is guarded on
 * a defined id so no request fires for an as-yet-unresolved route param --
 * `firstValueFrom` would otherwise be called with `bookings/undefined` in the URL.
 */
export function useBookingQuery(id: () => string | undefined) {
  const apiClient = inject(ApiClient);

  return injectQuery<BookingDto, NormalizedApiError>(() => {
    const bookingId = id();

    return {
      queryKey: ['bookings', 'detail', bookingId] as const,
      queryFn: () => firstValueFrom(apiClient.get<BookingDto>(`bookings/${bookingId}`)),
      enabled: !!bookingId,
    };
  });
}

/**
 * Wraps `injectMutation` over `ApiClient.post<BookingDto, CreateBookingPayload>('bookings', ...)`,
 * mirroring `useCreateCustomerMutation`'s exact pattern. On success, invalidates
 * `['bookings', 'list']` (AD-3) so the list re-fetches and the newly-created
 * booking appears.
 */
export function useCreateBookingMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<BookingDto, NormalizedApiError, CreateBookingPayload>(() => ({
    mutationFn: (payload) =>
      firstValueFrom(apiClient.post<BookingDto, CreateBookingPayload>('bookings', payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: BOOKINGS_LIST_QUERY_KEY }),
  }));
}

/**
 * Wraps `injectMutation` over `ApiClient.post('bookings/{id}/cancel', ...)` -- added for Story 4.3's
 * Cancel flow, mirroring `useDeactivateCustomerMutation`'s exact shape (same body-less POST /
 * invalidate-on-success pattern). `ApiClient.post<T, B>` requires a body argument, so `undefined` is
 * passed explicitly for this body-less action. On success, invalidates `['bookings', 'list']` (AD-3)
 * so the list re-fetches and the cancelled booking's row reflects its new `Cancelled` status.
 */
export function useCancelBookingMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<void, NormalizedApiError, string>(() => ({
    mutationFn: (bookingId: string) =>
      firstValueFrom(apiClient.post<void, undefined>(`bookings/${bookingId}/cancel`, undefined)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: BOOKINGS_LIST_QUERY_KEY }),
  }));
}
