import { settle } from '../../testing/settle';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NotificationService } from '../../core/services/notification.service';
import { StoreFormDialog } from './store-form-dialog';
import { Store } from './stores.service';

describe('StoreFormDialog', () => {
  let fixture: ComponentFixture<StoreFormDialog>;
  let http: HttpTestingController;
  const close = vi.fn();
  const success = vi.fn();

  function setup(data: Store | null = null) {
    close.mockClear();
    success.mockClear();
    TestBed.configureTestingModule({
      imports: [StoreFormDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: NotificationService, useValue: { success, error: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(StoreFormDialog);
    return settle();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const form = () => (fixture.componentInstance as any).form;

  async function submit() {
    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  it('does not submit when required fields are missing', async () => {
    await setup();

    await submit();

    http.expectNone('/api/stores');
    expect(element().textContent).toContain('Campo obrigatório.');
  });

  it('rejects a CNPJ with the wrong length before calling the API', async () => {
    await setup();
    form().patchValue({ tradeName: 'Loja', cnpj: '123' });

    await submit();

    http.expectNone('/api/stores');
    expect(element().textContent).toContain('O CNPJ deve ter 14 caracteres');
  });

  it('creates a store, converting blanks to null', async () => {
    await setup();
    form().patchValue({ tradeName: ' Loja ', cnpj: '11.222.333/0001-81', city: '', state: 'SP' });

    await submit();

    const request = http.expectOne({ method: 'POST', url: '/api/stores' });
    expect(request.request.body).toEqual({
      tradeName: 'Loja',
      legalName: null,
      cnpj: '11.222.333/0001-81',
      address: null,
      city: null,
      state: 'SP',
      notes: null,
    });
    request.flush({ id: 1, tradeName: 'Loja' });
    await settle();
    expect(success).toHaveBeenCalledWith('Supermercado cadastrado.');
    expect(close).toHaveBeenCalledWith({ id: 1, tradeName: 'Loja' });
  });

  it('shows backend validation errors on the fields', async () => {
    await setup();
    form().patchValue({ tradeName: 'Loja', cnpj: '11222333000182' });

    await submit();
    http
      .expectOne('/api/stores')
      .flush({ title: 'Erro', errors: { cnpj: ['CNPJ inválido.'] } }, { status: 400, statusText: 'Bad Request' });
    await settle();

    expect(close).not.toHaveBeenCalled();
    expect(element().textContent).toContain('CNPJ inválido.');
  });

  it('shows a duplicate CNPJ conflict on the CNPJ field', async () => {
    await setup();
    form().patchValue({ tradeName: 'Loja', cnpj: '11222333000181' });

    await submit();
    http
      .expectOne('/api/stores')
      .flush({ title: 'Conflito', detail: 'O CNPJ informado já pertence ao supermercado "A".' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(form().controls.cnpj.errors).toEqual({ server: 'O CNPJ informado já pertence ao supermercado "A".' });
  });

  it('edits an existing store with the formatted CNPJ', async () => {
    await setup({
      id: 9,
      tradeName: 'Loja',
      legalName: null,
      cnpj: '11222333000181',
      address: null,
      city: 'Campinas',
      state: 'SP',
      notes: null,
      createdAt: '',
      receiptCount: 0,
      totalSpent: 0,
      lastPurchaseAt: null,
    });

    expect(form().controls.cnpj.value).toBe('11.222.333/0001-81');
    await submit();
    http.expectOne({ method: 'PUT', url: '/api/stores/9' }).flush({ id: 9 });
    await settle();
    expect(success).toHaveBeenCalledWith('Supermercado atualizado.');
  });
});
