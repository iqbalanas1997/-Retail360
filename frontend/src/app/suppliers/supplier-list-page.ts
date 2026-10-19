import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { SessionService } from '../core/session.service';
import { SupplierApi } from '../core/supplier-api.service';
import { type Supplier } from '../core/supplier.models';

@Component({
  selector: 'app-supplier-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './supplier-list-page.html',
})
export class SupplierListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(SupplierApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly pageSize = 50;
  protected readonly canWrite = this.session.canWriteSuppliers;
  protected readonly suppliers = signal<Supplier[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly hasMore = signal(false);
  private loadGeneration = 0;
  protected readonly form = this.formBuilder.nonNullable.group({
    name: '',
    activeOnly: false,
  });

  constructor() {
    const message = readSuccess(this.router);
    if (message) {
      this.success.set(message);
    }

    void this.reload();
  }

  protected status(supplier: Supplier): string {
    return supplier.isActive ? 'Active' : 'Inactive';
  }

  protected async search(): Promise<void> {
    this.success.set('');
    await this.reload();
  }

  protected async reload(): Promise<void> {
    const generation = ++this.loadGeneration;
    this.loading.set(true);
    this.loadingMore.set(false);
    this.error.set('');
    await this.load(0, false, generation);
    if (generation === this.loadGeneration) {
      this.loading.set(false);
    }
  }

  protected async showMore(): Promise<void> {
    if (this.loading() || this.loadingMore()) {
      return;
    }

    const generation = this.loadGeneration;
    this.loadingMore.set(true);
    this.error.set('');
    await this.load(this.suppliers().length, true, generation);
    if (generation === this.loadGeneration) {
      this.loadingMore.set(false);
    }
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    const { name, activeOnly } = this.form.getRawValue();
    try {
      const page = await firstValueFrom(this.api.list({ name, activeOnly, limit: this.pageSize, offset }));
      if (generation !== this.loadGeneration) {
        return;
      }

      this.suppliers.set(append ? [...this.suppliers(), ...page] : page);
      this.hasMore.set(page.length === this.pageSize);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.suppliers.set([]);
        this.hasMore.set(false);
      }
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
