import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError, toApiError } from '../core/api-error';
import { formatMoney } from '../core/purchase-rules';
import { SaleApi } from '../core/sale-api.service';
import { formatSoldAt } from '../core/sale-rules';
import { type Sale } from '../core/sale.models';

@Component({
  selector: 'app-sale-detail-page',
  imports: [RouterLink],
  templateUrl: './sale-detail-page.html',
})
export class SaleDetailPage {
  private readonly salesApi = inject(SaleApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly sale = signal<Sale | null>(null);

  constructor() {
    const message = readSuccess(this.router);
    if (message) {
      this.success.set(message);
    }

    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected soldAt(sale: Sale): string {
    return formatSoldAt(sale.soldAt);
  }

  protected print(): void {
    window.print();
  }

  private async load(): Promise<void> {
    const id = this.route.snapshot.paramMap.get('id');
    if (!id) {
      this.error.set('Sale was not found.');
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.error.set('');
    try {
      this.sale.set(await firstValueFrom(this.salesApi.get(id)));
    } catch (error) {
      this.sale.set(null);
      this.error.set(messageFrom(error));
    } finally {
      this.loading.set(false);
    }
  }
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : toApiError(error).message;
}

function readSuccess(router: Router): string {
  const candidates = [
    router.getCurrentNavigation()?.extras?.state,
    router.lastSuccessfulNavigation()?.extras?.state,
    history.state,
  ];
  for (const state of candidates) {
    if (state && typeof state === 'object' && 'success' in state && typeof state.success === 'string') {
      const next = { ...(history.state as Record<string, unknown>) };
      delete next['success'];
      history.replaceState(next, '');
      return state.success;
    }
  }

  return '';
}
