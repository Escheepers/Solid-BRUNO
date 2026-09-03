import { inject } from '@angular/core';
import { injectQuery } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { PagedResult } from '../../core/models/paged-result';
import { VehicleDto } from '../../core/models/vehicle-dto';

export interface VehiclesQueryParams {
  page: number;
  pageSize: number;
  search: string;
}

/**
 * Wraps `injectQuery` over `ApiClient.get<PagedResult<VehicleDto>>('vehicles', ...)`.
 * Query-key convention (AD-3): `['vehicles', 'list', { page, pageSize, search }]` —
 * exact, so cache entries are keyed identically across every consumer of this query.
 * `params` is a function (not a plain object) so `injectQuery`'s reactive context
 * re-runs the query whenever any Signal it reads (page/pageSize/search) changes.
 */
export function useVehiclesQuery(params: () => VehiclesQueryParams) {
  const apiClient = inject(ApiClient);

  return injectQuery(() => {
    const { page, pageSize, search } = params();

    return {
      queryKey: ['vehicles', 'list', { page, pageSize, search }] as const,
      queryFn: () =>
        firstValueFrom(
          apiClient.get<PagedResult<VehicleDto>>('vehicles', { page, pageSize, search }),
        ),
    };
  });
}
