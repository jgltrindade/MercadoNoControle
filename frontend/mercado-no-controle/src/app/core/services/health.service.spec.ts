import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { API_BASE_URL } from '../api/api-config';
import { HealthService, SystemStatus } from './health.service';

describe('HealthService', () => {
  let http: HttpTestingController;

  function setup(baseUrl?: string): HealthService {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        ...(baseUrl ? [{ provide: API_BASE_URL, useValue: baseUrl }] : []),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(HealthService);
  }

  afterEach(() => http.verify());

  it('requests GET /api/health and returns the status', () => {
    const service = setup();
    const expected: SystemStatus = {
      status: 'Healthy',
      databaseAvailable: true,
      version: '1.0.0',
      environment: 'Development',
      serverTimeUtc: '2026-11-23T12:00:00Z',
    };
    let result: SystemStatus | undefined;

    service.getStatus().subscribe((status) => (result = status));

    const request = http.expectOne('/api/health');
    expect(request.request.method).toBe('GET');
    request.flush(expected);
    expect(result).toEqual(expected);
  });

  it('uses the configured API base URL', () => {
    const service = setup('http://api.local/api');

    service.getStatus().subscribe();

    http.expectOne('http://api.local/api/health').flush({});
  });
});
