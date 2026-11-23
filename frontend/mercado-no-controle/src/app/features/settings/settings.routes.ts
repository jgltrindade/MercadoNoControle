import { Routes } from '@angular/router';
import { placeholderRoute } from '../../shared/components/placeholder-page/placeholder-route';

export const SETTINGS_ROUTES: Routes = [
  placeholderRoute({
    title: 'Configurações',
    description: 'Provedor de IA, preferências de exibição, backup e gerenciamento de imagens.',
    phase: 'Fase 7',
    icon: 'settings',
  }),
];
