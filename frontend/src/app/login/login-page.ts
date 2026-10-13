import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

@Component({
  selector: 'app-login-page',
  imports: [ReactiveFormsModule],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ShopApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly error = signal('');
  protected readonly submitting = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    userName: ['', Validators.required],
    password: ['', Validators.required],
  });

  protected async submit(): Promise<void> {
    if (this.submitting()) {
      return;
    }

    this.error.set('');
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.error.set('Enter the user name and password.');
      return;
    }

    this.submitting.set(true);
    const { userName, password } = this.form.getRawValue();
    try {
      const response = await firstValueFrom(this.api.login(userName.trim(), password));
      this.session.remember(response);
      await this.router.navigateByUrl('/catalog/products');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.submitting.set(false);
    }
  }
}
