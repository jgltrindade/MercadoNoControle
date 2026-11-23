import { Routes } from '@angular/router';
import { placeholderRoute } from '../../shared/components/placeholder-page/placeholder-route';

export const RECEIPTS_ROUTES: Routes = [
  placeholderRoute({
    title: 'Notas fiscais',
    description: 'Consulta, edição e exclusão das compras registradas.',
    phase: 'Fase 3',
    icon: 'receipt_long',
  }),
  placeholderRoute({
    path: 'new',
    title: 'Nova compra',
    description: 'Cadastro manual de compras e envio da fotografia da nota fiscal para leitura.',
    phase: 'Fase 3',
    icon: 'add_shopping_cart',
  }),
];
