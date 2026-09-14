import { inject } from '@angular/core';
import { QueryClient, injectMutation, injectQuery } from '@tanstack/angular-query-experimental';
import { firstValueFrom } from 'rxjs';

import { ApiClient } from '../../core/api-client/api-client';
import { NormalizedApiError } from '../../core/api-client/normalized-api-error';
import { CustomerDto } from '../../core/models/customer-dto';
import { PagedResult } from '../../core/models/paged-result';

export interface CustomersQueryParams {
  page: number;
  pageSize: number;
  search: string;
}

export interface CreateCustomerPayload {
  firstName: string;
  lastName: string;
  email: string;
  phoneNumber: string;
}

/** Matches `useCustomersQuery`'s query-key convention (AD-3) exactly, minus the params
 * — `invalidateQueries` matches every params variant sharing this key prefix. */
export const CUSTOMERS_LIST_QUERY_KEY = ['customers', 'list'] as const;

/**
 * Wraps `injectQuery` over `ApiClient.get<PagedResult<CustomerDto>>('customers', ...)`,
 * mirroring `useVehiclesQuery`'s exact shape (spec-1-7/spec-2-1). Query-key convention
 * (AD-3): `['customers', 'list', { page, pageSize, search }]` — no `showInactive`
 * param, Customer doesn't have that yet. `params` is a function (not a plain object)
 * so `injectQuery`'s reactive context re-runs the query whenever any Signal it reads
 * (page/pageSize/search) changes.
 */
export function useCustomersQuery(params: () => CustomersQueryParams) {
  const apiClient = inject(ApiClient);

  return injectQuery(() => {
    const { page, pageSize, search } = params();

    return {
      queryKey: ['customers', 'list', { page, pageSize, search }] as const,
      queryFn: () =>
        firstValueFrom(
          apiClient.get<PagedResult<CustomerDto>>('customers', { page, pageSize, search }),
        ),
    };
  });
}

/**
 * Wraps `injectMutation` over `ApiClient.post<CustomerDto, CreateCustomerPayload>('customers', ...)`
 * — mirrors the create-mutation pattern `VehicleFormModal` inlines for its own
 * create case. On success, invalidates `['customers', 'list']` (AD-3) so the list
 * re-fetches and the newly-created customer appears.
 */
export function useCreateCustomerMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<CustomerDto, NormalizedApiError, CreateCustomerPayload>(() => ({
    mutationFn: (payload) =>
      firstValueFrom(apiClient.post<CustomerDto, CreateCustomerPayload>('customers', payload)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_LIST_QUERY_KEY }),
  }));
}
