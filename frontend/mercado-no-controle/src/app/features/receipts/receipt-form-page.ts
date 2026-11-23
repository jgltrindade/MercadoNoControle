import { CurrencyPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, input, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import { FormArray, FormControl, FormGroup, NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { Router, RouterLink } from '@angular/router';
import { Observable, map, merge, of, startWith, switchMap, tap } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { fromIsoDate, toIsoDate } from '../../core/i18n/date-adapter';
import { NotificationService } from '../../core/services/notification.service';
import { EmptyState } from '../../shared/components/empty-state/empty-state';
import { SearchPicker } from '../../shared/components/search-picker/search-picker';
import {
  accessKeyFormat,
  applyServerErrors,
  emptyToNull,
  fieldErrorMessage,
  maxDecimalPlaces,
} from '../../shared/forms/form-errors';
import { ProductsService, formatPackage } from '../products/products.service';
import { ProductRef, ReceiptPickers, StoreRef, productDescription, productName, storeDescription, storeName } from './pickers';
import { ImportsService, ReceiptDraft } from './imports.service';
import { ReceiptConfirmationService } from './receipt-confirmation.service';
import { ReceiptImportCard } from './receipt-import-card';
import {
  Receipt,
  ReceiptItem,
  ReceiptRequest,
  ReceiptsService,
  expectedItemTotal,
  roundMoney,
} from './receipts.service';

type ItemForm = FormGroup<{
  id: FormControl<number | null>;
  description: FormControl<string>;
  product: FormControl<ProductRef | null>;
  quantity: FormControl<number | null>;
  unit: FormControl<string>;
  unitPrice: FormControl<number | null>;
  discount: FormControl<number | null>;
  total: FormControl<number | null>;
  /** Mensagem de sugestão de produto (não enviada à API). */
  hint: FormControl<string | null>;
}>;

const MAX_IMAGE_BYTES = 10 * 1024 * 1024;
const IMAGE_TYPES = ['image/jpeg', 'image/png', 'image/webp'];

/** Mapeia os nomes de campos da API para os controles do formulário. */
const FIELD_ALIASES = { storeId: 'store', issuedAt: 'issuedDate', productId: 'product', description: 'description' };

/** Cadastro manual (rota /receipts/new) e edição (/receipts/:id/edit) de compras. */
@Component({
  selector: 'app-receipt-form-page',
  imports: [
    CurrencyPipe,
    ReactiveFormsModule,
    RouterLink,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatDatepickerModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatProgressBarModule,
    MatTooltipModule,
    EmptyState,
    ReceiptImportCard,
    SearchPicker,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './receipt-form-page.html',
  styleUrl: './receipt-form-page.scss',
})
export class ReceiptFormPage implements OnInit {
  private readonly fb = inject(NonNullableFormBuilder);
  private readonly service = inject(ReceiptsService);
  private readonly productsService = inject(ProductsService);
  private readonly confirmation = inject(ReceiptConfirmationService);
  private readonly importsService = inject(ImportsService);
  private readonly notification = inject(NotificationService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly pickers = inject(ReceiptPickers);

  /** Parâmetro de rota `:id` (edição). */
  readonly id = input<string>();

  /** Conferência de importação: identificador e dados lidos pela IA (quando houver). */
  readonly importId = input<string>();
  readonly draft = input<ReceiptDraft | null>(null);
  protected readonly isImport = computed(() => !!this.importId());
  protected readonly draftWarnings = computed(() => this.draft()?.warnings ?? []);
  protected readonly unregisteredStore = computed(() => {
    const draft = this.draft();
    return draft && !draft.storeId && (draft.extractedStoreName || draft.extractedCnpj) ? draft : null;
  });

  protected readonly storeName = storeName;
  protected readonly storeDescription = storeDescription;
  protected readonly productName = productName;
  protected readonly productDescription = productDescription;
  protected readonly errorMessage = fieldErrorMessage;

  /** Id da nota já gravada (edição, ou criada nesta tela antes de uma falha posterior). */
  private readonly savedId = signal<number | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly status = signal<Receipt['status']>('Draft');
  protected readonly hasImage = signal(false);
  protected readonly pendingImage = signal<File | null>(null);
  protected readonly imageError = signal<string | null>(null);
  protected readonly isEdit = computed(() => this.savedId() !== null);

  protected readonly form = this.fb.group({
    store: new FormControl<StoreRef | null>(null, Validators.required),
    issuedDate: new FormControl<Date | null>(null, Validators.required),
    issuedTime: this.fb.control('12:00', Validators.required),
    number: this.fb.control('', Validators.maxLength(20)),
    series: this.fb.control('', Validators.maxLength(10)),
    accessKey: this.fb.control('', accessKeyFormat),
    paymentMethod: this.fb.control('', Validators.maxLength(60)),
    autoTotals: this.fb.control(true),
    productsTotal: this.moneyControl(),
    discountTotal: this.moneyControl(),
    additionalCharges: this.moneyControl(),
    total: this.moneyControl(),
    items: this.fb.array<ItemForm>([]),
  });

  protected readonly items = this.form.controls.items;

  private readonly formValue = toSignal(this.form.valueChanges.pipe(map(() => this.form.getRawValue())), {
    initialValue: this.form.getRawValue(),
  });

  /** Resumo dos valores para conferência (RN10) enquanto o usuário digita. */
  protected readonly summary = computed(() => {
    const value = this.formValue();
    const itemsGross = roundMoney(value.items.reduce((sum, i) => sum + (i.total ?? 0) + (i.discount ?? 0), 0));
    const itemDiscounts = roundMoney(value.items.reduce((sum, i) => sum + (i.discount ?? 0), 0));
    const expectedTotal = roundMoney(
      (value.productsTotal ?? 0) - (value.discountTotal ?? 0) + (value.additionalCharges ?? 0),
    );
    return {
      itemCount: value.items.length,
      itemsGross,
      itemDiscounts,
      productsMismatch: Math.abs(itemsGross - (value.productsTotal ?? 0)) > 0.01,
      expectedTotal,
      totalMismatch: Math.abs(expectedTotal - (value.total ?? 0)) > 0.01,
      unidentified: value.items.filter((i) => !i.product).length,
    };
  });

  constructor() {
    merge(this.items.valueChanges, this.form.controls.additionalCharges.valueChanges, this.form.controls.autoTotals.valueChanges)
      .pipe(startWith(null), takeUntilDestroyed())
      .subscribe(() => this.recalculateTotals());
  }

  ngOnInit(): void {
    const id = Number(this.id());
    if (this.isImport()) {
      this.patchFromDraft(this.draft());
    } else if (id > 0) {
      this.load(id);
    } else {
      this.form.controls.issuedDate.setValue(new Date());
      this.addItem();
    }
  }

  addItem(item?: Partial<ReceiptItem>, hint: string | null = null): void {
    const group: ItemForm = this.fb.group({
      id: new FormControl<number | null>(item?.id ?? null),
      description: this.fb.control(item?.description ?? '', [Validators.required, Validators.maxLength(250)]),
      product: new FormControl<ProductRef | null>(
        item?.productId ? { id: item.productId, name: item.productName ?? `Produto ${item.productId}` } : null,
      ),
      quantity: new FormControl<number | null>(item?.quantity ?? 1, [
        Validators.required,
        Validators.min(0.0001),
        maxDecimalPlaces(4),
      ]),
      unit: this.fb.control(item?.unit ?? 'UN', Validators.maxLength(10)),
      unitPrice: new FormControl<number | null>(item?.unitPrice ?? null, [
        Validators.required,
        Validators.min(0),
        maxDecimalPlaces(4),
      ]),
      discount: new FormControl<number | null>(item?.discount ?? 0, [Validators.min(0), maxDecimalPlaces(2)]),
      total: new FormControl<number | null>(item?.total ?? null, [Validators.required, Validators.min(0), maxDecimalPlaces(2)]),
      hint: new FormControl<string | null>(hint),
    });
    this.watchItem(group);
    this.items.push(group);
  }

  removeItem(index: number): void {
    this.items.removeAt(index);
  }

  /** Ao sair da descrição, sugere o produto já associado a ela (descrição alternativa confirmada). */
  suggestProduct(group: ItemForm): void {
    const { description, product } = group.getRawValue();
    if (product || !description.trim()) {
      return;
    }
    this.productsService.match(description).subscribe({
      next: (matches) => {
        if (group.controls.product.value) {
          return;
        }
        const confirmed = matches.filter((m) => m.confirmed && m.status === 'Active');
        if (confirmed.length === 1) {
          const match = confirmed[0];
          group.controls.product.setValue({
            id: match.productId,
            name: match.name,
            brand: match.brand,
            detail: formatPackage(match.packageContent, match.unit),
          });
          group.controls.hint.setValue('Associado automaticamente pela descrição — confira.');
        } else if (matches.length > 1) {
          group.controls.hint.setValue(`Esta descrição está associada a ${matches.length} produtos — escolha o correto.`);
        }
      },
      error: () => undefined,
    });
  }

  createProductFor(group: ItemForm): void {
    this.pickers.createProduct().subscribe((product) => group.controls.product.setValue(product));
  }

  createStore(): void {
    const fromReceipt = this.unregisteredStore();
    const prefill = fromReceipt ? { tradeName: fromReceipt.extractedStoreName, cnpj: fromReceipt.extractedCnpj } : undefined;
    this.pickers.createStore(prefill).subscribe((store) => this.form.controls.store.setValue(store));
  }

  /** Descarta a importação em conferência, sem registrar compra. */
  discardImport(): void {
    const importId = this.importId();
    if (!importId) return;
    this.saving.set(true);
    this.importsService.discard(importId).subscribe({
      next: () => {
        this.notification.success('Leitura descartada.');
        void this.router.navigate(['/receipts/new']);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.notification.error(toApiError(error).message);
      },
    });
  }

  /** Preenche os totais da nota a partir dos itens. */
  fillTotalsFromItems(): void {
    const { itemsGross, itemDiscounts } = this.summary();
    const charges = this.form.controls.additionalCharges.value ?? 0;
    this.form.patchValue({
      productsTotal: itemsGross,
      discountTotal: itemDiscounts,
      total: roundMoney(itemsGross - itemDiscounts + charges),
    });
  }

  onFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    input.value = '';
    this.imageError.set(null);
    if (!file) {
      return;
    }
    if (!IMAGE_TYPES.includes(file.type)) {
      this.imageError.set('Formato não suportado. Envie JPEG, PNG ou WebP.');
      return;
    }
    if (file.size > MAX_IMAGE_BYTES) {
      this.imageError.set('A imagem excede o tamanho máximo de 10 MB.');
      return;
    }
    this.pendingImage.set(file);
  }

  clearPendingImage(): void {
    this.pendingImage.set(null);
  }

  /** Salva a compra; com `confirm`, em seguida solicita a confirmação (RN01). */
  save(confirm: boolean): void {
    this.formError.set(null);
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.formError.set('Revise os campos destacados.');
      return;
    }
    if (confirm && this.items.length === 0) {
      this.formError.set('Inclua ao menos um item antes de confirmar a compra.');
      return;
    }

    const request = this.buildRequest();
    const importId = this.importId();
    if (importId) {
      this.confirmImport(importId, request);
      return;
    }

    const existingId = this.savedId();
    const save$: Observable<Receipt> = existingId ? this.service.update(existingId, request) : this.service.create(request);

    this.saving.set(true);
    save$
      .pipe(
        tap((receipt) => this.savedId.set(receipt.id)),
        switchMap((receipt) => this.uploadPendingImage(receipt)),
        switchMap((receipt) =>
          confirm && receipt.status === 'Draft'
            ? this.confirmation.confirm(receipt.id).pipe(map((confirmed) => ({ receipt, confirmed, asked: true })))
            : of({ receipt, confirmed: null as Receipt | null, asked: false }),
        ),
      )
      .subscribe({
        next: ({ receipt, confirmed, asked }) => {
          if (confirmed) {
            this.notification.success('Compra confirmada e registrada.');
          } else if (asked) {
            this.notification.success('Compra salva como rascunho; ela ainda não participa dos relatórios.');
          } else {
            this.notification.success(existingId ? 'Compra atualizada.' : 'Rascunho salvo.');
          }
          void this.router.navigate(['/receipts', receipt.id]);
        },
        error: (error: unknown) => this.handleSaveError(error),
      });
  }

  /** Registra a compra a partir da importação; nada é gravado se o usuário recusar os alertas. */
  private confirmImport(importId: string, request: ReceiptRequest): void {
    this.saving.set(true);
    this.confirmation
      .confirmWith((acknowledge) => this.importsService.confirm(importId, request, acknowledge))
      .subscribe({
        next: (receipt) => {
          this.saving.set(false);
          if (receipt) {
            this.notification.success('Compra confirmada e registrada.');
            void this.router.navigate(['/receipts', receipt.id]);
          }
        },
        error: (error: unknown) => this.handleSaveError(error),
      });
  }

  /** Preenche o formulário com o que a IA leu. Campos não lidos ficam vazios para o usuário completar. */
  private patchFromDraft(draft: ReceiptDraft | null): void {
    this.items.clear({ emitEvent: false });
    if (!draft) {
      // Nada foi lido: cadastro manual comum, com totais calculados pelos itens.
      this.form.controls.issuedDate.setValue(new Date());
      this.addItem();
      return;
    }

    // Valores impressos lidos da nota são preservados; o cálculo automático fica desligado.
    this.form.controls.autoTotals.setValue(false);
    for (const item of draft.items) {
      const hint = item.productId
        ? 'Associado pela descrição — confira.'
        : item.matchCount > 1
          ? `Esta descrição está associada a ${item.matchCount} produtos — escolha o correto.`
          : null;
      this.addItem(
        {
          description: item.description,
          productId: item.productId,
          productName: item.productName,
          quantity: item.quantity ?? undefined,
          unit: item.unit,
          unitPrice: item.unitPrice ?? undefined,
          discount: item.discount ?? 0,
          total: item.total ?? undefined,
        },
        hint,
      );
    }
    if (draft.items.length === 0) {
      this.addItem();
    }

    this.form.patchValue({
      store: draft.storeId ? { id: draft.storeId, tradeName: draft.storeName ?? '' } : null,
      issuedDate: draft.issuedAt ? fromIsoDate(draft.issuedAt) : null,
      issuedTime: draft.issuedAt && draft.issuedAt.length >= 16 ? draft.issuedAt.slice(11, 16) : '12:00',
      number: draft.number ?? '',
      series: draft.series ?? '',
      accessKey: draft.accessKey ?? '',
      paymentMethod: draft.paymentMethod ?? '',
      productsTotal: draft.productsTotal,
      discountTotal: draft.discountTotal,
      additionalCharges: draft.additionalCharges ?? 0,
      total: draft.total,
    });
    // Destaca de imediato o que precisa ser preenchido.
    this.form.markAllAsTouched();
  }

  private load(id: number): void {
    this.loading.set(true);
    this.service.get(id).subscribe({
      next: (receipt) => {
        this.loading.set(false);
        this.savedId.set(receipt.id);
        this.patch(receipt);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        const apiError = toApiError(error);
        this.loadError.set(apiError.status === 404 ? 'Compra não encontrada.' : apiError.message);
      },
    });
  }

  private patch(receipt: Receipt): void {
    this.status.set(receipt.status);
    this.hasImage.set(receipt.hasImage);
    this.form.controls.autoTotals.setValue(false);
    this.items.clear({ emitEvent: false });
    receipt.items.forEach((item) => this.addItem(item));
    // Na edição, os totais impressos são preservados; o cálculo automático fica desligado.
    this.form.patchValue({
      store: { id: receipt.storeId, tradeName: receipt.storeName, cnpj: receipt.storeCnpj },
      issuedDate: fromIsoDate(receipt.issuedAt),
      issuedTime: receipt.issuedAt.slice(11, 16),
      number: receipt.number ?? '',
      series: receipt.series ?? '',
      accessKey: receipt.accessKey ?? '',
      paymentMethod: receipt.paymentMethod ?? '',
      autoTotals: false,
      productsTotal: receipt.productsTotal,
      discountTotal: receipt.discountTotal,
      additionalCharges: receipt.additionalCharges,
      total: receipt.total,
    });
  }

  private buildRequest(): ReceiptRequest {
    const value = this.form.getRawValue();
    return {
      storeId: value.store!.id,
      number: emptyToNull(value.number),
      series: emptyToNull(value.series),
      accessKey: emptyToNull(value.accessKey),
      issuedAt: `${toIsoDate(value.issuedDate!)}T${value.issuedTime}:00`,
      productsTotal: value.productsTotal ?? 0,
      discountTotal: value.discountTotal ?? 0,
      additionalCharges: value.additionalCharges ?? 0,
      total: value.total ?? 0,
      paymentMethod: emptyToNull(value.paymentMethod),
      items: value.items.map((item) => ({
        id: item.id,
        description: item.description.trim(),
        productId: item.product?.id ?? null,
        quantity: item.quantity ?? 0,
        unit: emptyToNull(item.unit),
        unitPrice: item.unitPrice ?? 0,
        discount: item.discount ?? 0,
        total: item.total ?? 0,
      })),
    };
  }

  private uploadPendingImage(receipt: Receipt): Observable<Receipt> {
    const file = this.pendingImage();
    if (!file) {
      return of(receipt);
    }
    return this.service.uploadImage(receipt.id, file).pipe(tap(() => this.pendingImage.set(null)));
  }

  private handleSaveError(error: unknown): void {
    this.saving.set(false);
    const apiError = toApiError(error);
    if (apiError.status === 409 && apiError.problem?.['existingReceiptId']) {
      this.form.controls.accessKey.setErrors({ server: apiError.message });
      this.form.controls.accessKey.markAsTouched();
      return;
    }
    if (apiError.fieldErrors['file']) {
      this.imageError.set(apiError.fieldErrors['file'].join(' '));
    }
    const applied = applyServerErrors(this.form, apiError, FIELD_ALIASES);
    if (!applied || apiError.fieldErrors['items']) {
      this.formError.set(
        this.savedId() ? `${apiError.message} A compra foi salva; corrija e salve novamente.` : apiError.message,
      );
    }
  }

  /** Recalcula o total do item quando quantidade, preço ou desconto mudam. */
  private watchItem(group: ItemForm): void {
    const { quantity, unitPrice, discount, product, hint } = group.controls;
    merge(quantity.valueChanges, unitPrice.valueChanges, discount.valueChanges)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        if (quantity.value !== null && unitPrice.value !== null) {
          group.controls.total.setValue(Math.max(0, expectedItemTotal(quantity.value, unitPrice.value, discount.value ?? 0)));
        }
      });
    product.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => hint.setValue(null, { emitEvent: false }));
  }

  private recalculateTotals(): void {
    if (!this.form.controls.autoTotals.value) {
      return;
    }
    const items = this.items.getRawValue();
    const productsTotal = roundMoney(items.reduce((sum, i) => sum + (i.total ?? 0) + (i.discount ?? 0), 0));
    const discountTotal = roundMoney(items.reduce((sum, i) => sum + (i.discount ?? 0), 0));
    const charges = this.form.controls.additionalCharges.value ?? 0;
    this.form.patchValue(
      { productsTotal, discountTotal, total: roundMoney(productsTotal - discountTotal + charges) },
      { emitEvent: false },
    );
    this.form.updateValueAndValidity();
  }

  private moneyControl(): FormControl<number | null> {
    return new FormControl<number | null>(0, [Validators.required, Validators.min(0), maxDecimalPlaces(2)]);
  }
}
