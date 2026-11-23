import { Routes } from '@angular/router';

export const CATEGORIES_ROUTES: Routes = [
  {
    path: '',
    title: 'Categorias',
    loadComponent: () => import('./categories-page').then((m) => m.CategoriesPage),
  },
];
