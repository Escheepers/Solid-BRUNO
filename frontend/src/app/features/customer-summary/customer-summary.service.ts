import { inject } from '@angular/core';
import { injectQuery } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { CustomerSummaryDto } from '../../core/models/customer-summary-dto';

/**
 * Wraps `injectQuery` over `ApiClient.get<CustomerSummaryDto>('customers/{id}/summary')`
 * for the read-only Customer Summary page (spec-5-1), mirroring
 * `useVehicleQuery`/`useBookingQuery`'s exact shape. Query-key convention (AD-3):
 * `['customers', 'summary', id]` — distinct from `['customers', 'detail', id]` (which
 * doesn't exist, since there's no editable Customer detail page) and from
 * `CUSTOMERS_LIST_QUERY_KEY`'s `['customers', 'list']` prefix, so this query never gets
 * invalidated by a Customers-list mutation. `id` is a function (mirroring
 * `useVehicleQuery`'s `id`) so the query reactively re-runs if the route's `id` param
 * changes without remounting `CustomerSummaryPage`. `enabled` is guarded on a defined id
 * so no request fires for an as-yet-unresolved route param.
 */
export function useCustomerSummaryQuery(id: () => string | undefined) {
  const apiClient = inject(ApiClient);

  return injectQuery<CustomerSummaryDto, NormalizedApiError>(() => {
    const customerId = id();

    return {
      queryKey: ['customers', 'summary', customerId] as const,
      queryFn: () =>
        firstValueFrom(apiClient.get<CustomerSummaryDto>(`customers/${customerId}/summary`)),
      enabled: !!customerId,
    };
  });
}
