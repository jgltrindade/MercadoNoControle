import { Routes } from '@angular/router';

export const RECEIPTS_ROUTES: Routes = [
  {
    path: '',
    title: 'Notas fiscais',
    loadComponent: () => import('./receipts-page').then((m) => m.ReceiptsPage),
  },
  {
    path: 'new',
    title: 'Nova compra',
    loadComponent: () => import('./receipt-form-page').then((m) => m.ReceiptFormPage),
  },
  {
    path: 'imports/:importId',
    title: 'Conferência da nota',
    loadComponent: () => import('./import-review-page').then((m) => m.ImportReviewPage),
  },
  {
    path: ':id',
    title: 'Detalhes da compra',
    loadComponent: () => import('./receipt-details-page').then((m) => m.ReceiptDetailsPage),
  },
  {
    path: ':id/edit',
    title: 'Editar compra',
    loadComponent: () => import('./receipt-form-page').then((m) => m.ReceiptFormPage),
  },
];
