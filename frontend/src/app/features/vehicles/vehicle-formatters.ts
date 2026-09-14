/**
 * The single source of truth for how a `Vehicle`'s currency/date fields are
 * formatted, shared by `VehiclesPage` (the list) and `VehicleDetailPage` (Story
 * 2.5) so both surfaces render `dailyRate`/`createdDate` identically -- extracted
 * here rather than duplicated per the spec's DRY requirement.
 */
export const currencyFormatter = new Intl.NumberFormat('en-ZA', {
  style: 'currency',
  currency: 'ZAR',
});

export const dateFormatter = new Intl.DateTimeFormat('en-ZA', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});
