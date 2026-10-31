import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { SessionService } from '../core/session.service';
import { UserApi } from '../core/user-api.service';
import { optionalPassword, requirePassword, requireRole, requireUserName } from '../core/user-rules';
import { SHOP_ROLES, type SaveUserBody, type ShopUser } from '../core/user.models';

@Component({
  selector: 'app-user-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './user-form-page.html',
})
export class UserFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(UserApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly roles = SHOP_ROLES;
  protected readonly canManage = this.session.canManageUsers;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly user = signal<ShopUser | null>(null);
  protected readonly form = this.formBuilder.nonNullable.group({
    userName: ['', Validators.required],
    role: 'Cashier',
    password: '',
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected isSelf(): boolean {
    const current = this.user();
    return current !== null && current.id === this.session.userId();
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      if (id) {
        const user = await firstValueFrom(this.api.get(id));
        this.user.set(user);
        this.form.setValue({ userName: user.userName, role: user.role, password: '' });
      } else {
        this.user.set(null);
        this.form.setValue({ userName: '', role: 'Cashier', password: '' });
      }

      if (this.canManage()) {
        this.form.enable();
      } else {
        this.form.disable();
      }

      const message = readSuccess(this.router);
      if (message) {
        this.success.set(message);
      }
    } catch (error) {
      this.loadFailed.set(true);
      this.user.set(null);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.canManage() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    let body: SaveUserBody;
    try {
      const value = this.form.getRawValue();
      body = {
        userName: requireUserName(value.userName),
        role: requireRole(value.role),
        password: this.user() ? optionalPassword(value.password) : requirePassword(value.password),
      };
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    const existing = this.user();
    try {
      const saved = existing
        ? await firstValueFrom(this.api.update(existing.id, body))
        : await firstValueFrom(this.api.create(body));
      this.success.set(existing ? 'User saved.' : 'User created.');
      if (!existing) {
        await this.router.navigate(['/users', saved.id], { state: { success: 'User created.' } });
        return;
      }

      this.user.set(saved);
      this.form.controls.password.setValue('');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async setActive(active: boolean): Promise<void> {
    const existing = this.user();
    if (!existing || !this.canManage() || this.saving() || this.isSelf()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    this.saving.set(true);
    try {
      const saved = await firstValueFrom(
        active ? this.api.activate(existing.id) : this.api.deactivate(existing.id),
      );
      this.user.set(saved);
      this.success.set(active ? 'User is active.' : 'User is inactive.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const existing = this.user();
    if (!existing || !this.canManage() || this.saving() || this.isSelf()) {
      return;
    }

    this.error.set('');
    this.saving.set(true);
    try {
      await firstValueFrom(this.api.delete(existing.id));
      await this.router.navigate(['/users'], { state: { success: 'User removed.' } });
    } catch (error) {
      this.confirmDelete.set(false);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }
}

function readSuccess(router: Router): string {
  const candidates = [
    router.getCurrentNavigation()?.extras?.state,
    router.lastSuccessfulNavigation()?.extras?.state,
    history.state,
  ];
  for (const state of candidates) {
    if (state && typeof state === 'object' && 'success' in state && typeof state.success === 'string') {
      return state.success;
    }
  }

  return '';
}
