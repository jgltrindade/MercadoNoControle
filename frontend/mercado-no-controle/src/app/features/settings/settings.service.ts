import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, map } from 'rxjs';
import { API_BASE_URL } from '../../core/api/api-config';
import { DownloadedFile, toDownloadedFile } from '../../shared/utils/download';

export interface ReaderSettings {
  available: boolean;
  provider: string;
  model: string | null;
  effort: string | null;
  timeoutSeconds: number;
  message: string | null;
}

export interface DataCounts {
  stores: number;
  categories: number;
  products: number;
  confirmedReceipts: number;
  draftReceipts: number;
  priceObservations: number;
  openImports: number;
}

export interface SystemInfo {
  version: string;
  environment: string;
  authenticationEnabled: boolean;
  databaseFile: string;
  databaseSizeBytes: number;
  uploadsDirectory: string;
  counts: DataCounts;
  reader: ReaderSettings;
}

export interface ImageFile {
  path: string;
  sizeBytes: number;
  modifiedAt: string;
}

export interface ImagesSummary {
  totalFiles: number;
  totalBytes: number;
  referencedFiles: number;
  missingFiles: number;
  orphanBytes: number;
  orphans: ImageFile[];
}

export interface DeleteOrphansResult {
  deletedFiles: number;
  freedBytes: number;
}

@Injectable({ providedIn: 'root' })
export class SettingsService {
  private readonly http = inject(HttpClient);
  private readonly url = `${inject(API_BASE_URL)}/settings`;

  system(): Observable<SystemInfo> {
    return this.http.get<SystemInfo>(`${this.url}/system`);
  }

  images(): Observable<ImagesSummary> {
    return this.http.get<ImagesSummary>(`${this.url}/images`);
  }

  deleteOrphans(): Observable<DeleteOrphansResult> {
    return this.http.delete<DeleteOrphansResult>(`${this.url}/images/orphans`);
  }

  /** Backup completo (banco + imagens). */
  backup(): Observable<DownloadedFile> {
    return this.download('backup', 'mercado-no-controle-backup.zip');
  }

  /** Dados em CSV. */
  exportData(): Observable<DownloadedFile> {
    return this.download('export', 'mercado-no-controle-dados.zip');
  }

  private download(path: string, fallback: string): Observable<DownloadedFile> {
    return this.http
      .get(`${this.url}/${path}`, { responseType: 'blob', observe: 'response' })
      .pipe(map((response) => toDownloadedFile(response, fallback)));
  }
}

const sizeFormat = new Intl.NumberFormat('pt-BR', { maximumFractionDigits: 1 });

/** Tamanho de arquivo legível (B, KB, MB, GB). */
export function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit++;
  }
  return `${sizeFormat.format(value)} ${units[unit]}`;
}
