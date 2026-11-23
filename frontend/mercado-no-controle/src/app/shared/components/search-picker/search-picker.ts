import { ChangeDetectionStrategy, Component, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ControlValueAccessor, FormControl, NgControl, ReactiveFormsModule } from '@angular/forms';
import { MatAutocompleteModule } from '@angular/material/autocomplete';
import { MatButtonModule } from '@angular/material/button';
import { ErrorStateMatcher } from '@angular/material/core';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Observable, Subject, catchError, debounceTime, of, switchMap } from 'rxjs';
import { fieldErrorMessage } from '../../forms/form-errors';

/** Item selecionável: precisa apenas de um `id`. */
export interface PickerItem {
  id: number;
}

/**
 * Campo de seleção com busca no servidor (autocomplete), usado como controle de formulário.
 * O valor é o item selecionado (ou `null`); digitar após selecionar limpa a seleção.
 * Opcionalmente oferece a opção "cadastrar novo", emitida em `create` com o texto digitado.
 */
@Component({
  selector: 'app-search-picker',
  imports: [
    ReactiveFormsModule,
    MatAutocompleteModule,
    MatButtonModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <mat-form-field class="search-picker" [subscriptSizing]="subscriptSizing()">
      <mat-label>{{ label() }}</mat-label>
      <input
        matInput
        [formControl]="text"
        [matAutocomplete]="auto"
        [placeholder]="placeholder()"
        [required]="required()"
        [errorStateMatcher]="errorMatcher"
        (focus)="onFocus()"
        (blur)="markTouched()"
      />
      @if (value() && !disabled()) {
        <button matIconSuffix mat-icon-button type="button" aria-label="Limpar seleção" (click)="clear()">
          <mat-icon>close</mat-icon>
        </button>
      }
      <mat-autocomplete #auto="matAutocomplete" [displayWith]="displayFn" (optionSelected)="onSelected($event.option.value)">
        @for (option of options(); track option.id) {
          <mat-option [value]="option">
            <span>{{ display()(option) }}</span>
            @if (describe()(option); as description) {
              <small class="search-picker__description"> · {{ description }}</small>
            }
          </mat-option>
        }
        @if (createLabel()) {
          <mat-option [value]="createToken" class="search-picker__create">
            <mat-icon>add</mat-icon>
            {{ createLabel() }}
          </mat-option>
        }
      </mat-autocomplete>
      @if (hint()) {
        <mat-hint>{{ hint() }}</mat-hint>
      }
      <mat-error>{{ errorText() }}</mat-error>
    </mat-form-field>
  `,
  styles: `
    :host { display: block; }
    .search-picker { width: 100%; }
    .search-picker__description { color: var(--mat-sys-on-surface-variant); }
  `,
})
export class SearchPicker<T extends PickerItem> implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly search = input.required<(term: string) => Observable<T[]>>();
  readonly display = input.required<(item: T) => string>();
  readonly describe = input<(item: T) => string | null>(() => null);
  readonly placeholder = input('');
  readonly hint = input<string | null>(null);
  readonly required = input(false);
  readonly createLabel = input<string | null>(null);
  readonly subscriptSizing = input<'fixed' | 'dynamic'>('fixed');

  /** Usuário escolheu "cadastrar novo"; emite o texto digitado. */
  readonly create = output<string>();

  private readonly ngControl = inject(NgControl, { self: true, optional: true });
  private readonly terms = new Subject<string>();
  private onChange: (value: T | null) => void = () => {};
  private onTouched: () => void = () => {};

  protected readonly createToken = { id: -1 } as T;
  protected readonly text = new FormControl<string | T>('', { nonNullable: true });
  protected readonly options = signal<T[]>([]);
  readonly value = signal<T | null>(null);
  protected readonly disabled = signal(false);

  protected readonly errorMatcher: ErrorStateMatcher = {
    isErrorState: () => !!this.ngControl?.invalid && !!this.ngControl.touched,
  };

  protected readonly displayFn = (item: T | string | null): string =>
    typeof item === 'string' ? item : item && item !== this.createToken ? this.display()(item) : '';

  constructor() {
    if (this.ngControl) {
      this.ngControl.valueAccessor = this;
    }

    this.text.valueChanges.pipe(takeUntilDestroyed()).subscribe((value) => {
      if (typeof value !== 'string') {
        return;
      }
      const selected = this.value();
      if (selected && value !== this.display()(selected)) {
        this.value.set(null);
        this.onChange(null);
      }
      this.terms.next(value);
    });

    this.terms
      .pipe(
        debounceTime(250),
        switchMap((term) => this.search()(term.trim()).pipe(catchError(() => of([] as T[])))),
        takeUntilDestroyed(),
      )
      .subscribe((options) => this.options.set(options));
  }

  protected errorText(): string | null {
    return fieldErrorMessage(this.ngControl?.control ?? null);
  }

  protected onFocus(): void {
    const value = this.text.value;
    this.terms.next(typeof value === 'string' ? value : '');
  }

  protected onSelected(option: T): void {
    if (option === this.createToken) {
      const typed = this.value() ? '' : this.lastTypedText();
      this.text.setValue(this.value() ?? '', { emitEvent: false });
      this.create.emit(typed);
      return;
    }
    this.value.set(option);
    this.onChange(option);
    this.markTouched();
  }

  protected clear(): void {
    this.value.set(null);
    this.text.setValue('', { emitEvent: false });
    this.onChange(null);
    this.markTouched();
  }

  protected markTouched(): void {
    this.onTouched();
  }

  writeValue(value: T | null): void {
    this.value.set(value ?? null);
    this.text.setValue(value ?? '', { emitEvent: false });
  }

  registerOnChange(fn: (value: T | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) {
      this.text.disable({ emitEvent: false });
    } else {
      this.text.enable({ emitEvent: false });
    }
  }

  private lastTypedText(): string {
    const value = this.text.value;
    return typeof value === 'string' ? value.trim() : '';
  }
}
