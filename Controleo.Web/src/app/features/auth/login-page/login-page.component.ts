import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';

@Component({
  selector: 'app-login-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './login-page.component.html',
  styleUrl: './login-page.component.scss'
})
export class LoginPageComponent {
  isLoading = false;
  errorMessage = '';
  successMessage = '';
  mode: 'login' | 'register' = 'login';
  emailTouched = false;

  name = '';
  email = '';
  password = '';
  confirmPassword = '';

  constructor(
    private readonly auth: AuthService,
    private readonly router: Router
  ) {}

  setMode(mode: 'login' | 'register'): void {
    this.mode = mode;
    this.errorMessage = '';
    this.successMessage = '';
    this.emailTouched = false;
  }

  get isEmailValid(): boolean {
    const email = this.email.trim();
    return email.length > 0 && this.isValidEmail(email);
  }

  get showEmailInvalid(): boolean {
    return this.emailTouched && this.email.trim().length > 0 && !this.isEmailValid;
  }

  get showEmailValid(): boolean {
    return this.emailTouched && this.isEmailValid;
  }

  async submit(): Promise<void> {
    if (this.isLoading) {
      return;
    }

    this.errorMessage = '';
    this.successMessage = '';

    const email = this.email.trim();
    const password = this.password.trim();
    this.emailTouched = true;

    if (!email || !password) {
      this.errorMessage = 'Correo y contraseña son obligatorios.';
      return;
    }

    if (!this.isValidEmail(email)) {
      this.errorMessage = 'Ingresa un correo válido.';
      return;
    }

    if (this.mode === 'register') {
      if (!this.name.trim()) {
        this.errorMessage = 'El nombre es obligatorio para crear la cuenta.';
        return;
      }

      if (password.length < 8) {
        this.errorMessage = 'La contraseña debe tener al menos 8 caracteres.';
        return;
      }

      if (password !== this.confirmPassword.trim()) {
        this.errorMessage = 'Las contraseñas no coinciden.';
        return;
      }
    }

    this.isLoading = true;

    try {
      if (this.mode === 'register') {
        await this.auth.register({
          name: this.name.trim(),
          email,
          password
        });
        this.successMessage = 'Cuenta creada correctamente.';
      } else {
        await this.auth.login({ email, password });
      }

      await this.router.navigateByUrl('/registrar');
    } catch (error) {
      this.errorMessage = this.resolveErrorMessage(error);
    } finally {
      this.isLoading = false;
    }
  }

  private resolveErrorMessage(error: unknown): string {
    const fallback = 'No fue posible completar la autenticación. Verifica tus datos e intenta de nuevo.';

    if (!error || typeof error !== 'object' || !('error' in error)) {
      return fallback;
    }

    const rawPayload = (error as { error?: unknown }).error;
    const payload = this.normalizePayload(rawPayload);

    if (payload?.message && payload.message.trim().length > 0) {
      return payload.message;
    }

    const validationMessage = this.extractValidationMessage(payload?.errors);
    return validationMessage ?? fallback;
  }

  private isValidEmail(email: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  }

  private normalizePayload(rawPayload: unknown): { message?: string; errors?: Record<string, string[]> } | null {
    if (!rawPayload) {
      return null;
    }

    if (typeof rawPayload === 'string') {
      try {
        const parsed = JSON.parse(rawPayload) as { message?: string; errors?: Record<string, string[]> };
        return parsed;
      } catch {
        return { message: rawPayload };
      }
    }

    if (typeof rawPayload === 'object') {
      return rawPayload as { message?: string; errors?: Record<string, string[]> };
    }

    return null;
  }

  private extractValidationMessage(errors?: Record<string, string[]>): string | null {
    if (!errors || typeof errors !== 'object') {
      return null;
    }

    for (const key of Object.keys(errors)) {
      const messages = errors[key];
      if (Array.isArray(messages) && messages.length > 0 && messages[0]?.trim()) {
        return messages[0];
      }
    }

    return null;
  }
}
