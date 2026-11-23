import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { toHttpParams } from '../../shared/models/paging';
import { DownloadedFile, toDownloadedFile } from '../../shared/utils/download';

export type { DownloadedFile };

/** Filtros comuns do dashboard e dos relatórios ("aaaa-MM-dd", inclusive). */
export interface AnalyticsQuery {
  from?: string | null;
  to?: string | null;
  storeId?: number | null;
  categoryId?: number | null;
}

export interface DashboardSummary {
  from: string;
  to: string;
  totalSpent: number;
  purchaseCount: number;
  averageTicket: number | null;
  largestPurchase: { receiptId: number; total: number; date: string; storeName: string } | null;
  topStore: { storeId: number; storeName: string; total: number } | null;
  currentMonthTotal: number;
  currentYearTotal: number;
}

export interface MonthlyTotal {
  year: number;
  month: number;
  total: number;
  purchaseCount: number;
}

export interface StoreTotal {
  storeId: number;
  storeName: string;
  total: number;
  purchaseCount: number;
  sharePercent: number;
}

export interface CategoryTotal {
  categoryId: number | null;
  categoryName: string;
  total: number;
  sharePercent: number;
}

export interface ProductTotal {
  productId: number;
  productName: string;
  categoryName: string | null;
  total: number;
  purchaseCount: number;
  sharePercent: number;
}

export type ReportKey = 'monthly' | 'stores' | 'categories' | 'products' | 'history' | 'comparison';
export type ReportColumnType = 'Text' | 'Integer' | 'Number' | 'Money' | 'Percent' | 'Date';
export type ExportFormat = 'csv' | 'xlsx' | 'pdf';

export interface ReportColumn {
  key: string;
  header: string;
  type: ReportColumnType;
}

export interface ReportRow {
  cells: (string | number | null)[];
  isTotal: boolean;
}

export interface ReportTable {
  key: ReportKey;
  title: string;
  filters: string;
  notes: string | null;
  generatedAt: string;
  columns: ReportColumn[];
  rows: ReportRow[];
}

export interface ReportQuery extends AnalyticsQuery {
  ranking?: 'purchases' | 'spent' | 'increase' | null;
}

@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  summary(query: AnalyticsQuery): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.baseUrl}/dashboard`, { params: toHttpParams(query) });
  }

  monthly(query: AnalyticsQuery): Observable<MonthlyTotal[]> {
    return this.http.get<MonthlyTotal[]>(`${this.baseUrl}/dashboard/monthly`, { params: toHttpParams(query) });
  }

  stores(query: AnalyticsQuery): Observable<StoreTotal[]> {
    return this.http.get<StoreTotal[]>(`${this.baseUrl}/dashboard/stores`, { params: toHttpParams(query) });
  }

  categories(query: AnalyticsQuery): Observable<CategoryTotal[]> {
    return this.http.get<CategoryTotal[]>(`${this.baseUrl}/dashboard/categories`, { params: toHttpParams(query) });
  }

  products(query: AnalyticsQuery, top = 10): Observable<ProductTotal[]> {
    return this.http.get<ProductTotal[]>(`${this.baseUrl}/dashboard/products`, { params: toHttpParams({ ...query, top }) });
  }

  report(key: ReportKey, query: ReportQuery): Observable<ReportTable> {
    return this.http.get<ReportTable>(`${this.baseUrl}/reports/${key}`, { params: toHttpParams(query) });
  }

  /** Baixa o relatório no formato pedido, com o nome de arquivo definido pela API. */
  export(key: ReportKey, format: ExportFormat, query: ReportQuery): Observable<DownloadedFile> {
    return this.http
      .get(`${this.baseUrl}/reports/export`, {
        params: toHttpParams({ ...query, report: key, format }),
        responseType: 'blob',
        observe: 'response',
      })
      .pipe(map((response) => toDownloadedFile(response, `relatorio-${key}.${format}`)));
  }
}
