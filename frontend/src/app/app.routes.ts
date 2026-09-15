import { Routes } from '@angular/router';

import { BookingDetailPage } from './features/bookings/booking-detail-page';
import { BookingsPage } from './features/bookings/bookings-page';
import { CustomerSummaryPage } from './features/customer-summary/customer-summary-page';
import { CustomersPage } from './features/customers/customers-page';
import { VehicleDetailPage } from './features/vehicles/vehicle-detail-page';
import { VehiclesPage } from './features/vehicles/vehicles-page';

export const routes: Routes = [
  { path: '', redirectTo: 'bookings', pathMatch: 'full' },
  { path: 'bookings', component: BookingsPage },
  { path: 'bookings/:id', component: BookingDetailPage },
  { path: 'vehicles', component: VehiclesPage },
  { path: 'vehicles/:id', component: VehicleDetailPage },
  { path: 'customers', component: CustomersPage },
  { path: 'customers/:id/summary', component: CustomerSummaryPage },
];
