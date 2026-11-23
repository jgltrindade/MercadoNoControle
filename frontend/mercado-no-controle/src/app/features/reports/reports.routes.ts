import { Routes } from '@angular/router';

export const REPORTS_ROUTES: Routes = [
  {
    path: '',
    title: 'Relatórios',
    loadComponent: () => import('./reports-page').then((m) => m.ReportsPage),
  },
];
