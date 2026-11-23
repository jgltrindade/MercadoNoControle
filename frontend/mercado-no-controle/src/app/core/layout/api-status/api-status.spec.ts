import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ApiStatus } from './api-status';

describe('ApiStatus', () => {
  let fixture: ComponentFixture<ApiStatus>;
  let http: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ApiStatus],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(ApiStatus);
    await fixture.whenStable();
  });

  afterEach(() => http.verify());

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  async function respond(body: object | null, options?: { status: number; statusText: string }) {
    const request = http.expectOne('/api/health');
    if (options) {
      request.flush(body, options);
    } else {
      request.flush(body);
    }
    await fixture.whenStable();
  }

  it('shows a loading state while checking', () => {
    expect(fixture.componentInstance.state()).toBe('checking');
    expect(text()).toContain('Verificando API');
    http.expectOne('/api/health').flush({ databaseAvailable: true });
  });

  it('shows online when the API and database are available', async () => {
    await respond({ status: 'Healthy', databaseAvailable: true, version: '1.0.0', environment: 'Development' });

    expect(fixture.componentInstance.state()).toBe('online');
    expect(text()).toContain('API online');
  });

  it('shows offline when the request fails', async () => {
    await respond(null, { status: 0, statusText: 'Unknown Error' });

    expect(fixture.componentInstance.state()).toBe('offline');
    expect(text()).toContain('API offline');
  });

  it('shows degraded when the database is unavailable', async () => {
    await respond(
      { status: 'Degraded', databaseAvailable: false, version: '1.0.0', environment: 'Development' },
      { status: 503, statusText: 'Service Unavailable' },
    );

    expect(fixture.componentInstance.state()).toBe('degraded');
    expect(text()).toContain('Banco indisponível');
  });

  it('checks again when clicked', async () => {
    await respond(null, { status: 0, statusText: 'Unknown Error' });

    (fixture.nativeElement as HTMLElement).querySelector<HTMLButtonElement>('[data-testid="api-status"]')!.click();
    await fixture.whenStable();
    await respond({ status: 'Healthy', databaseAvailable: true, version: '1.0.0', environment: 'Development' });

    expect(fixture.componentInstance.state()).toBe('online');
  });
});
