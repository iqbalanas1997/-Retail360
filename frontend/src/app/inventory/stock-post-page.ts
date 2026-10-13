import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { type Product } from '../core/catalog.models';
import { InventoryApi } from '../core/inventory-api.service';
import { type Shelf, type ShelfBalance, type StockMovement } from '../core/inventory.models';
import {
  OperationAttempt,
  parseAdjustmentQuantity,
  parsePositiveQuantity,
  parseReason,
} from '../core/inventory-rules';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

type StockPostMode = 'opening' | 'adjustment' | 'transfer';

@Component({
  selector: 'app-stock-post-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './stock-post-page.html',
})
export class StockPostPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly inventory = inject(InventoryApi);
  private readonly catalog = inject(ShopApi);
  private readonly session = inject(SessionService);
  private readonly attempt = new OperationAttempt();

  protected readonly canManage = this.session.canManageInventory;
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly error = signal('');
  protected readonly retryHint = signal('');
  protected readonly success = signal('');
  protected readonly products = signal<Product[]>([]);
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly movements = signal<StockMovement[]>([]);
  protected readonly balances = signal<ShelfBalance[]>([]);
  protected readonly form = this.formBuilder.nonNullable.group({
    productId: ['', Validators.required],
    shelfId: '',
    sourceShelfId: '',
    destinationShelfId: '',
    quantity: ['', Validators.required],
    reasonChoice: 'damaged',
    reasonText: 'opening',
  });

  constructor() {
    if (this.mode() === 'transfer') {
      this.form.controls.reasonText.setValue('');
    }

    void this.load();
  }

  protected mode(): StockPostMode {
    const mode = this.route.snapshot.data['mode'];
    if (mode === 'adjustment' || mode === 'transfer' || mode === 'opening') {
      return mode;
    }

    return 'opening';
  }

  protected quantityLabel(): string {
    return this.mode() === 'adjustment' ? 'Adjustment quantity' : 'Quantity';
  }

  protected shelfLabel(shelf: Shelf): string {
    return `${shelf.areaName} / ${shelf.name}${shelf.isActive ? '' : ' (inactive)'}`;
  }

  protected productLabel(product: Product): string {
    return product.barcodes.length > 0 ? `${product.name} (${product.barcodes.join(', ')})` : product.name;
  }

  protected quantity(value: number): string {
    return formatScaled(value, 3);
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    try {
      const [products, shelves] = await Promise.all([
        firstValueFrom(this.catalog.listProducts({ name: '', activeOnly: false, limit: 200, offset: 0 })),
        firstValueFrom(this.inventory.listShelves()),
      ]);
      this.products.set(products);
      this.shelves.set(shelves);
    } catch (error) {
      this.loadFailed.set(true);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async submit(): Promise<void> {
    if (this.saving()) {
      return;
    }

    this.error.set('');
    this.retryHint.set('');
    this.success.set('');
    let body: Record<string, string | number>;
    try {
      const prepared = this.prepare();
      body = { ...prepared.body, operationId: this.attempt.idFor(prepared.key) };
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    try {
      const movements = await this.post(body);
      this.movements.set(movements);
      this.success.set('Stock saved.');
      try {
        this.balances.set(await this.readBalances());
      } catch (error) {
        this.retryHint.set(error instanceof ApiError ? error.message : 'The balance could not be read.');
      }
      this.attempt.complete();
    } catch (error) {
      const apiError = error instanceof ApiError ? error : new ApiError(0, 'The request failed.');
      this.error.set(apiError.message);
      if (apiError.status === 0 || apiError.status >= 500) {
        this.retryHint.set('Submit again to retry this same stock operation.');
      }
    } finally {
      this.saving.set(false);
    }
  }

  private prepare(): { key: string; body: Record<string, string | number> } {
    const value = this.form.getRawValue();
    const productId = value.productId.trim();
    if (!productId) {
      throw new Error('Product was not found.');
    }

    if (this.mode() === 'transfer') {
      const sourceShelfId = value.sourceShelfId.trim();
      const destinationShelfId = value.destinationShelfId.trim();
      if (!sourceShelfId || !destinationShelfId) {
        throw new Error('Shelf was not found.');
      }

      if (sourceShelfId === destinationShelfId) {
        throw new Error('Transfer source and destination must be different shelves.');
      }

      const quantity = parsePositiveQuantity(value.quantity);
      const reason = parseReason(value.reasonText, false);
      return {
        key: JSON.stringify({ productId, sourceShelfId, destinationShelfId, quantity, reason }),
        body: { productId, sourceShelfId, destinationShelfId, quantity, reason },
      };
    }

    const shelfId = value.shelfId.trim();
    if (!shelfId) {
      throw new Error('Shelf was not found.');
    }

    if (this.mode() === 'adjustment') {
      const quantity = parseAdjustmentQuantity(value.quantity);
      const reason = parseReason(this.adjustmentReason(value.reasonChoice, value.reasonText), true);
      return {
        key: JSON.stringify({ mode: 'adjustment', productId, shelfId, quantity, reason }),
        body: { productId, shelfId, quantity, reason },
      };
    }

    const quantity = parsePositiveQuantity(value.quantity);
    const reason = parseReason(value.reasonText, true);
    return {
      key: JSON.stringify({ mode: 'opening', productId, shelfId, quantity, reason }),
      body: { productId, shelfId, quantity, reason },
    };
  }

  private adjustmentReason(choice: string, text: string): string {
    if (choice === 'damaged' || choice === 'expired' || choice === 'missing') {
      return choice;
    }

    return text;
  }

  private post(body: Record<string, string | number>) {
    const operationId = String(body['operationId']);
    const productId = String(body['productId']);
    const quantity = Number(body['quantity']);
    const reason = String(body['reason']);
    if (this.mode() === 'adjustment') {
      return firstValueFrom(
        this.inventory.adjustStock({ operationId, productId, shelfId: String(body['shelfId']), quantity, reason }),
      );
    }

    if (this.mode() === 'transfer') {
      return firstValueFrom(
        this.inventory.transferStock({
          operationId,
          productId,
          sourceShelfId: String(body['sourceShelfId']),
          destinationShelfId: String(body['destinationShelfId']),
          quantity,
          reason,
        }),
      );
    }

    return firstValueFrom(
      this.inventory.openStock({ operationId, productId, shelfId: String(body['shelfId']), quantity, reason }),
    );
  }

  private async readBalances(): Promise<ShelfBalance[]> {
    const value = this.form.getRawValue();
    if (this.mode() === 'transfer') {
      const [source, destination] = await Promise.all([
        firstValueFrom(this.inventory.listBalances(value.productId, value.sourceShelfId)),
        firstValueFrom(this.inventory.listBalances(value.productId, value.destinationShelfId)),
      ]);
      return [...source, ...destination];
    }

    return firstValueFrom(this.inventory.listBalances(value.productId, value.shelfId));
  }
}
