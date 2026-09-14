/**
 * The single source of truth for how a `Customer`'s date fields are formatted,
 * mirroring `features/vehicles/vehicle-formatters.ts`'s `dateFormatter` exactly (same
 * `en-ZA` config). Kept as its own instance here rather than importing the Vehicles
 * one — each feature folder stays a self-contained module with no cross-feature
 * imports anywhere else in the codebase; this is one small `Intl.DateTimeFormat`
 * config, not enough shared behavior to justify introducing that coupling. Customer
 * has no currency field, so no currency formatter is needed here.
 */
export const dateFormatter = new Intl.DateTimeFormat('en-ZA', {
  year: 'numeric',
  month: 'short',
  day: 'numeric',
});
