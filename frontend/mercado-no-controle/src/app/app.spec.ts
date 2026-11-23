import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Title } from '@angular/platform-browser';
import { Router, TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { AppTitleStrategy } from './core/layout/app-title.strategy';
import { providePtBrDateAdapter } from './core/i18n/date-adapter';
import { AuthService, AuthStatus } from './core/auth/auth.service';

const signedIn: AuthStatus = {
  enabled: true,
  setupRequired: false,
  authenticated: true,
  user: { id: 1, name: 'Pessoa', email: 'eu@example.com', lastLoginAt: null },
};

/** Sessão simulada: os testes de rotas não dependem da API de autenticação. */
function fakeAuth(status: AuthStatus) {
  return {
    status: signal(status),
    user: signal(status.user),
    enabled: signal(status.enabled),
    ensureLoaded: () => Promise.resolve(status),
    signedOut: () => undefined,
  };
}

describe('App routing', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  async function setup(status: AuthStatus) {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: fakeAuth(status) },
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TitleStrategy, useExisting: AppTitleStrategy },
        providePtBrDateAdapter(),
      ],
    });
    harness = await RouterTestingHarness.create();
    http = TestBed.inject(HttpTestingController);
  }

  beforeEach(async () => {
    await setup(signedIn);
  });

  it('redirects the root URL to the dashboard', async () => {
    await harness.navigateByUrl('/');

    expect(TestBed.inject(Title).getTitle()).toBe('Dashboard · Mercado no Controle');
    expect(harness.routeNativeElement?.textContent).toContain('Dashboard');
  });

  it.each([
    ['/receipts', 'Notas fiscais'],
    ['/receipts/new', 'Nova compra'],
    ['/products', 'Produtos'],
    ['/price-history', 'Histórico de preços'],
    ['/supermarkets', 'Supermercados'],
    ['/categories', 'Categorias'],
    ['/reports', 'Relatórios'],
    ['/settings', 'Configurações'],
  ])('loads %s lazily with its title', async (url, title) => {
    await harness.navigateByUrl(url);

    expect(TestBed.inject(Title).getTitle()).toBe(`${title} · Mercado no Controle`);
    expect(TestBed.inject(AppTitleStrategy).currentPageTitle()).toBe(title);
  });

  it('redirects unknown URLs to the dashboard', async () => {
    await harness.navigateByUrl('/does-not-exist');

    expect(TestBed.inject(AppTitleStrategy).currentPageTitle()).toBe('Dashboard');
  });

  afterEach(() => {
    // As telas carregam dados ao abrir; os testes de roteamento não dependem dessas respostas.
    http.match(() => true);
    http.verify();
  });
});

describe('App routing without a session', () => {
  async function navigate(status: AuthStatus, url: string) {
    TestBed.configureTestingModule({
      providers: [
        { provide: AuthService, useValue: fakeAuth(status) },
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TitleStrategy, useExisting: AppTitleStrategy },
        providePtBrDateAdapter(),
      ],
    });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl(url);
    return harness;
  }

  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    http.match(() => true);
    http.verify();
  });

  it('sends to the login page keeping the requested page', async () => {
    await navigate({ enabled: true, setupRequired: false, authenticated: false, user: null }, '/receipts?status=Draft');

    expect(TestBed.inject(Router).url).toBe('/login?returnUrl=%2Freceipts%3Fstatus%3DDraft');
    expect(TestBed.inject(Title).getTitle()).toBe('Entrar · Mercado no Controle');
  });

  it('shows the first access form when no user exists', async () => {
    const harness = await navigate({ enabled: true, setupRequired: true, authenticated: false, user: null }, '/dashboard');

    expect(TestBed.inject(Router).url.startsWith('/login')).toBe(true);
    expect(harness.routeNativeElement?.textContent).toContain('Primeiro acesso');
  });

  it('opens the application directly when authentication is disabled', async () => {
    await navigate({ enabled: false, setupRequired: false, authenticated: true, user: null }, '/login');

    expect(TestBed.inject(Router).url).toBe('/dashboard');
  });
});
