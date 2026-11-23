import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { PageRequest, PagedResult, toHttpParams } from '../../shared/models/paging';

export interface Store {
  id: number;
  tradeName: string;
  legalName: string | null;
  cnpj: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  notes: string | null;
  createdAt: string;
  receiptCount: number;
  totalSpent: number;
  lastPurchaseAt: string | null;
}

export interface StoreRequest {
  tradeName: string;
  legalName: string | null;
  cnpj: string | null;
  address: string | null;
  city: string | null;
  state: string | null;
  notes: string | null;
}

export interface StoreQuery extends PageRequest {
  state?: string | null;
}

export const BRAZILIAN_STATES = [
  'AC', 'AL', 'AP', 'AM', 'BA', 'CE', 'DF', 'ES', 'GO', 'MA', 'MT', 'MS', 'MG', 'PA',
  'PB', 'PR', 'PE', 'PI', 'RJ', 'RN', 'RS', 'RO', 'RR', 'SC', 'SP', 'SE', 'TO',
] as const;

@Injectable({ providedIn: 'root' })
export class StoresService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/stores`;

  list(query: StoreQuery): Observable<PagedResult<Store>> {
    return this.http.get<PagedResult<Store>>(this.url, { params: toHttpParams(query) });
  }

  get(id: number): Observable<Store> {
    return this.http.get<Store>(`${this.url}/${id}`);
  }

  create(request: StoreRequest): Observable<Store> {
    return this.http.post<Store>(this.url, request);
  }

  update(id: number, request: StoreRequest): Observable<Store> {
    return this.http.put<Store>(`${this.url}/${id}`, request);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}`);
  }
}
