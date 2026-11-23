import { Routes } from '@angular/router';
import { placeholderRoute } from '../../shared/components/placeholder-page/placeholder-route';

export const DASHBOARD_ROUTES: Routes = [
  placeholderRoute({
    title: 'Dashboard',
    description: 'Indicadores de gastos, evolução mensal e distribuição por supermercado e categoria.',
    phase: 'Fase 6',
    icon: 'dashboard',
  }),
];
