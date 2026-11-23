import { HttpParams } from '@angular/common/http';

/** Espelha `PagedResult<T>` do backend. */
export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export type SortDirection = 'asc' | 'desc';

/** Parâmetros comuns de listagem (página iniciando em 1). */
export interface PageRequest {
  page: number;
  pageSize: number;
  search?: string | null;
  sortBy?: string | null;
  sortDirection?: SortDirection | null;
}

export const DEFAULT_PAGE_SIZE = 20;
export const PAGE_SIZE_OPTIONS = [10, 20, 50, 100];

/** Converte um objeto em query string, ignorando valores nulos, indefinidos e vazios. */
export function toHttpParams(values: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value === null || value === undefined || value === '') {
      continue;
    }
    params = params.set(key, String(value));
  }
  return params;
}
