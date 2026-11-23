import { Routes } from '@angular/router';
import { placeholderRoute } from '../../shared/components/placeholder-page/placeholder-route';

export const REPORTS_ROUTES: Routes = [
  placeholderRoute({
    title: 'Relatórios',
    description: 'Relatórios filtráveis com exportação em CSV, Excel e PDF.',
    phase: 'Fase 6',
    icon: 'analytics',
  }),
];
