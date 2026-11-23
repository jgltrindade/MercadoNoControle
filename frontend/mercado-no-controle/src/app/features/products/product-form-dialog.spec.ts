import { settle } from '../../testing/settle';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { NotificationService } from '../../core/services/notification.service';
import { ProductFormDialog } from './product-form-dialog';

describe('ProductFormDialog', () => {
  let fixture: ComponentFixture<ProductFormDialog>;
  let http: HttpTestingController;
  const close = vi.fn();

  beforeEach(async () => {
    close.mockClear();
    TestBed.configureTestingModule({
      imports: [ProductFormDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: null },
        { provide: NotificationService, useValue: { success: vi.fn(), error: vi.fn() } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ProductFormDialog);
    await settle();
    http.expectOne('/api/categories').flush([{ id: 4, name: 'Laticínios', description: null, productCount: 0 }]);
    await settle();
  });

  afterEach(() => http.verify());

  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const form = () => (fixture.componentInstance as any).form;
  const element = () => fixture.nativeElement as HTMLElement;

  async function submit() {
    element().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  it('defaults to unit UN and active status', () => {
    expect(form().controls.unit.value).toBe('UN');
    expect(form().controls.status.value).toBe('Active');
  });

  it('validates package content and barcode before calling the API', async () => {
    form().patchValue({ name: 'Leite', packageContent: 1.12345, barcode: '78A' });

    await submit();

    http.expectNone('/api/products');
    expect(form().controls.packageContent.hasError('decimalPlaces')).toBe(true);
    expect(form().controls.barcode.hasError('digits')).toBe(true);
  });

  it('sends a typed request', async () => {
    form().patchValue({ name: ' Leite Integral 1L ', brand: '', categoryId: 4, unit: 'L', packageContent: 1, barcode: '' });

    await submit();

    const request = http.expectOne({ method: 'POST', url: '/api/products' });
    expect(request.request.body).toEqual({
      name: 'Leite Integral 1L',
      brand: null,
      categoryId: 4,
      barcode: null,
      unit: 'L',
      packageContent: 1,
      status: 'Active',
    });
    request.flush({ id: 10 });
    await settle();
    expect(close).toHaveBeenCalledWith({ id: 10 });
  });

  it('shows a duplicate barcode on the barcode field', async () => {
    form().patchValue({ name: 'Leite', barcode: '4006381333931' });

    await submit();
    http
      .expectOne('/api/products')
      .flush({ title: 'Conflito', detail: 'O código de barras já pertence ao produto "X".' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(form().controls.barcode.errors?.server).toContain('já pertence');
  });

  it('shows general errors that are not tied to a field', async () => {
    form().patchValue({ name: 'Leite', categoryId: 4 });

    await submit();
    http.expectOne('/api/products').flush(null, { status: 0, statusText: 'Unknown Error' });
    await settle();

    expect(element().querySelector('.form-error')?.textContent).toContain('Não foi possível conectar');
  });
});
