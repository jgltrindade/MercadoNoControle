import { settle } from '../../testing/settle';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { NotificationService } from '../../core/services/notification.service';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { ProductDetailsPage } from './product-details-page';
import { Product } from './products.service';

describe('ProductDetailsPage', () => {
  let fixture: ComponentFixture<ProductDetailsPage>;
  let http: HttpTestingController;
  const success = vi.fn();

  const product: Product = {
    id: 7,
    name: 'Leite Integral 1L',
    brand: 'Marca',
    categoryId: 4,
    categoryName: 'Laticínios',
    barcode: null,
    unit: 'L',
    packageContent: 1,
    status: 'Active',
    createdAt: '2026-11-20T12:00:00Z',
    aliases: [
      { id: 1, description: 'LEITE INT 1L', confirmed: true, source: 'Manual', createdAt: '' },
      { id: 2, description: 'LT INT 1000ML', confirmed: false, source: 'AiSuggestion', createdAt: '' },
    ],
  };

  beforeEach(async () => {
    success.mockClear();
    TestBed.configureTestingModule({
      imports: [ProductDetailsPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: NotificationService, useValue: { success, error: vi.fn() } },
        { provide: ConfirmService, useValue: { confirm: () => of(true) } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProductDetailsPage);
    fixture.componentRef.setInput('id', 7);
    await settle();
  });

  afterEach(() => {
    // O resumo do histórico de preços é carregado junto com o produto.
    http.match((req) => req.url === '/api/products/7/prices');
    http.verify();
  });

  const element = () => fixture.nativeElement as HTMLElement;

  async function loadProduct(body: Product = product) {
    http.expectOne('/api/products/7').flush(body);
    await settle();
  }

  it('shows product data and aliases with their confirmation state', async () => {
    await loadProduct();

    const text = element().textContent!;
    expect(text).toContain('Leite Integral 1L');
    expect(text).toContain('Laticínios');
    expect(text).toContain('1 L');
    expect(text).toContain('Manual · Confirmada');
    expect(text).toContain('Sugestão da IA · Aguardando confirmação');
    expect(element().querySelectorAll('button[mattooltip="Confirmar associação"]').length).toBe(1);
  });

  it('adds an alias and reloads the product', async () => {
    await loadProduct();
    const input = element().querySelector<HTMLInputElement>('.alias-form input')!;
    input.value = 'LEITE INTEGRAL';
    input.dispatchEvent(new Event('input'));

    element().querySelector('.alias-form')!.dispatchEvent(new Event('submit'));
    await settle();

    const request = http.expectOne({ method: 'POST', url: '/api/products/7/aliases' });
    expect(request.request.body).toEqual({ description: 'LEITE INTEGRAL' });
    request.flush({ id: 3, description: 'LEITE INTEGRAL', confirmed: true, source: 'Manual', createdAt: '' });
    await settle();
    expect(success).toHaveBeenCalledWith('Descrição associada ao produto.');
    await loadProduct();
  });

  it('shows a duplicate alias error on the field', async () => {
    await loadProduct();
    const input = element().querySelector<HTMLInputElement>('.alias-form input')!;
    input.value = 'leite int 1l';
    input.dispatchEvent(new Event('input'));

    element().querySelector('.alias-form')!.dispatchEvent(new Event('submit'));
    await settle();
    http
      .expectOne('/api/products/7/aliases')
      .flush({ title: 'Conflito', detail: 'A descrição já está associada a este produto.' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(element().textContent).toContain('A descrição já está associada a este produto.');
  });

  it('confirms a suggested alias', async () => {
    await loadProduct();

    element().querySelector<HTMLButtonElement>('button[mattooltip="Confirmar associação"]')!.click();
    await settle();

    http.expectOne({ method: 'PUT', url: '/api/products/7/aliases/2/confirm' }).flush({});
    await settle();
    await loadProduct();
  });

  it('shows a not-found state', async () => {
    http.expectOne('/api/products/7').flush({ title: 'Recurso não encontrado' }, { status: 404, statusText: 'Not Found' });
    await settle();

    expect(element().textContent).toContain('Produto não encontrado');
  });
});
