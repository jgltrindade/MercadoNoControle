import { Routes } from '@angular/router';

export const PRODUCTS_ROUTES: Routes = [
  {
    path: '',
    title: 'Produtos',
    loadComponent: () => import('./products-page').then((m) => m.ProductsPage),
  },
  {
    path: ':id',
    title: 'Detalhes do produto',
    loadComponent: () => import('./product-details-page').then((m) => m.ProductDetailsPage),
  },
];
