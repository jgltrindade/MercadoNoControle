import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { of } from 'rxjs';
import { settle } from '../../../testing/settle';
import { SearchPicker } from './search-picker';

interface Item {
  id: number;
  name: string;
}

@Component({
  imports: [ReactiveFormsModule, SearchPicker],
  template: `
    <app-search-picker
      [formControl]="control"
      label="Supermercado"
      [search]="search"
      [display]="display"
      createLabel="Cadastrar novo"
      (create)="created.set($event)"
    />
  `,
})
class Host {
  readonly control = new FormControl<Item | null>(null, Validators.required);
  readonly created = signal<string | null>(null);
  readonly search = vi.fn((term: string) =>
    of([
      { id: 1, name: 'Mercado A' },
      { id: 2, name: 'Mercado B' },
    ].filter((i) => i.name.toLowerCase().includes(term.toLowerCase()))),
  );
  readonly display = (item: Item) => item.name;
}

describe('SearchPicker', () => {
  let fixture: ComponentFixture<Host>;

  beforeEach(async () => {
    vi.useFakeTimers();
    fixture = TestBed.createComponent(Host);
    vi.useRealTimers();
    await settle();
  });

  const input = () => (fixture.nativeElement as HTMLElement).querySelector('input')!;
  const picker = () => fixture.debugElement.children[0].componentInstance as SearchPicker<Item>;

  async function type(text: string) {
    input().value = text;
    input().dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    await settle();
  }

  it('shows the selected value written by the form', async () => {
    fixture.componentInstance.control.setValue({ id: 2, name: 'Mercado B' });
    await settle();

    expect(input().value).toBe('Mercado B');
  });

  it('searches as the user types (debounced)', async () => {
    await type('b');

    expect(fixture.componentInstance.search).toHaveBeenCalledWith('b');
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    expect((picker() as any).options()).toEqual([{ id: 2, name: 'Mercado B' }]);
  });

  it('updates the form value when an option is selected and clears it when the user types again', async () => {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    (picker() as any).onSelected({ id: 1, name: 'Mercado A' });
    expect(fixture.componentInstance.control.value).toEqual({ id: 1, name: 'Mercado A' });

    await type('Merc');

    expect(fixture.componentInstance.control.value).toBeNull();
  });

  it('emits create with the typed text', async () => {
    await type('Feira do bairro');

    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const component = picker() as any;
    component.onSelected(component.createToken);

    expect(fixture.componentInstance.created()).toBe('Feira do bairro');
    expect(fixture.componentInstance.control.value).toBeNull();
  });

  it('follows the disabled state of the form control', async () => {
    fixture.componentInstance.control.disable();
    await settle();

    expect(input().disabled).toBe(true);
  });
});
