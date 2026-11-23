import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, Router, RouterStateSnapshot, UrlTree, provideRouter } from '@angular/router';
import { authGuard, authInterceptor, guestGuard, safeReturnUrl } from '../../core/auth/auth.guards';
import { AuthService, AuthStatus } from '../../core/auth/auth.service';
import { settle } from '../../testing/settle';
import { LoginPage } from './login-page';

const user = { id: 1, name: 'Pessoa', email: 'eu@example.com', lastLoginAt: null };
const signedOut: AuthStatus = { enabled: true, setupRequired: false, authenticated: false, user: null };

function configure() {
  TestBed.configureTestingModule({
    providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
  });
  return { http: TestBed.inject(HttpTestingController), auth: TestBed.inject(AuthService), router: TestBed.inject(Router) };
}

describe('AuthService', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('loads the status once and updates it on login and logout', async () => {
    const { http, auth } = configure();

    const first = auth.ensureLoaded();
    const second = auth.ensureLoaded();
    http.expectOne('/api/auth/status').flush(signedOut);
    expect(await first).toEqual(signedOut);
    expect(await second).toEqual(signedOut);
    expect(auth.user()).toBeNull();

    auth.login({ email: 'eu@example.com', password: 'senha-forte', rememberMe: true }).subscribe();
    const login = http.expectOne('/api/auth/login');
    expect(login.request.body).toEqual({ email: 'eu@example.com', password: 'senha-forte', rememberMe: true });
    login.flush(user);
    expect(auth.status()?.authenticated).toBe(true);
    expect(auth.user()?.name).toBe('Pessoa');

    auth.logout().subscribe();
    http.expectOne({ method: 'POST', url: '/api/auth/logout' }).flush(null);
    expect(auth.status()?.authenticated).toBe(false);
    expect(auth.user()).toBeNull();
  });

  it('sends password and profile changes', () => {
    const { http, auth } = configure();

    auth.changePassword('antiga-123', 'nova-12345').subscribe();
    const password = http.expectOne('/api/auth/password');
    expect(password.request.method).toBe('PUT');
    expect(password.request.body).toEqual({ currentPassword: 'antiga-123', newPassword: 'nova-12345' });
    password.flush(null);

    auth.updateProfile('Novo', 'novo@example.com').subscribe();
    const profile = http.expectOne('/api/auth/profile');
    expect(profile.request.body).toEqual({ name: 'Novo', email: 'novo@example.com' });
    profile.flush({ ...user, name: 'Novo' });
  });
});

describe('auth guards and interceptor', () => {
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  const run = (guard: typeof authGuard, url = '/receipts') =>
    TestBed.runInInjectionContext(() => guard({} as ActivatedRouteSnapshot, { url } as RouterStateSnapshot)) as Promise<boolean | UrlTree>;

  it('redirects to the login with the return URL when there is no session', async () => {
    const { http, router } = configure();

    const result = run(authGuard);
    http.expectOne('/api/auth/status').flush(signedOut);

    expect(router.serializeUrl((await result) as UrlTree)).toBe('/login?returnUrl=%2Freceipts');
  });

  it('lets the application open with a session or with authentication disabled', async () => {
    const { http } = configure();
    const withSession = run(authGuard);
    http.expectOne('/api/auth/status').flush({ ...signedOut, authenticated: true, user });
    expect(await withSession).toBe(true);

    const disabled = TestBed.inject(AuthService).ensureLoaded(true);
    http.expectOne('/api/auth/status').flush({ enabled: false, setupRequired: false, authenticated: true, user: null });
    await disabled;
    expect(await run(authGuard)).toBe(true);
  });

  it('lets the application open when the API is offline (the pages show the connection error)', async () => {
    const { http } = configure();

    const result = run(authGuard);
    http.expectOne('/api/auth/status').error(new ProgressEvent('error'));

    expect(await result).toBe(true);
  });

  it('keeps signed-in users away from the login page', async () => {
    const { http, router } = configure();

    const result = run(guestGuard, '/login');
    http.expectOne('/api/auth/status').flush({ ...signedOut, authenticated: true, user });

    expect(router.serializeUrl((await result) as UrlTree)).toBe('/dashboard');
  });

  it('returns to the login page when the session expires', async () => {
    const { http, router } = configure();
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    vi.spyOn(router, 'url', 'get').mockReturnValue('/products/7');

    TestBed.inject(HttpClient).get('/api/products/7').subscribe({ error: () => undefined });
    http.expectOne('/api/products/7').flush({ title: 'Não autenticado' }, { status: 401, statusText: 'Unauthorized' });

    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/products/7' } });

    navigate.mockClear();
    TestBed.inject(HttpClient).post('/api/auth/login', {}).subscribe({ error: () => undefined });
    http.expectOne('/api/auth/login').flush({ detail: 'E-mail ou senha incorretos.' }, { status: 401, statusText: 'Unauthorized' });
    expect(navigate).not.toHaveBeenCalled();
  });

});

