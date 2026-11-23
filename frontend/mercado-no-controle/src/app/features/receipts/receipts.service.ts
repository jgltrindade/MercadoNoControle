import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { PageRequest, PagedResult, toHttpParams } from '../../shared/models/paging';

export type ReceiptStatus = 'Draft' | 'Confirmed';

export const RECEIPT_STATUS_LABELS: Record<ReceiptStatus, string> = {
  Draft: 'Rascunho',
  Confirmed: 'Confirmada',
};

export interface ReceiptListItem {
  id: number;
  storeId: number;
  storeName: string;
  number: string | null;
  series: string | null;
  issuedAt: string;
  total: number;
  itemCount: number;
  unidentifiedItemCount: number;
  status: ReceiptStatus;
  hasImage: boolean;
  createdAt: string;
}

export interface ReceiptItem {
  id: number;
  lineNumber: number;
  description: string;
  productId: number | null;
  productName: string | null;
  quantity: number;
  unit: string | null;
  unitPrice: number;
  discount: number;
  total: number;
}

export interface ReceiptWarning {
  code: string;
  message: string;
  lineNumber?: number | null;
}

export interface DuplicateCandidate {
  id: number;
  storeName: string;
  number: string | null;
  series: string | null;
  issuedAt: string;
  total: number;
  reason: string;
}

export interface Receipt {
  id: number;
  storeId: number;
  storeName: string;
  storeCnpj: string | null;
  number: string | null;
  series: string | null;
  accessKey: string | null;
  issuedAt: string;
  productsTotal: number;
  discountTotal: number;
  additionalCharges: number;
  total: number;
  paymentMethod: string | null;
  status: ReceiptStatus;
  hasImage: boolean;
  createdAt: string;
  updatedAt: string | null;
  items: ReceiptItem[];
  warnings: ReceiptWarning[];
  possibleDuplicates: DuplicateCandidate[];
}

export interface ReceiptItemRequest {
  id: number | null;
  description: string;
  productId: number | null;
  quantity: number;
  unit: string | null;
  unitPrice: number;
  discount: number;
  total: number;
}

export interface ReceiptRequest {
  storeId: number;
  number: string | null;
  series: string | null;
  accessKey: string | null;
  /** Data e hora locais da nota, sem fuso: "aaaa-MM-ddTHH:mm:ss". */
  issuedAt: string;
  productsTotal: number;
  discountTotal: number;
  additionalCharges: number;
  total: number;
  paymentMethod: string | null;
  items: ReceiptItemRequest[];
}

export interface ReceiptQuery extends PageRequest {
  storeId?: number | null;
  status?: ReceiptStatus | null;
  /** "aaaa-MM-dd", inclusive. */
  from?: string | null;
  to?: string | null;
}

/** Arredonda para centavos (mesma regra do backend: meio para longe do zero). */
export function roundMoney(value: number): number {
  return Math.sign(value) * Math.round((Math.abs(value) + Number.EPSILON) * 100) / 100;
}

/** Total esperado de um item: arredondar(quantidade × preço, 2) − desconto. */
export function expectedItemTotal(quantity: number, unitPrice: number, discount: number): number {
  return roundMoney(roundMoney((quantity || 0) * (unitPrice || 0)) - (discount || 0));
}

@Injectable({ providedIn: 'root' })
export class ReceiptsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/receipts`;

  list(query: ReceiptQuery): Observable<PagedResult<ReceiptListItem>> {
    return this.http.get<PagedResult<ReceiptListItem>>(this.url, { params: toHttpParams(query) });
  }

  get(id: number): Observable<Receipt> {
    return this.http.get<Receipt>(`${this.url}/${id}`);
  }

  create(request: ReceiptRequest): Observable<Receipt> {
    return this.http.post<Receipt>(this.url, request);
  }

  update(id: number, request: ReceiptRequest): Observable<Receipt> {
    return this.http.put<Receipt>(`${this.url}/${id}`, request);
  }

  confirm(id: number, acknowledgeWarnings: boolean): Observable<Receipt> {
    return this.http.put<Receipt>(`${this.url}/${id}/confirm`, { acknowledgeWarnings });
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }

  uploadImage(id: number, file: File): Observable<Receipt> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<Receipt>(`${this.url}/${id}/image`, form);
  }

  removeImage(id: number): Observable<Receipt> {
    return this.http.delete<Receipt>(`${this.url}/${id}/image`);
  }

  /** URL da imagem; `version` evita cache após substituição. */
  imageUrl(id: number, version?: string | null): string {
    return `${this.url}/${id}/image${version ? `?v=${encodeURIComponent(version)}` : ''}`;
  }
}
