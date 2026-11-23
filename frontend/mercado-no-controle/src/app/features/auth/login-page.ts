import { ChangeDetectionStrategy, Component, computed, inject, input, signal } from '@angular/core';
import { AbstractControl, FormControl, FormGroup, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { toApiError } from '../../core/api/api-error';
import { safeReturnUrl } from '../../core/auth/auth.guards';
import { AuthService } from '../../core/auth/auth.service';
import { APP_NAME } from '../../core/layout/app-title.strategy';
import { applyServerErrors, fieldErrorMessage } from '../../shared/forms/form-errors';

export const PASSWORD_MIN_LENGTH = 8;

/** Confirmação de senha igual à senha (grupo com `password` e `confirmPassword`). */
export function passwordsMatch(group: AbstractControl): ValidationErrors | null {
  const password = group.get('password')?.value;
  const confirm = group.get('confirmPassword');
  if (!confirm) return null;
  const mismatch = !!confirm.value && password !== confirm.value;
  const others = { ...(confirm.errors ?? {}) };
  delete others['mismatch'];
  confirm.setErrors(mismatch ? { ...others, mismatch: true } : Object.keys(others).length ? others : null);
  return null;
}

/** Entrada na aplicação: login ou, no primeiro acesso, cadastro do usuário único. */
@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule, MatButtonModule, MatCardModule, MatCheckboxModule, MatFormFieldModule, MatIconModule, MatInputModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login-page.html',
  styleUrl: './login-page.scss',
})
export class LoginPage {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);

  readonly returnUrl = input<string>();

  protected readonly appName = APP_NAME;
  protected readonly errorMessage = fieldErrorMessage;
  protected readonly minLength = PASSWORD_MIN_LENGTH;
  protected readonly setupRequired = computed(() => this.auth.status()?.setupRequired ?? false);
  protected readonly submitting = signal(false);
  protected readonly error = signal<string | null>(null);
  protected readonly showPassword = signal(false);

  protected readonly loginForm = new FormGroup({
    email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(254)] }),
    password: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(128)] }),
    rememberMe: new FormControl(true, { nonNullable: true }),
  });

  protected readonly setupForm = new FormGroup(
    {
      name: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(100)] }),
      email: new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.email, Validators.maxLength(254)] }),
      password: new FormControl('', {
        nonNullable: true,
        validators: [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH), Validators.maxLength(128)],
      }),
      confirmPassword: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    },
    { validators: passwordsMatch },
  );

  submitLogin(): void {
    if (this.loginForm.invalid) {
      this.loginForm.markAllAsTouched();
      return;
    }
    this.run(this.auth.login(this.loginForm.getRawValue()), this.loginForm);
  }

  submitSetup(): void {
    if (this.setupForm.invalid) {
      this.setupForm.markAllAsTouched();
      return;
    }
    const { name, email, password } = this.setupForm.getRawValue();
    this.run(this.auth.setup({ name, email, password }), this.setupForm);
  }

  private run(request: Observable<unknown>, form: FormGroup): void {
    this.submitting.set(true);
    this.error.set(null);
    request.subscribe({
      next: () => {
        this.submitting.set(false);
        void this.router.navigateByUrl(safeReturnUrl(this.returnUrl()));
      },
      error: (error: unknown) => {
        this.submitting.set(false);
        const apiError = toApiError(error);
        if (!applyServerErrors(form, apiError)) {
          this.error.set(apiError.message);
        }
        if (apiError.status === 409) {
          // Outro cadastro foi concluído antes: passa a pedir login.
          void this.auth.ensureLoaded(true);
        }
      },
    });
  }
}
