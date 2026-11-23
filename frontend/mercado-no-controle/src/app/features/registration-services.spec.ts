import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { CategoriesService } from './categories/categories.service';
import { ProductsService } from './products/products.service';
import { StoresService } from './supermarkets/stores.service';

describe('Registration HTTP services', () => {
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('CategoriesService uses /api/categories', () => {
    const service = TestBed.inject(CategoriesService);

    service.list().subscribe();
    service.create({ name: 'Pet', description: null }).subscribe();
    service.update(3, { name: 'Pet', description: 'x' }).subscribe();
    service.delete(3).subscribe();

    expect(http.expectOne({ method: 'GET', url: '/api/categories' })).toBeTruthy();
    expect(http.expectOne({ method: 'POST', url: '/api/categories' }).request.body).toEqual({ name: 'Pet', description: null });
    expect(http.expectOne({ method: 'PUT', url: '/api/categories/3' })).toBeTruthy();
    expect(http.expectOne({ method: 'DELETE', url: '/api/categories/3' })).toBeTruthy();
  });

  it('StoresService sends paging, filters and sorting as query params', () => {
    const service = TestBed.inject(StoresService);

    service
      .list({ page: 2, pageSize: 10, search: 'bom', state: 'SP', sortBy: 'totalSpent', sortDirection: 'desc' })
      .subscribe();

    const request = http.expectOne((req) => req.url === '/api/stores');
    expect(request.request.params.toString()).toBe('page=2&pageSize=10&search=bom&state=SP&sortBy=totalSpent&sortDirection=desc');
  });

  it('StoresService CRUD endpoints', () => {
    const service = TestBed.inject(StoresService);
    const body = { tradeName: 'A', legalName: null, cnpj: null, address: null, city: null, state: null, notes: null };

    service.get(5).subscribe();
    service.create(body).subscribe();
    service.update(5, body).subscribe();
    service.delete(5).subscribe();

    http.expectOne({ method: 'GET', url: '/api/stores/5' });
    http.expectOne({ method: 'POST', url: '/api/stores' });
    http.expectOne({ method: 'PUT', url: '/api/stores/5' });
    http.expectOne({ method: 'DELETE', url: '/api/stores/5' });
  });

  it('ProductsService list, CRUD and alias endpoints', () => {
    const service = TestBed.inject(ProductsService);
    const body = {
      name: 'Leite',
      brand: null,
      categoryId: 4,
      barcode: null,
      unit: 'L' as const,
      packageContent: 1,
      status: 'Active' as const,
    };

    service.list({ page: 1, pageSize: 20, categoryId: 4, status: 'Active', search: null }).subscribe();
    service.get(7).subscribe();
    service.create(body).subscribe();
    service.update(7, body).subscribe();
    service.delete(7).subscribe();
    service.addAlias(7, 'LEITE INT').subscribe();
    service.confirmAlias(7, 2).subscribe();
    service.deleteAlias(7, 2).subscribe();

    expect(http.expectOne((req) => req.method === 'GET' && req.url === '/api/products').request.params.toString()).toBe(
      'page=1&pageSize=20&categoryId=4&status=Active',
    );
    http.expectOne({ method: 'GET', url: '/api/products/7' });
    expect(http.expectOne({ method: 'POST', url: '/api/products' }).request.body).toEqual(body);
    http.expectOne({ method: 'PUT', url: '/api/products/7' });
    http.expectOne({ method: 'DELETE', url: '/api/products/7' });
    expect(http.expectOne({ method: 'POST', url: '/api/products/7/aliases' }).request.body).toEqual({ description: 'LEITE INT' });
    http.expectOne({ method: 'PUT', url: '/api/products/7/aliases/2/confirm' });
    http.expectOne({ method: 'DELETE', url: '/api/products/7/aliases/2' });
  });
});
