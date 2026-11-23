import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';

/** Áreas da aplicação: exigem sessão quando a autenticação está ativa. */
export const authGuard: CanActivateFn = async (_route, state) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  try {
    const status = await auth.ensureLoaded();
    if (!status.enabled || status.authenticated) {
      return true;
    }
  } catch {
    // API indisponível: deixa a aplicação abrir; as telas mostram o erro de conexão.
    return true;
  }
  return router.createUrlTree(['/login'], { queryParams: returnUrlParams(state.url) });
};

/** Tela de login/cadastro inicial: desnecessária com sessão ativa ou autenticação desligada. */
export const guestGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  try {
    const status = await auth.ensureLoaded(true);
    return !status.enabled || status.authenticated ? router.createUrlTree(['/dashboard']) : true;
  } catch {
    return true;
  }
};

/** Sessão expirada durante o uso: volta para o login, preservando a tela atual. */
export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return next(request).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401 && !request.url.includes('/auth/')) {
        auth.signedOut();
        const current = router.url;
        if (!current.startsWith('/login')) {
          void router.navigate(['/login'], { queryParams: returnUrlParams(current) });
        }
      }
      return throwError(() => error);
    }),
  );
};

function returnUrlParams(url: string): Record<string, string> {
  return url && url !== '/' && !url.startsWith('/login') ? { returnUrl: url } : {};
}

/** Aceita apenas caminhos internos como destino após o login (evita redirecionamento aberto). */
export function safeReturnUrl(value: string | null | undefined): string {
  return value && value.startsWith('/') && !value.startsWith('//') && !value.startsWith('/login') ? value : '/dashboard';
}
