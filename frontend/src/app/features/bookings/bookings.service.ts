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
  /**
   * Optional free-text search (spec-bookings-search), matching a booking's Vehicle
   * Make/Model/RegistrationNumber or Customer's first/last name (a single search
   * box, OR'd across all five fields server-side). Additive and independent of
   * `vehicleId` -- Vehicle Detail's own booking-history call site never supplies
   * this, so its behavior stays completely unaffected.
   */
  search?: string;
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

/**
 * The body of `PUT /api/bookings/{id}` (`UpdateBookingCommand` minus the route's `bookingId`): just the
 * new dates, as `"yyyy-MM-dd"` strings. Like create, it carries no `totalPrice` -- the server rescales it.
 */
export interface UpdateBookingPayload {
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
 * filter for this story's list; `GetBookingsQuery` also takes an optional `search`,
 * see below). Query-key convention (AD-3):
 * `['bookings', 'list', { page, pageSize, vehicleId, search }]`. `params` is a
 * function (not a plain object) so `injectQuery`'s reactive context re-runs the
 * query whenever any Signal it reads (page/pageSize/vehicleId/search) changes.
 *
 * `vehicleId` (spec-4-5's Scope decision 1) and `search` (spec-bookings-search) are
 * each omitted from the actual HTTP request entirely when absent/empty -- rather
 * than sent as the literal string `"undefined"` or an empty `search=` -- so
 * `BookingsPage`'s own unfiltered call (which never supplies `vehicleId`, and
 * supplies `search` only once the user has typed something) keeps issuing the exact
 * same `?page=&pageSize=` request it always has until a filter is actually set.
 * Vehicle Detail's booking-history section (spec-4-5) is the first caller to supply
 * `vehicleId`, and never supplies `search`.
 */
export function useBookingsQuery(params: () => BookingsQueryParams) {
  const apiClient = inject(ApiClient);

  return injectQuery(() => {
    const { page, pageSize, vehicleId, search } = params();

    return {
      queryKey: ['bookings', 'list', { page, pageSize, vehicleId, search }] as const,
      queryFn: () =>
        firstValueFrom(
          apiClient.get<PagedResult<BookingDto>>('bookings', {
            page,
            pageSize,
            ...(vehicleId ? { vehicleId } : {}),
            ...(search?.trim() ? { search } : {}),
          }),
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

/**
 * Wraps `injectMutation` over `ApiClient.put<BookingDto, UpdateBookingPayload>('bookings/{id}', ...)` --
 * the reschedule of an upcoming booking. On success it invalidates the list and this booking's own
 * detail query (`['bookings', 'detail', id]`) so both reflect the new dates and price straight away.
 */
export function useUpdateBookingMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<BookingDto, NormalizedApiError, { id: string; payload: UpdateBookingPayload }>(() => ({
    mutationFn: ({ id, payload }) =>
      firstValueFrom(apiClient.put<BookingDto, UpdateBookingPayload>(`bookings/${id}`, payload)),
    onSuccess: (_updated, { id }) =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: BOOKINGS_LIST_QUERY_KEY }),
        queryClient.invalidateQueries({ queryKey: ['bookings', 'detail', id] }),
      ]),
  }));
}
