import { inject } from '@angular/core';
import { QueryClient, injectMutation, injectQuery } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { PagedResult } from '../../core/models/paged-result';
import { VehicleDto } from '../../core/models/vehicle-dto';

export interface VehiclesQueryParams {
  page: number;
  pageSize: number;
  search: string;
  showInactive: boolean;
}

export interface CreateVehiclePayload {
  registrationNumber: string;
  make: string;
  model: string;
  year: number;
  dailyRate: number;
}

/** Identical shape to `CreateVehiclePayload` -- kept as its own named type so `useUpdateVehicleMutation`'s
 * call site reads clearly, mirroring `CreateCustomerPayload`/`UpdateCustomerPayload`'s own naming. */
export type UpdateVehiclePayload = CreateVehiclePayload;

/** Matches `useVehiclesQuery`'s query-key convention (AD-3) exactly, minus the params. */
const VEHICLES_LIST_QUERY_KEY = ['vehicles', 'list'] as const;

/**
 * Wraps `injectQuery` over `ApiClient.get<PagedResult<VehicleDto>>('vehicles', ...)`.
 * Query-key convention (AD-3): `['vehicles', 'list', { page, pageSize, search, showInactive }]`
 * — exact, so cache entries are keyed identically across every consumer of this query.
 * `params` is a function (not a plain object) so `injectQuery`'s reactive context
 * re-runs the query whenever any Signal it reads (page/pageSize/search/showInactive) changes.
 */
export function useVehiclesQuery(params: () => VehiclesQueryParams) {
  const apiClient = inject(ApiClient);

  return injectQuery(() => {
    const { page, pageSize, search, showInactive } = params();

    return {
      queryKey: ['vehicles', 'list', { page, pageSize, search, showInactive }] as const,
      queryFn: () =>
        firstValueFrom(
          apiClient.get<PagedResult<VehicleDto>>('vehicles', {
            page,
            pageSize,
            search,
            showInactive,
          }),
        ),
    };
  });
}

/**
 * Wraps `injectQuery` over `ApiClient.get<VehicleDto>('vehicles/{id}')` for a single
 * vehicle's detail (Story 2.5). Query-key convention (AD-3):
 * `['vehicles', 'detail', id]`. `id` is a function (mirroring `useVehiclesQuery`'s
 * `params`) so the query reactively re-runs if the route's `id` param changes without
 * remounting `VehicleDetailPage`. `enabled` is guarded on a defined id so no request
 * fires for an as-yet-unresolved route param -- `firstValueFrom` would otherwise be
 * called with `vehicles/undefined` in the URL.
 */
export function useVehicleQuery(id: () => string | undefined) {
  const apiClient = inject(ApiClient);

  return injectQuery<VehicleDto, NormalizedApiError>(() => {
    const vehicleId = id();

    return {
      queryKey: ['vehicles', 'detail', vehicleId] as const,
      queryFn: () => firstValueFrom(apiClient.get<VehicleDto>(`vehicles/${vehicleId}`)),
      enabled: !!vehicleId,
    };
  });
}

/**
 * Wraps `injectMutation` over `ApiClient.post<VehicleDto, CreateVehiclePayload>('vehicles', ...)`
 * -- mirrors `useCreateCustomerMutation`'s exact shape (audit fix: this hook and
 * `useUpdateVehicleMutation` below used to be inlined directly in `VehicleFormModal`, unlike every
 * other form's create/update mutations, which already lived in their own `*.service.ts`; moved here
 * for consistency, with no change to the underlying HTTP call or invalidation behaviour). On success,
 * invalidates `['vehicles', 'list']` (AD-3) so the list re-fetches and the newly-created vehicle appears.
 */
export function useCreateVehicleMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<VehicleDto, NormalizedApiError, CreateVehiclePayload>(() => ({
    mutationFn: (payload) =>
      firstValueFrom(apiClient.post<VehicleDto, CreateVehiclePayload>('vehicles', payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: VEHICLES_LIST_QUERY_KEY }),
  }));
}

/**
 * Wraps `injectMutation` over `ApiClient.put<VehicleDto, UpdateVehiclePayload>('vehicles/{id}', ...)`
 * -- the update counterpart to `useCreateVehicleMutation` above, mirroring
 * `useUpdateCustomerMutation`'s exact shape. `VehicleFormModal` selects between this and
 * `useCreateVehicleMutation` based on whether it was opened for create or edit (its own concern, not
 * this service's -- SRP). On success, invalidates `['vehicles', 'list']` (AD-3) exactly like the
 * create mutation, so the list re-fetches and shows the edited values.
 */
export function useUpdateVehicleMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<
    VehicleDto,
    NormalizedApiError,
    { vehicleId: string; payload: UpdateVehiclePayload }
  >(() => ({
    mutationFn: ({ vehicleId, payload }) =>
      firstValueFrom(
        apiClient.put<VehicleDto, UpdateVehiclePayload>(`vehicles/${vehicleId}`, payload),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: VEHICLES_LIST_QUERY_KEY }),
  }));
}

/**
 * Wraps `injectMutation` over `ApiClient.post('vehicles/{id}/deactivate', ...)` -- mirrors the
 * `injectMutation` pattern `useCreateVehicleMutation`/`useUpdateVehicleMutation` above establish.
 * `ApiClient.post<T, B>` requires a body argument, so `undefined` is passed explicitly for this
 * body-less action. On success, invalidates `['vehicles', 'list']` (AD-3) so the list re-fetches
 * and the deactivated vehicle disappears via the existing soft-delete query filter -- no new
 * frontend filtering logic needed.
 */
export function useDeactivateVehicleMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<void, NormalizedApiError, string>(() => ({
    mutationFn: (vehicleId: string) =>
      firstValueFrom(apiClient.post<void, undefined>(`vehicles/${vehicleId}/deactivate`, undefined)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: VEHICLES_LIST_QUERY_KEY }),
  }));
}

/**
 * Wraps `injectMutation` over `ApiClient.post('vehicles/{id}/restore', ...)` -- mirrors
 * `useDeactivateVehicleMutation`'s exact pattern (same body-less POST / invalidate-on-success
 * shape). On success, invalidates `['vehicles', 'list']` (AD-3) so both the default and
 * "show inactive" views re-fetch and the restored vehicle moves accordingly.
 */
export function useRestoreVehicleMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<void, NormalizedApiError, string>(() => ({
    mutationFn: (vehicleId: string) =>
      firstValueFrom(apiClient.post<void, undefined>(`vehicles/${vehicleId}/restore`, undefined)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: VEHICLES_LIST_QUERY_KEY }),
  }));
}
