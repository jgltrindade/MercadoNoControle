import { Routes } from '@angular/router';
import { authGuard, guestGuard } from './core/auth/auth.guards';
import { Shell } from './core/layout/shell/shell';

/** Rotas principais. Cada funcionalidade é carregada sob demanda (lazy loading). */
export const routes: Routes = [
  {
    path: 'login',
    title: 'Entrar',
    canActivate: [guestGuard],
    loadComponent: () => import('./features/auth/login-page').then((m) => m.LoginPage),
  },
  {
    path: '',
    component: Shell,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      {
        path: 'dashboard',
        loadChildren: () => import('./features/dashboard/dashboard.routes').then((m) => m.DASHBOARD_ROUTES),
      },
      {
        path: 'receipts',
        loadChildren: () => import('./features/receipts/receipts.routes').then((m) => m.RECEIPTS_ROUTES),
      },
      {
        path: 'products',
        loadChildren: () => import('./features/products/products.routes').then((m) => m.PRODUCTS_ROUTES),
      },
      {
        path: 'price-history',
        loadChildren: () =>
          import('./features/price-history/price-history.routes').then((m) => m.PRICE_HISTORY_ROUTES),
      },
      {
        path: 'supermarkets',
        loadChildren: () =>
          import('./features/supermarkets/supermarkets.routes').then((m) => m.SUPERMARKETS_ROUTES),
      },
      {
        path: 'categories',
        loadChildren: () => import('./features/categories/categories.routes').then((m) => m.CATEGORIES_ROUTES),
      },
      {
        path: 'reports',
        loadChildren: () => import('./features/reports/reports.routes').then((m) => m.REPORTS_ROUTES),
      },
      {
        path: 'settings',
        loadChildren: () => import('./features/settings/settings.routes').then((m) => m.SETTINGS_ROUTES),
      },
      { path: '**', redirectTo: 'dashboard' },
    ],
  },
];
