/**
 * The one paginated-response shape every paginated endpoint (Vehicles, Customers,
 * Bookings) returns — see ARCHITECTURE-SPINE.md AD-14. `items` carries `T` (a raw
 * wire-format DTO for whichever entity is being paged); mapping to a feature's view
 * model happens downstream of this shape, never inside it.
 */
export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}
