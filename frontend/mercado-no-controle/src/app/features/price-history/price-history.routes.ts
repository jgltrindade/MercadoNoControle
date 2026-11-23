import { Routes } from '@angular/router';

export const PRICE_HISTORY_ROUTES: Routes = [
  {
    path: '',
    title: 'Histórico de preços',
    loadComponent: () => import('./price-history-page').then((m) => m.PriceHistoryPage),
  },
];
