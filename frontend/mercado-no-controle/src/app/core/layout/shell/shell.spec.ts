import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { NAVIGATION_ITEMS } from '../navigation';
import { Shell } from './shell';

describe('Shell', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    });
  });

  afterEach(() => {
    const http = TestBed.inject(HttpTestingController);
    http.match('/api/health');
    http.verify();
  });

  it('renders every menu item from the specification', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();

    const links = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-nav-list a'));

    expect(links.map((link) => link.querySelector('[matListItemTitle]')?.textContent?.trim())).toEqual(NAVIGATION_ITEMS.map((item) => item.label));
    expect(links.map((link) => link.getAttribute('href'))).toEqual(NAVIGATION_ITEMS.map((item) => item.path));
  });

  it('shows the brand and the API status indicator in the header', async () => {
    const fixture = TestBed.createComponent(Shell);
    await fixture.whenStable();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('.shell__brand')?.textContent).toContain('Mercado no Controle');
    expect(element.querySelector('app-api-status')).not.toBeNull();
  });
});
