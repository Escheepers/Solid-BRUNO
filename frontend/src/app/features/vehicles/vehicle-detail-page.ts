import { Component, computed, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { map } from 'rxjs';

import { Skeleton } from '../../shared/skeleton/skeleton';
import { toVehicle } from './models/vehicle';
import { currencyFormatter, dateFormatter } from './vehicle-formatters';
import { useVehicleQuery } from './vehicles.service';

/**
 * The Vehicles feature's single-record view (Story 2.5, the last story in Epic 2).
 * Reads the route's `id` param, drives `useVehicleQuery`, and renders exactly the
 * three states its AC describes -- loading (`Skeleton`, shaped for a detail card),
 * not-found (a dedicated message + a link back to the list), and found (the full
 * record, reusing the list's own currency/date formatting). Deliberately has no
 * Edit/Deactivate/Restore actions of its own (spec-2-5's Scope decision 2 -- those
 * stay on the list) and no booking-history section (Epic 4's job) -- this component's
 * only responsibility is rendering one vehicle's record in one of its three states
 * (SRP).
 */
@Component({
  selector: 'app-vehicle-detail-page',
  imports: [Skeleton, RouterLink],
  templateUrl: './vehicle-detail-page.html',
})
export class VehicleDetailPage {
  private readonly vehicleId = toSignal(
    inject(ActivatedRoute).paramMap.pipe(map((params) => params.get('id') ?? undefined)),
  );

  protected readonly query = useVehicleQuery(() => this.vehicleId());

  protected readonly vehicle = computed(() => {
    const dto = this.query.data();
    return dto ? toVehicle(dto) : null;
  });

  /**
   * Narrows down to the one error case this story's AC actually requires
   * distinguishing (a real `NotFoundError`, Story 2.5's own addition to
   * `NormalizedApiError`). Any other error (a genuine `ServerError`) falls through to
   * the template's generic fallback -- not explicitly tested by this story's AC, but
   * still never a blank page.
   */
  protected readonly isNotFound = computed(() => this.query.error()?.kind === 'not-found');

  protected readonly formattedDailyRate = computed(() => {
    const vehicle = this.vehicle();
    return vehicle ? currencyFormatter.format(vehicle.dailyRate) : '';
  });

  protected readonly formattedCreatedDate = computed(() => {
    const vehicle = this.vehicle();
    return vehicle ? dateFormatter.format(vehicle.createdDate) : '';
  });
}
