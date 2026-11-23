import { Routes } from '@angular/router';
import { placeholderRoute } from '../../shared/components/placeholder-page/placeholder-route';

export const PRICE_HISTORY_ROUTES: Routes = [
  placeholderRoute({
    title: 'Histórico de preços',
    description: 'Evolução de preços por produto e comparação entre supermercados.',
    phase: 'Fase 5',
    icon: 'monitoring',
  }),
];
