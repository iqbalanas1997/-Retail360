import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { type ProductStock, type ShelfBalance } from '../core/inventory.models';
import { InventoryApi } from '../core/inventory-api.service';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-stock-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './stock-page.html',
})
export class StockPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(InventoryApi);
  private readonly session = inject(SessionService);

  protected readonly pageSize = 50;
  protected readonly canManage = this.session.canManageInventory;
  protected readonly rows = signal<ProductStock[]>([]);
  protected readonly balances = signal<ShelfBalance[]>([]);
  protected readonly selectedId = signal('');
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly loadingBalances = signal(false);
  protected readonly error = signal('');
  protected readonly balanceError = signal('');
  protected readonly hasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    name: '',
    status: 'all',
  });

  constructor() {
    void this.reload();
  }

  protected visibleRows(): ProductStock[] {
    const name = this.form.controls.name.value.trim().toLowerCase();
    if (!name) {
      return this.rows();
    }

    return this.rows().filter((row) => row.productName.toLowerCase().includes(name));
  }

  protected quantity(value: number): string {
    return formatScaled(value, 3);
  }

  protected statusLabel(status: string): string {
    if (status === 'out') {
      return 'Out of stock';
    }

    if (status === 'low') {
      return 'Low stock';
    }

    return 'In stock';
  }

  protected async reload(): Promise<void> {
    this.loading.set(true);
    this.error.set('');
    this.selectedId.set('');
    this.balances.set([]);
    await this.load(0, false);
    this.loading.set(false);
  }

  protected async showMore(): Promise<void> {
    this.loadingMore.set(true);
    this.error.set('');
    await this.load(this.rows().length, true);
    this.loadingMore.set(false);
  }

  protected async selectProduct(productId: string): Promise<void> {
    this.selectedId.set(productId);
    this.balanceError.set('');
    this.loadingBalances.set(true);
    try {
      this.balances.set(await firstValueFrom(this.api.listBalances(productId)));
    } catch (error) {
      this.balances.set([]);
      this.balanceError.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loadingBalances.set(false);
    }
  }

  private async load(offset: number, append: boolean): Promise<void> {
    try {
      const page = await firstValueFrom(
        this.api.listStock(this.form.controls.status.value, this.pageSize, offset),
      );
      this.rows.set(append ? [...this.rows(), ...page] : page);
      this.hasMore.set(page.length === this.pageSize);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
      if (!append) {
        this.rows.set([]);
        this.hasMore.set(false);
      }
    }
  }
}
