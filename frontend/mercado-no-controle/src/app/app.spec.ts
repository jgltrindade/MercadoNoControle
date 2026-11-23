import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Title } from '@angular/platform-browser';
import { TitleStrategy, provideRouter, withComponentInputBinding } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { routes } from './app.routes';
import { AppTitleStrategy } from './core/layout/app-title.strategy';
import { providePtBrDateAdapter } from './core/i18n/date-adapter';

describe('App routing', () => {
  let harness: RouterTestingHarness;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter(routes, withComponentInputBinding()),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: TitleStrategy, useExisting: AppTitleStrategy },
        providePtBrDateAdapter(),
      ],
    });
    harness = await RouterTestingHarness.create();
    http = TestBed.inject(HttpTestingController);
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
