import { Component, computed, input } from '@angular/core';

export type BookingStatus = 'Active' | 'Completed' | 'Cancelled';

/**
 * What a badge can show: the three real statuses plus "Upcoming", a display-only label for an Active
 * booking that has not started yet (derived in the Bookings feature, never sent by the API).
 */
export type BadgeStatus = BookingStatus | 'Upcoming';

interface BadgeVariant {
  bgClass: string;
  textClass: string;
  dotClass: string;
}

/**
 * The three status-color mappings from `DESIGN.md`'s `badge-active`/`badge-
 * completed`/`badge-cancelled` tokens: Active -> success, Completed -> neutral
 * (deliberately desaturated gray, not green -- "completed is not an achievement,
 * it's just closed"), Cancelled -> danger. A plain lookup table, not a switch --
 * the single place this status-to-color mapping exists (DRY).
 */
const VARIANTS: Record<BadgeStatus, BadgeVariant> = {
  // Upcoming reuses the design system's existing link-blue (AA-contrast in light and dark) on the
  // neutral surface, so it reads as "scheduled, not started" without adding a new color token.
  Upcoming: { bgClass: 'bg-surface-alt', textClass: 'text-link', dotClass: 'bg-link' },
  Active: { bgClass: 'bg-success-bg', textClass: 'text-success-text', dotClass: 'bg-success-dot' },
  Completed: { bgClass: 'bg-neutral-bg', textClass: 'text-neutral-text', dotClass: 'bg-neutral-dot' },
  Cancelled: { bgClass: 'bg-danger-bg', textClass: 'text-danger-text', dotClass: 'bg-danger-dot' },
};

/**
 * The app's first status `Badge` (`{components.badge}`/`{components.badge-active}`/
 * `{components.badge-completed}`/`{components.badge-cancelled}` in `DESIGN.md`),
 * built for Booking's `Status` column (spec-4-1). `status` takes the exact string
 * the backend's `BookingDto.Status` serializes as (`JsonStringEnumConverter`,
 * Program.cs) -- `"Active"`/`"Completed"`/`"Cancelled"` -- so no extra mapping step
 * exists between the wire value and this component's input. Always dot-first,
 * solid (non-translucent) fills per `DESIGN.md`'s "never use full-saturation fills
 * ... badge and status colors use solid fills" rule, and pairs color with the
 * literal status text (`EXPERIENCE.md`'s "no state is signaled by color alone").
 * Deliberately has no knowledge of Booking itself (SRP) -- just a status string in,
 * a colored pill out.
 */
@Component({
  selector: 'app-badge',
  templateUrl: './badge.html',
})
export class Badge {
  readonly status = input.required<BadgeStatus>();

  protected readonly variant = computed(() => VARIANTS[this.status()]);
}
