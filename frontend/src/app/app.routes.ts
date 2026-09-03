import { Routes } from '@angular/router';

import { BookingsPage } from './features/bookings/bookings-page';
import { CustomersPage } from './features/customers/customers-page';
import { VehiclesPage } from './features/vehicles/vehicles-page';

export const routes: Routes = [
  { path: '', redirectTo: 'bookings', pathMatch: 'full' },
  { path: 'bookings', component: BookingsPage },
  { path: 'vehicles', component: VehiclesPage },
  { path: 'customers', component: CustomersPage },
];
