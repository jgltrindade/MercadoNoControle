import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { Receipt, ReceiptRequest, ReceiptWarning } from './receipts.service';

export type ReceiptImportStatus = 'Pending' | 'Processing' | 'AwaitingReview' | 'Failed' | 'Confirmed' | 'Discarded';

export const IMPORT_STATUS_LABELS: Record<ReceiptImportStatus, string> = {
  Pending: 'Na fila',
  Processing: 'Lendo a nota…',
  AwaitingReview: 'Aguardando conferência',
  Failed: 'Falha na leitura',
  Confirmed: 'Compra registrada',
  Discarded: 'Descartada',
};

export interface ReaderStatus {
  available: boolean;
  provider: string;
  model: string | null;
  message: string | null;
}

export interface DraftItem {
  lineNumber: number;
  description: string;
  quantity: number | null;
  unit: string | null;
  unitPrice: number | null;
  discount: number | null;
  total: number | null;
  productId: number | null;
  productName: string | null;
  matchCount: number;
}

/** Dados lidos pela IA, validados pelo backend, para conferência. */
export interface ReceiptDraft {
  storeId: number | null;
  storeName: string | null;
  extractedStoreName: string | null;
  extractedCnpj: string | null;
  number: string | null;
  series: string | null;
  accessKey: string | null;
  issuedAt: string | null;
  productsTotal: number | null;
  discountTotal: number | null;
  additionalCharges: number | null;
  total: number | null;
  paymentMethod: string | null;
  items: DraftItem[];
  warnings: ReceiptWarning[];
}

export interface ReceiptImport {
  id: string;
  status: ReceiptImportStatus;
  originalFileName: string | null;
  fileSize: number;
  provider: string | null;
  model: string | null;
  error: string | null;
  attemptCount: number;
  createdAt: string;
  processedAt: string | null;
  receiptId: number | null;
  draft: ReceiptDraft | null;
}

/** Leitura em andamento: o status ainda vai mudar. */
export function isImportInProgress(status: ReceiptImportStatus): boolean {
  return status === 'Pending' || status === 'Processing';
}

@Injectable({ providedIn: 'root' })
export class ImportsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/receipts`;

  readerStatus(): Observable<ReaderStatus> {
    return this.http.get<ReaderStatus>(`${this.url}/imports/reader`);
  }

  upload(file: File): Observable<ReceiptImport> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<ReceiptImport>(`${this.url}/upload`, form);
  }

  listOpen(): Observable<ReceiptImport[]> {
    return this.http.get<ReceiptImport[]>(`${this.url}/imports`);
  }

  get(id: string): Observable<ReceiptImport> {
    return this.http.get<ReceiptImport>(`${this.url}/imports/${id}`);
  }

  reprocess(id: string): Observable<ReceiptImport> {
    return this.http.post<ReceiptImport>(`${this.url}/imports/${id}/reprocess`, null);
  }

  discard(id: string): Observable<ReceiptImport> {
    return this.http.put<ReceiptImport>(`${this.url}/imports/${id}/discard`, null);
  }

  confirm(id: string, request: ReceiptRequest, acknowledgeWarnings: boolean): Observable<Receipt> {
    return this.http.put<Receipt>(`${this.url}/imports/${id}/confirm`, { ...request, acknowledgeWarnings });
  }

  imageUrl(id: string): string {
    return `${this.url}/imports/${id}/image`;
  }
}
