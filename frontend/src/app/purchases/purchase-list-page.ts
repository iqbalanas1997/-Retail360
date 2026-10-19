import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatMoney } from '../core/purchase-rules';
import { PurchaseApi } from '../core/purchase-api.service';
import { type Purchase } from '../core/purchase.models';
import { SessionService } from '../core/session.service';
import { SupplierApi } from '../core/supplier-api.service';
import { type Supplier } from '../core/supplier.models';

@Component({
  selector: 'app-purchase-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './purchase-list-page.html',
})
export class PurchaseListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly purchasesApi = inject(PurchaseApi);
  private readonly suppliersApi = inject(SupplierApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);

  protected readonly pageSize = 50;
  protected readonly canRecord = this.session.canRecordPurchases;
  protected readonly purchases = signal<Purchase[]>([]);
  protected readonly suppliers = signal<Supplier[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly hasMore = signal(false);
  private loadGeneration = 0;
  protected readonly form = this.formBuilder.nonNullable.group({
    supplierId: '',
    status: '',
  });

  constructor() {
    const message = readSuccess(this.router);
    if (message) {
      this.success.set(message);
    }

    void this.reload();
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected reference(purchase: Purchase): string {
    return purchase.reference?.trim() || purchase.id;
  }

  protected statusLabel(status: string): string {
    if (status === 'recorded') {
      return 'Recorded';
    }

    if (status === 'received') {
      return 'Received';
    }

    return status;
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
    await this.load(this.purchases().length, true, generation);
    if (generation === this.loadGeneration) {
      this.loadingMore.set(false);
    }
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    const { supplierId, status } = this.form.getRawValue();
    const purchaseRequest = firstValueFrom(
      this.purchasesApi.list({
        supplierId,
        status,
        limit: this.pageSize,
        offset,
      }),
    );
    const supplierRequest = append
      ? Promise.resolve(this.suppliers())
      : firstValueFrom(this.suppliersApi.list({ name: '', activeOnly: false, limit: 200, offset: 0 }));
    const [purchaseResult, supplierResult] = await Promise.allSettled([purchaseRequest, supplierRequest]);
    if (generation !== this.loadGeneration) {
      return;
    }

    if (supplierResult.status === 'fulfilled') {
      this.suppliers.set(supplierResult.value);
    }

    if (purchaseResult.status === 'fulfilled') {
      this.purchases.set(append ? [...this.purchases(), ...purchaseResult.value] : purchaseResult.value);
      this.hasMore.set(purchaseResult.value.length === this.pageSize);
      if (supplierResult.status === 'rejected') {
        this.error.set(messageFrom(supplierResult.reason));
      }
      return;
    }

    this.error.set(messageFrom(purchaseResult.reason));
    if (!append) {
      this.purchases.set([]);
      this.hasMore.set(false);
    }
  }
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : 'The request failed.';
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
