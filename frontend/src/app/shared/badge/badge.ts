import { Component, computed, input } from '@angular/core';

export type BookingStatus = 'Active' | 'Completed' | 'Cancelled';

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
const VARIANTS: Record<BookingStatus, BadgeVariant> = {
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
  readonly status = input.required<BookingStatus>();

  protected readonly variant = computed(() => VARIANTS[this.status()]);
}
