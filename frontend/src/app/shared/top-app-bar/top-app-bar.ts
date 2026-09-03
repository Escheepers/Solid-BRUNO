import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

interface NavLink {
  readonly label: string;
  readonly path: string;
}

/**
 * The persistent header (`{components.top-app-bar}` in DESIGN.md) carrying the
 * Bookings/Vehicles/Customers nav. The active section is marked by color, bold
 * weight, underline, AND `aria-current="page"` — never color alone, per
 * EXPERIENCE.md's Accessibility Floor.
 */
@Component({
  selector: 'app-top-app-bar',
  imports: [RouterLink, RouterLinkActive],
  templateUrl: './top-app-bar.html',
})
export class TopAppBar {
  protected readonly links: readonly NavLink[] = [
    { label: 'Bookings', path: '/bookings' },
    { label: 'Vehicles', path: '/vehicles' },
    { label: 'Customers', path: '/customers' },
  ];
}
