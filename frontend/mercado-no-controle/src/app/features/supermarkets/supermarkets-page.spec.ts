import { settle } from '../../testing/settle';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { LOCALE_ID, DEFAULT_CURRENCY_CODE } from '@angular/core';
import { registerLocaleData } from '@angular/common';
import localePt from '@angular/common/locales/pt';
import { provideRouter } from '@angular/router';
import { PagedResult } from '../../shared/models/paging';
import { Store } from './stores.service';
import { SupermarketsPage } from './supermarkets-page';

registerLocaleData(localePt);

describe('SupermarketsPage', () => {
  let fixture: ComponentFixture<SupermarketsPage>;
  let http: HttpTestingController;

  const store: Store = {
    id: 1,
    tradeName: 'Mercado Bom',
    legalName: 'Mercado Bom Ltda',
    cnpj: '11222333000181',
    address: null,
    city: 'Campinas',
    state: 'SP',
    notes: null,
    createdAt: '2026-11-20T12:00:00Z',
    receiptCount: 2,
    totalSpent: 30.3,
    lastPurchaseAt: '2026-11-18T09:00:00',
  };

  const page = (items: Store[], totalCount = items.length): PagedResult<Store> => ({
    items,
    page: 1,
    pageSize: 20,
    totalCount,
    totalPages: Math.ceil(totalCount / 20),
  });

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [SupermarketsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: LOCALE_ID, useValue: 'pt-BR' },
        { provide: DEFAULT_CURRENCY_CODE, useValue: 'BRL' },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(SupermarketsPage);
    await settle();
  });

  afterEach(() => {
    vi.useRealTimers();
    http.verify();
  });

  const element = () => fixture.nativeElement as HTMLElement;
  const expectList = (): TestRequest => http.expectOne((req) => req.url === '/api/stores');

  it('requests the first page sorted by name and renders formatted values', async () => {
    const request = expectList();
    expect(request.request.params.toString()).toBe('page=1&pageSize=20&sortBy=tradeName&sortDirection=asc');
    request.flush(page([store]));
    await settle();

    const row = element().querySelector('tr[mat-row]')!.textContent!;
    expect(row).toContain('Mercado Bom');
    expect(row).toContain('11.222.333/0001-81');
    expect(row).toContain('Campinas / SP');
    expect(row).toMatch(/R\$\s*30,30/);
    expect(row).toContain('18/11/2026');
    expect(element().querySelector<HTMLButtonElement>('button[aria-label="Excluir Mercado Bom"]')!.disabled).toBe(true);
  });

  it('debounces the search and returns to the first page', async () => {
    expectList().flush(page([store], 45));
    await settle();

    vi.useFakeTimers();
    const input = element().querySelector<HTMLInputElement>('input[placeholder^="Nome"]')!;
    input.value = 'cam';
    input.dispatchEvent(new Event('input'));
    vi.advanceTimersByTime(100);
    http.expectNone((req) => req.url === '/api/stores');
    vi.advanceTimersByTime(300);
    vi.useRealTimers();
    await settle();

    expect(expectList().request.params.get('search')).toBe('cam');
  });

  it('shows a filtered empty state with a clear button', async () => {
    expectList().flush(page([store]));
    await settle();

    fixture.componentInstance.onStateChange('RJ');
    await settle();
    const request = expectList();
    expect(request.request.params.get('state')).toBe('RJ');
    request.flush(page([]));
    await settle();

    expect(element().textContent).toContain('Nenhum supermercado encontrado');
    expect(element().textContent).toContain('Limpar filtros');
  });

  it('sends sorting and paging changes to the API', async () => {
    expectList().flush(page([store], 45));
    await settle();

    fixture.componentInstance.onSortChange({ active: 'totalSpent', direction: 'desc' });
    await settle();
    const sorted = expectList();
    expect(sorted.request.params.get('sortBy')).toBe('totalSpent');
    expect(sorted.request.params.get('sortDirection')).toBe('desc');
    sorted.flush(page([store], 45));
    await settle();

    fixture.componentInstance.onPage({ pageIndex: 2, pageSize: 20, length: 45 });
    await settle();
    expect(expectList().request.params.get('page')).toBe('3');
  });

  it('shows an error state', async () => {
    expectList().flush({ title: 'Erro interno', detail: 'Falha no banco.' }, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(element().textContent).toContain('Não foi possível carregar os supermercados');
    expect(element().textContent).toContain('Falha no banco.');
  });
});
