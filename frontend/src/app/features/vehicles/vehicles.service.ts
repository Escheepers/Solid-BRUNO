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
 * Wraps `injectMutation` over `ApiClient.post('vehicles/{id}/deactivate', ...)` -- mirrors the
 * `injectMutation` pattern `VehicleFormModal`'s create/update mutation already established.
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
