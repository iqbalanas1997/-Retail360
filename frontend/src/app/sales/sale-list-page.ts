import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError, toApiError } from '../core/api-error';
import { formatMoney } from '../core/purchase-rules';
import { SaleApi } from '../core/sale-api.service';
import { formatSoldAt, parseReceiptNumber } from '../core/sale-rules';
import { type Sale } from '../core/sale.models';
import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-sale-list-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './sale-list-page.html',
})
export class SaleListPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly salesApi = inject(SaleApi);
  private readonly session = inject(SessionService);
  private loadGeneration = 0;

  protected readonly pageSize = 50;
  protected readonly canSell = this.session.canCompleteSales;
  protected readonly sales = signal<Sale[]>([]);
  protected readonly loading = signal(true);
  protected readonly loadingMore = signal(false);
  protected readonly error = signal('');
  protected readonly hasMore = signal(false);
  protected readonly form = this.formBuilder.nonNullable.group({
    receiptNumber: '',
  });

  constructor() {
    void this.reload();
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected soldAt(sale: Sale): string {
    return formatSoldAt(sale.soldAt);
  }

  protected async search(): Promise<void> {
    await this.reload();
  }

  protected async reload(): Promise<void> {
    const generation = ++this.loadGeneration;
    this.loading.set(true);
    this.loadingMore.set(false);
    this.error.set('');
    await this.load(0, false, generation);
  }

  protected async showMore(): Promise<void> {
    if (this.loading() || this.loadingMore() || !this.hasMore()) {
      return;
    }

    const generation = this.loadGeneration;
    this.loadingMore.set(true);
    this.error.set('');
    await this.load(this.sales().length, true, generation);
  }

  private async load(offset: number, append: boolean, generation: number): Promise<void> {
    let receiptNumber: number | null;
    try {
      receiptNumber = parseReceiptNumber(this.form.controls.receiptNumber.value);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      this.loading.set(false);
      this.loadingMore.set(false);
      return;
    }

    try {
      const rows = await firstValueFrom(
        this.salesApi.list({ receiptNumber, limit: this.pageSize, offset }),
      );
      if (generation !== this.loadGeneration) {
        return;
      }

      this.sales.set(append ? [...this.sales(), ...rows] : rows);
      this.hasMore.set(rows.length === this.pageSize);
    } catch (error) {
      if (generation !== this.loadGeneration) {
        return;
      }

      this.error.set(messageFrom(error));
      if (!append) {
        this.sales.set([]);
        this.hasMore.set(false);
      }
    } finally {
      if (generation === this.loadGeneration) {
        this.loading.set(false);
        this.loadingMore.set(false);
      }
    }
  }
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : toApiError(error).message;
}
