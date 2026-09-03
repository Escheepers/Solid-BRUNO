import { Component, computed, input } from '@angular/core';

/**
 * Reusable loading placeholder (`{components.skeleton}` in DESIGN.md) — a flat
 * `bg-surface-alt` block per row, deliberately with no shimmer animation (kept
 * simple and calm, consistent with the rest of the palette's restraint). `aria-busy`
 * is set on this component's own host element so any container using `Skeleton` for
 * its loading state (e.g. `DataTable`) gets the right accessibility signal for free —
 * never a spinner, per `EXPERIENCE.md`.
 */
@Component({
  selector: 'app-skeleton',
  templateUrl: './skeleton.html',
  host: {
    '[attr.aria-busy]': 'true',
  },
})
export class Skeleton {
  readonly rows = input(5);

  protected readonly rowIndexes = computed(() => Array.from({ length: this.rows() }, (_, i) => i));
}
