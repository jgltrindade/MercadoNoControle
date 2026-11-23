import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, filter, map } from 'rxjs';
import { formatCnpj } from '../../shared/pipes/cnpj.pipe';
import { ProductFormDialog } from '../products/product-form-dialog';
import { Product, ProductsService, formatPackage } from '../products/products.service';
import { StoreFormData, StoreFormDialog } from '../supermarkets/store-form-dialog';
import { Store, StoresService } from '../supermarkets/stores.service';

/** Referência mínima a um supermercado em seletores. */
export interface StoreRef {
  id: number;
  tradeName: string;
  city?: string | null;
  cnpj?: string | null;
}

/** Referência mínima a um produto em seletores. */
export interface ProductRef {
  id: number;
  name: string;
  brand?: string | null;
  detail?: string | null;
}

export const storeName = (store: StoreRef): string => store.tradeName;
export const storeDescription = (store: StoreRef): string | null =>
  [store.city, store.cnpj ? formatCnpj(store.cnpj) : null].filter(Boolean).join(' · ') || null;
export const productName = (product: ProductRef): string => product.name;
export const productDescription = (product: ProductRef): string | null =>
  [product.brand, product.detail].filter(Boolean).join(' · ') || null;

/** Funções de busca e cadastro rápido usadas pelos seletores das telas de compras. */
@Injectable({ providedIn: 'root' })
export class ReceiptPickers {
  private readonly stores = inject(StoresService);
  private readonly products = inject(ProductsService);
  private readonly dialog = inject(MatDialog);

  readonly searchStores = (term: string): Observable<StoreRef[]> =>
    this.stores
      .list({ page: 1, pageSize: 10, search: term, sortBy: 'tradeName', sortDirection: 'asc' })
      .pipe(map((result) => result.items.map(toStoreRef)));

  readonly searchProducts = (term: string): Observable<ProductRef[]> =>
    this.products
      .list({ page: 1, pageSize: 10, search: term, status: 'Active', sortBy: 'name', sortDirection: 'asc' })
      .pipe(
        map((result) =>
          result.items.map((p) => ({ id: p.id, name: p.name, brand: p.brand, detail: formatPackage(p.packageContent, p.unit) })),
        ),
      );

  /** Abre o cadastro de supermercado (opcionalmente pré-preenchido) e emite o supermercado criado. */
  createStore(prefill?: { tradeName?: string | null; cnpj?: string | null }): Observable<StoreRef> {
    const data: StoreFormData = prefill
      ? { prefill: { tradeName: prefill.tradeName ?? undefined, cnpj: prefill.cnpj ?? null } }
      : null;
    return this.dialog
      .open(StoreFormDialog, { data, width: '640px' })
      .afterClosed()
      .pipe(filter((store): store is Store => !!store), map(toStoreRef));
  }

  /** Abre o cadastro de produto e emite o produto criado. */
  createProduct(): Observable<ProductRef> {
    return this.dialog
      .open(ProductFormDialog, { data: null, width: '640px' })
      .afterClosed()
      .pipe(
        filter((product): product is Product => !!product),
        map((p) => ({ id: p.id, name: p.name, brand: p.brand, detail: formatPackage(p.packageContent, p.unit) })),
      );
  }
}

export function toStoreRef(store: Store): StoreRef {
  return { id: store.id, tradeName: store.tradeName, city: store.city, cnpj: store.cnpj };
}
