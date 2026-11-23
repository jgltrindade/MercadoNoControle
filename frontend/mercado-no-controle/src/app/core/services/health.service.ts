import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api/api-config';

/** Espelha o DTO `SystemStatusResponse` do backend. */
export interface SystemStatus {
  status: 'Healthy' | 'Degraded';
  databaseAvailable: boolean;
  version: string;
  environment: string;
  serverTimeUtc: string;
}

@Injectable({ providedIn: 'root' })
export class HealthService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getStatus(): Observable<SystemStatus> {
    return this.http.get<SystemStatus>(`${this.baseUrl}/health`);
  }
}
