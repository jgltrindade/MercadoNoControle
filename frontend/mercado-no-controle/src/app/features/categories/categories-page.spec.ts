import { settle } from '../../testing/settle';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { of } from 'rxjs';
import { ConfirmService } from '../../shared/components/confirm-dialog/confirm-dialog';
import { CategoriesPage } from './categories-page';
import { Category } from './categories.service';

describe('CategoriesPage', () => {
  let fixture: ComponentFixture<CategoriesPage>;
  let http: HttpTestingController;
  const confirm = vi.fn(() => of(true));

  const categories: Category[] = [
    { id: 1, name: 'Bebidas', description: null, productCount: 0 },
    { id: 2, name: 'Laticínios', description: 'Leites e queijos', productCount: 3 },
  ];

  beforeEach(async () => {
    confirm.mockClear();
    TestBed.configureTestingModule({
      imports: [CategoriesPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), { provide: ConfirmService, useValue: { confirm } }],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CategoriesPage);
    await settle();
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  async function flushList(body: Category[]) {
    http.expectOne('/api/categories').flush(body);
    await settle();
  }

  it('shows a loading indicator and then the table', async () => {
    expect(element().querySelector('mat-progress-bar')).not.toBeNull();

    await flushList(categories);

    const rows = element().querySelectorAll('tr[mat-row]');
    expect(rows.length).toBe(2);
    expect(rows[1].textContent).toContain('Leites e queijos');
    expect(element().querySelector('mat-progress-bar')).toBeNull();
  });

  it('disables deletion of categories that have products', async () => {
    await flushList(categories);

    const deleteButtons = element().querySelectorAll<HTMLButtonElement>('button[aria-label^="Excluir"]');
    expect(deleteButtons[0].disabled).toBe(false);
    expect(deleteButtons[1].disabled).toBe(true);
  });

  it('shows an empty state', async () => {
    await flushList([]);

    expect(element().textContent).toContain('Nenhuma categoria cadastrada');
  });

  it('shows an error state with retry', async () => {
    http.expectOne('/api/categories').flush(null, { status: 0, statusText: 'Unknown Error' });
    await settle();

    expect(element().textContent).toContain('Não foi possível carregar as categorias');
    element().querySelector<HTMLButtonElement>('app-empty-state button')!.click();
    await settle();
    await flushList(categories);
    expect(element().querySelectorAll('tr[mat-row]').length).toBe(2);
  });

  it('deletes after confirmation and reloads', async () => {
    await flushList(categories);

    element().querySelector<HTMLButtonElement>('button[aria-label="Excluir Bebidas"]')!.click();
    await settle();

    expect(confirm).toHaveBeenCalled();
    http.expectOne({ method: 'DELETE', url: '/api/categories/1' }).flush(null, { status: 204, statusText: 'No Content' });
    await settle();
    await flushList([categories[1]]);
    expect(element().querySelectorAll('tr[mat-row]').length).toBe(1);
  });
});
