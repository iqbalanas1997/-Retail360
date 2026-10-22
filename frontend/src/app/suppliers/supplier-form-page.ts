import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { PurchaseApi } from '../core/purchase-api.service';
import { type Purchase } from '../core/purchase.models';
import { formatMoney } from '../core/purchase-rules';
import { SessionService } from '../core/session.service';
import { SupplierApi } from '../core/supplier-api.service';
import { type SaveSupplierBody, type Supplier } from '../core/supplier.models';
import {
  optionalAddress,
  optionalContactPerson,
  optionalEmail,
  optionalNotes,
  optionalPhone,
  supplierName,
} from '../core/supplier-rules';

@Component({
  selector: 'app-supplier-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './supplier-form-page.html',
})
export class SupplierFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(SupplierApi);
  private readonly purchasesApi = inject(PurchaseApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private historyGeneration = 0;

  protected readonly historyPageSize = 50;
  protected readonly canWrite = this.session.canWriteSuppliers;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly supplier = signal<Supplier | null>(null);
  protected readonly purchases = signal<Purchase[]>([]);
  protected readonly historyLoading = signal(false);
  protected readonly historyLoadingMore = signal(false);
  protected readonly historyError = signal('');
  protected readonly historyHasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    contactPerson: '',
    phone: '',
    email: '',
    address: '',
    notes: '',
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
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

  protected reloadHistory(): void {
    void this.loadPurchases(false);
  }

  protected showMorePurchases(): void {
    if (this.historyLoading() || this.historyLoadingMore()) {
      return;
    }

    void this.loadPurchases(true);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    this.clearHistory();
    const message = readSuccess(this.router);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      if (id) {
        const supplier = await firstValueFrom(this.api.get(id));
        this.supplier.set(supplier);
        this.form.setValue({
          name: supplier.name,
          contactPerson: supplier.contactPerson ?? '',
          phone: supplier.phone ?? '',
          email: supplier.email ?? '',
          address: supplier.address ?? '',
          notes: supplier.notes ?? '',
        });
        void this.loadPurchases(false);
      } else {
        this.supplier.set(null);
      }

      if (this.canWrite()) {
        this.form.enable();
      } else {
        this.form.disable();
      }
    } catch (error) {
      this.loadFailed.set(true);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      if (!this.loadFailed() && message) {
        this.success.set(message);
      }

      this.loading.set(false);
    }
  }

  protected async save(): Promise<void> {
    if (!this.canWrite() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    let body: SaveSupplierBody;
    try {
      body = this.buildBody();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    const existing = this.supplier();
    try {
      const saved = existing
        ? await firstValueFrom(this.api.update(existing.id, body))
        : await firstValueFrom(this.api.create(body));
      this.success.set(existing ? 'Supplier saved.' : 'Supplier created.');
      if (!existing) {
        await this.router.navigate(['/suppliers', saved.id], {
          state: { success: 'Supplier created.' },
        });
        return;
      }

      this.supplier.set(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async setActive(active: boolean): Promise<void> {
    const existing = this.supplier();
    if (!existing || !this.canWrite() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    this.saving.set(true);
    try {
      const saved = await firstValueFrom(
        active ? this.api.activate(existing.id) : this.api.deactivate(existing.id),
      );
      this.supplier.set(saved);
      this.success.set(active ? 'Supplier is active.' : 'Supplier is inactive.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async remove(): Promise<void> {
    const existing = this.supplier();
    if (!existing || !this.canWrite() || this.saving()) {
      return;
    }

    this.error.set('');
    this.saving.set(true);
    try {
      await firstValueFrom(this.api.delete(existing.id));
      await this.router.navigate(['/suppliers'], { state: { success: 'Supplier removed.' } });
    } catch (error) {
      this.confirmDelete.set(false);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  private clearHistory(): void {
    this.historyGeneration += 1;
    this.purchases.set([]);
    this.historyLoading.set(false);
    this.historyLoadingMore.set(false);
    this.historyError.set('');
    this.historyHasMore.set(false);
  }

  private async loadPurchases(append: boolean): Promise<void> {
    const id = this.supplier()?.id;
    if (!id) {
      return;
    }

    const generation = append ? this.historyGeneration : ++this.historyGeneration;
    if (append) {
      this.historyLoadingMore.set(true);
    } else {
      this.historyLoading.set(true);
      this.historyError.set('');
    }

    const offset = append ? this.purchases().length : 0;
    try {
      const page = await firstValueFrom(
        this.purchasesApi.list({
          supplierId: id,
          status: '',
          limit: this.historyPageSize,
          offset,
        }),
      );
      if (generation !== this.historyGeneration) {
        return;
      }

      this.purchases.set(append ? [...this.purchases(), ...page] : page);
      this.historyHasMore.set(page.length === this.historyPageSize);
    } catch (error) {
      if (generation !== this.historyGeneration) {
        return;
      }

      this.historyError.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.purchases.set([]);
        this.historyHasMore.set(false);
      }
    } finally {
      if (generation === this.historyGeneration) {
        this.historyLoading.set(false);
        this.historyLoadingMore.set(false);
      }
    }
  }

  private buildBody(): SaveSupplierBody {
    const value = this.form.getRawValue();
    return {
      name: supplierName(value.name),
      contactPerson: optionalContactPerson(value.contactPerson),
      phone: optionalPhone(value.phone),
      email: optionalEmail(value.email),
      address: optionalAddress(value.address),
      notes: optionalNotes(value.notes),
    };
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
