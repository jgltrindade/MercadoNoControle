import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom, tap } from 'rxjs';
import { API_BASE_URL } from '../api/api-config';

export interface AuthUser {
  id: number;
  name: string;
  email: string;
  lastLoginAt: string | null;
}

export interface AuthStatus {
  enabled: boolean;
  setupRequired: boolean;
  authenticated: boolean;
  user: AuthUser | null;
}

export interface LoginRequest {
  email: string;
  password: string;
  rememberMe: boolean;
}

export interface SetupRequest {
  name: string;
  email: string;
  password: string;
}

/**
 * Sessão do usuário único. A sessão fica num cookie HttpOnly emitido pela API; o frontend só
 * conhece o resultado de `/api/auth/status`.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly state = signal<AuthStatus | null>(null);
  private pending: Promise<AuthStatus> | null = null;

  readonly status = this.state.asReadonly();
  readonly user = computed(() => this.state()?.user ?? null);
  /** Autenticação ativa na instalação (exibe "Sair" e a seção de conta). */
  readonly enabled = computed(() => this.state()?.enabled ?? false);

  /** Carrega a situação da sessão uma vez (ou de novo, com `force`). */
  ensureLoaded(force = false): Promise<AuthStatus> {
    const current = this.state();
    if (current && !force) {
      return Promise.resolve(current);
    }
    this.pending ??= firstValueFrom(this.http.get<AuthStatus>(`${this.baseUrl}/auth/status`))
      .then((status) => {
        this.state.set(status);
        return status;
      })
      .finally(() => (this.pending = null));
    return this.pending;
  }

  login(request: LoginRequest): Observable<AuthUser> {
    return this.http.post<AuthUser>(`${this.baseUrl}/auth/login`, request).pipe(tap((user) => this.signedIn(user)));
  }

  setup(request: SetupRequest): Observable<AuthUser> {
    return this.http.post<AuthUser>(`${this.baseUrl}/auth/setup`, request).pipe(tap((user) => this.signedIn(user)));
  }

  logout(): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/auth/logout`, {}).pipe(tap(() => this.signedOut()));
  }

  changePassword(currentPassword: string, newPassword: string): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/auth/password`, { currentPassword, newPassword });
  }

  updateProfile(name: string, email: string): Observable<AuthUser> {
    return this.http
      .put<AuthUser>(`${this.baseUrl}/auth/profile`, { name, email })
      .pipe(tap((user) => this.state.update((s) => (s ? { ...s, user } : s))));
  }

  /** Sessão expirada ou encerrada (resposta 401 da API). */
  signedOut(): void {
    this.state.update((s) => (s ? { ...s, authenticated: false, user: null } : s));
  }

  private signedIn(user: AuthUser): void {
    this.state.update((s) => ({ enabled: s?.enabled ?? true, setupRequired: false, authenticated: true, user }));
  }
}
