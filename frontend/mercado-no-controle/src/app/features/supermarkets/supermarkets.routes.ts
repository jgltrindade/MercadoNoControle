import { Routes } from '@angular/router';

export const SUPERMARKETS_ROUTES: Routes = [
  {
    path: '',
    title: 'Supermercados',
    loadComponent: () => import('./supermarkets-page').then((m) => m.SupermarketsPage),
  },
];