describe('safeReturnUrl', () => {
  it('accepts only internal return URLs', () => {
    expect(safeReturnUrl('/receipts/10')).toBe('/receipts/10');
    expect(safeReturnUrl('https://evil.example')).toBe('/dashboard');
    expect(safeReturnUrl('//evil.example')).toBe('/dashboard');
    expect(safeReturnUrl('/login')).toBe('/dashboard');
    expect(safeReturnUrl(undefined)).toBe('/dashboard');
  });
});

describe('LoginPage', () => {
  let fixture: ComponentFixture<LoginPage>;
  let http: HttpTestingController;
  let navigateByUrl: ReturnType<typeof vi.fn>;

  async function setup(status: AuthStatus, returnUrl?: string) {
    TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    navigateByUrl = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true) as unknown as ReturnType<typeof vi.fn>;
    const loaded = TestBed.inject(AuthService).ensureLoaded();
    http.expectOne('/api/auth/status').flush(status);
    await loaded;
    fixture = TestBed.createComponent(LoginPage);
    if (returnUrl) fixture.componentRef.setInput('returnUrl', returnUrl);
    await settle();
  }

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;

  function type(selector: string, value: string) {
    const input = element().querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  async function submit() {
    element().querySelector<HTMLButtonElement>('button[type="submit"]')!.click();
    await settle();
  }

  it('signs in and returns to the requested page', async () => {
    await setup(signedOut, '/receipts/10');
    expect(element().textContent).toContain('Entrar');

    type('input[formcontrolname="email"]', 'eu@example.com');
    type('input[formcontrolname="password"]', 'senha-forte');
    await submit();
    http.expectOne('/api/auth/login').flush(user);
    await settle();

    expect(navigateByUrl).toHaveBeenCalledWith('/receipts/10');
  });

  it('shows the API message for wrong credentials', async () => {
    await setup(signedOut);
    type('input[formcontrolname="email"]', 'eu@example.com');
    type('input[formcontrolname="password"]', 'errada');
    await submit();
    http.expectOne('/api/auth/login').flush({ detail: 'E-mail ou senha incorretos.' }, { status: 401, statusText: 'Unauthorized' });
    await settle();

    expect(element().querySelector('[role="alert"]')?.textContent).toContain('E-mail ou senha incorretos.');
    expect(navigateByUrl).not.toHaveBeenCalled();
  });

  it('creates the user on first access after validating the password', async () => {
    await setup({ ...signedOut, setupRequired: true });
    expect(element().textContent).toContain('Primeiro acesso');

    type('input[formcontrolname="name"]', 'Pessoa');
    type('input[formcontrolname="email"]', 'eu@example.com');
    type('input[formcontrolname="password"]', 'curta');
    type('input[formcontrolname="confirmPassword"]', 'outra');
    await submit();
    expect(element().textContent).toContain('Mínimo de 8 caracteres.');
    expect(element().textContent).toContain('As senhas não conferem.');

    type('input[formcontrolname="password"]', 'senha-forte-123');
    type('input[formcontrolname="confirmPassword"]', 'senha-forte-123');
    await submit();
    const setupRequest = http.expectOne('/api/auth/setup');
    expect(setupRequest.request.body).toEqual({ name: 'Pessoa', email: 'eu@example.com', password: 'senha-forte-123' });
    setupRequest.flush(user);
    await settle();

    expect(navigateByUrl).toHaveBeenCalledWith('/dashboard');
  });

  it('shows the reason when the first access is refused', async () => {
    await setup({ ...signedOut, setupRequired: true });
    type('input[formcontrolname="name"]', 'Pessoa');
    type('input[formcontrolname="email"]', 'eu@example.com');
    type('input[formcontrolname="password"]', 'senha-forte-123');
    type('input[formcontrolname="confirmPassword"]', 'senha-forte-123');
    await submit();
    http
      .expectOne('/api/auth/setup')
      .flush({ detail: 'O cadastro inicial só pode ser feito no computador onde a aplicação está instalada.' }, { status: 403, statusText: 'Forbidden' });
    await settle();

    expect(element().querySelector('[role="alert"]')?.textContent).toContain('computador onde a aplicação está instalada');
  });
});
