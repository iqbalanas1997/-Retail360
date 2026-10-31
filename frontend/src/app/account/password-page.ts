import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

@Component({
  selector: 'app-password-page',
  imports: [ReactiveFormsModule],
  templateUrl: './password-page.html',
})
export class PasswordPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly error = signal('');
  protected readonly submitting = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    currentPassword: ['', Validators.required],
    newPassword: ['', Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    this.error.set('');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Enter the current password and a new password.');
      return;
    }

    this.submitting.set(true);
    const { currentPassword, newPassword } = this.form.getRawValue();
    try {
      await firstValueFrom(this.api.changePassword(currentPassword, newPassword));
      this.session.clear();
      await this.router.navigateByUrl('/login', { state: { notice: 'Sign in with the new password.' } });
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.submitting.set(false);
    }
  }
}
