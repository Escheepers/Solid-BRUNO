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

/** Identical shape to `CreateCustomerPayload` -- kept as its own named type so `useUpdateCustomerMutation`'s
 * call site reads clearly, mirroring `CreateCustomerPayload`'s own naming. */
export type UpdateCustomerPayload = CreateCustomerPayload;

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

/**
 * Wraps `injectMutation` over `ApiClient.put<CustomerDto, UpdateCustomerPayload>('customers/{id}', ...)`
 * -- the update counterpart to `useCreateCustomerMutation` above, added for Story 3.2's Edit flow.
 * `CustomerFormModal` selects between this and `useCreateCustomerMutation` based on whether it was
 * opened for create or edit (its own concern, not this service's -- SRP). On success, invalidates
 * `['customers', 'list']` (AD-3) exactly like the create mutation, so the list re-fetches and shows
 * the edited values.
 */
export function useUpdateCustomerMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<
    CustomerDto,
    NormalizedApiError,
    { customerId: string; payload: UpdateCustomerPayload }
  >(() => ({
    mutationFn: ({ customerId, payload }) =>
      firstValueFrom(
        apiClient.put<CustomerDto, UpdateCustomerPayload>(`customers/${customerId}`, payload),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_LIST_QUERY_KEY }),
  }));
}

/**
 * Wraps `injectMutation` over `ApiClient.delete<void>('customers/{id}', ...)` -- added
 * for Story 3.3's hard-delete flow. This is the app's first genuine `DELETE`
 * (`ApiClient.delete<T>` takes no body, unlike `useDeactivateVehicleMutation`'s
 * body-less `POST`), mirroring `useDeactivateVehicleMutation`'s shape otherwise: same
 * `injectMutation<void, NormalizedApiError, string>` signature, same
 * invalidate-on-success behaviour so the list re-fetches and the deleted customer
 * disappears.
 */
export function useHardDeleteCustomerMutation() {
  const apiClient = inject(ApiClient);
  const queryClient = inject(QueryClient);

  return injectMutation<void, NormalizedApiError, string>(() => ({
    mutationFn: (customerId: string) => firstValueFrom(apiClient.delete<void>(`customers/${customerId}`)),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: CUSTOMERS_LIST_QUERY_KEY }),
  }));
}
