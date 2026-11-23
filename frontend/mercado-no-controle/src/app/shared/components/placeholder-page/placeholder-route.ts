import { Route } from '@angular/router';
import { PlaceholderPage } from './placeholder-page';

interface PlaceholderRouteOptions {
  path?: string;
  title: string;
  description: string;
  phase: string;
  icon: string;
}

/** Cria a rota provisória de uma tela ainda não implementada. */
export function placeholderRoute({ path = '', title, description, phase, icon }: PlaceholderRouteOptions): Route {
  return {
    path,
    title,
    component: PlaceholderPage,
    data: { heading: title, description, phase, icon },
  };
}
