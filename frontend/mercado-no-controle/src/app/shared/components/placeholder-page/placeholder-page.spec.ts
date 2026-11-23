import { TestBed } from '@angular/core/testing';
import { PlaceholderPage } from './placeholder-page';

describe('PlaceholderPage', () => {
  it('shows the heading, description and planned phase', async () => {
    const fixture = TestBed.createComponent(PlaceholderPage);
    fixture.componentRef.setInput('heading', 'Produtos');
    fixture.componentRef.setInput('description', 'Catálogo de produtos.');
    fixture.componentRef.setInput('phase', 'Fase 2');
    await fixture.whenStable();

    const text = (fixture.nativeElement as HTMLElement).textContent;
    expect(text).toContain('Produtos');
    expect(text).toContain('Catálogo de produtos.');
    expect(text).toContain('Previsto para a Fase 2');
  });
});
