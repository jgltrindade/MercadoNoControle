import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { toHttpParams } from '../../shared/models/paging';
import { MeasurementUnit } from '../products/products.service';

export type NormalizedUnit = 'Kilogram' | 'Liter' | 'Unit';

export const NORMALIZED_UNIT_LABELS: Record<NormalizedUnit, string> = {
  Kilogram: 'kg',
  Liter: 'L',
  Unit: 'un',
};

export interface PriceHistoryQuery {
  /** "aaaa-MM-dd", inclusive. */
  from?: string | null;
  to?: string | null;
  storeId?: number | null;
}

export interface PricePoint {
  observationId: number;
  receiptId: number;
  date: string;
  storeId: number;
  storeName: string;
  quantity: number;
  unit: string | null;
  unitPrice: number;
  totalPrice: number;
  effectiveUnitPrice: number;
  normalizedPrice: number | null;
  normalizedUnit: NormalizedUnit | null;
}

export interface PriceReference {
  price: number;
  date: string;
  storeName: string;
}

export interface PriceSummary {
  count: number;
  last: PriceReference | null;
  lowest: PriceReference | null;
  highest: PriceReference | null;
  average: number | null;
  variationPercent: number | null;
  normalizedUnit: NormalizedUnit | null;
  lastNormalized: number | null;
  lowestNormalized: number | null;
  averageNormalized: number | null;
}

export interface ProductPriceHistory {
  productId: number;
  productName: string;
  brand: string | null;
  unit: MeasurementUnit;
  packageContent: number | null;
  normalizedUnit: NormalizedUnit | null;
  summary: PriceSummary;
  points: PricePoint[];
}

export interface StorePriceComparison {
  storeId: number;
  storeName: string;
  count: number;
  last: PriceReference;
  lowest: number;
  average: number;
  lastNormalized: number | null;
  differenceToCheapestPercent: number;
}

export interface AlternativeProductPrice {
  productId: number;
  name: string;
  brand: string | null;
  lastNormalized: number;
  normalizedUnit: NormalizedUnit;
  date: string;
  storeName: string;
}

export interface ProductPriceComparison {
  productId: number;
  productName: string;
  normalizedUnit: NormalizedUnit | null;
  stores: StorePriceComparison[];
  alternatives: AlternativeProductPrice[];
}

export interface UnassignedItemGroup {
  normalizedDescription: string;
  description: string;
  occurrences: number;
  lastSeenAt: string;
  totalSpent: number;
  stores: string[];
  suggestedProductId: number | null;
  suggestedProductName: string | null;
}

export interface AssignItemsResult {
  updatedItems: number;
  productId: number;
  productName: string;
}

@Injectable({ providedIn: 'root' })
export class PriceHistoryService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getPrices(productId: number, query: PriceHistoryQuery = {}): Observable<ProductPriceHistory> {
    return this.http.get<ProductPriceHistory>(`${this.baseUrl}/products/${productId}/prices`, {
      params: toHttpParams(query),
    });
  }

  getComparison(productId: number, query: PriceHistoryQuery = {}): Observable<ProductPriceComparison> {
    return this.http.get<ProductPriceComparison>(`${this.baseUrl}/products/${productId}/comparison`, {
      params: toHttpParams(query),
    });
  }

  unassignedItems(): Observable<UnassignedItemGroup[]> {
    return this.http.get<UnassignedItemGroup[]>(`${this.baseUrl}/price-history/unassigned-items`);
  }

  assign(description: string, productId: number): Observable<AssignItemsResult> {
    return this.http.post<AssignItemsResult>(`${this.baseUrl}/price-history/assign`, { description, productId });
  }
}
