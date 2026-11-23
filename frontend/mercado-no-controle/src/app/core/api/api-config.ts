import { InjectionToken } from '@angular/core';

/**
 * URL base da API. Em desenvolvimento, `/api` é encaminhado ao backend pelo
 * proxy do Angular (proxy.conf.json), evitando CORS e URLs fixas no código.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '/api',
});
