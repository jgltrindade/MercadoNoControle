import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { PageRequest, PagedResult, toHttpParams } from '../../shared/models/paging';

export type MeasurementUnit = 'UN' | 'KG' | 'G' | 'L' | 'ML' | 'CX' | 'PCT' | 'DZ';
export type ProductStatus = 'Active' | 'Inactive';
export type AliasSource = 'Manual' | 'Receipt' | 'AiSuggestion';

export const MEASUREMENT_UNITS: readonly { value: MeasurementUnit; label: string }[] = [
  { value: 'UN', label: 'Unidade (UN)' },
  { value: 'KG', label: 'Quilograma (KG)' },
  { value: 'G', label: 'Grama (G)' },
  { value: 'L', label: 'Litro (L)' },
  { value: 'ML', label: 'Mililitro (ML)' },
  { value: 'CX', label: 'Caixa (CX)' },
  { value: 'PCT', label: 'Pacote (PCT)' },
  { value: 'DZ', label: 'Dúzia (DZ)' },
];

export const PRODUCT_STATUS_LABELS: Record<ProductStatus, string> = {
  Active: 'Ativo',
  Inactive: 'Inativo',
};

export const ALIAS_SOURCE_LABELS: Record<AliasSource, string> = {
  Manual: 'Manual',
  Receipt: 'Nota fiscal',
  AiSuggestion: 'Sugestão da IA',
};

export interface ProductListItem {
  id: number;
  name: string;
  brand: string | null;
  categoryId: number | null;
  categoryName: string | null;
  barcode: string | null;
  unit: MeasurementUnit;
  packageContent: number | null;
  status: ProductStatus;
  aliasCount: number;
  createdAt: string;
}

export interface ProductAlias {
  id: number;
  description: string;
  confirmed: boolean;
  source: AliasSource;
  createdAt: string;
}

export interface Product extends Omit<ProductListItem, 'aliasCount'> {
  aliases: ProductAlias[];
}

export interface ProductRequest {
  name: string;
  brand: string | null;
  categoryId: number | null;
  barcode: string | null;
  unit: MeasurementUnit;
  packageContent: number | null;
  status: ProductStatus;
}

/** Produto associado a uma descrição de nota (resultado de /api/products/match). */
export interface ProductMatch {
  productId: number;
  name: string;
  brand: string | null;
  unit: MeasurementUnit;
  packageContent: number | null;
  status: ProductStatus;
  confirmed: boolean;
  source: AliasSource;
}

export interface ProductQuery extends PageRequest {
  categoryId?: number | null;
  status?: ProductStatus | null;
}

/** Ex.: (1, 'L') → "1 L"; (0.5, 'KG') → "0,5 KG". */
export function formatPackage(content: number | null, unit: MeasurementUnit): string {
  if (content === null || content === undefined) {
    return unit;
  }
  return `${content.toLocaleString('pt-BR', { maximumFractionDigits: 4 })} ${unit}`;
}

@Injectable({ providedIn: 'root' })
export class ProductsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/products`;

  list(query: ProductQuery): Observable<PagedResult<ProductListItem>> {
    return this.http.get<PagedResult<ProductListItem>>(this.url, { params: toHttpParams(query) });
  }

  get(id: number): Observable<Product> {
    return this.http.get<Product>(`${this.url}/${id}`);
  }

  create(request: ProductRequest): Observable<Product> {
    return this.http.post<Product>(this.url, request);
  }

  update(id: number, request: ProductRequest): Observable<Product> {
    return this.http.put<Product>(`${this.url}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }

  match(description: string): Observable<ProductMatch[]> {
    return this.http.get<ProductMatch[]>(`${this.url}/match`, { params: { description } });
  }

  addAlias(productId: number, description: string): Observable<ProductAlias> {
    return this.http.post<ProductAlias>(`${this.url}/${productId}/aliases`, { description });
  }

  confirmAlias(productId: number, aliasId: number): Observable<ProductAlias> {
    return this.http.put<ProductAlias>(`${this.url}/${productId}/aliases/${aliasId}/confirm`, null);
  }

  deleteAlias(productId: number, aliasId: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${productId}/aliases/${aliasId}`);
  }
}
