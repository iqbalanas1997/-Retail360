import { Component, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom, map } from 'rxjs';

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

export type StockPostMode = 'opening' | 'adjustment' | 'transfer';

interface ModeOption {
  id: StockPostMode;
  label: string;
  help: string;
}

@Component({
  selector: 'app-stock-post-page',
  imports: [ReactiveFormsModule],
  templateUrl: './stock-post-page.html',
})
export class StockPostPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly inventory = inject(InventoryApi);
  private readonly catalog = inject(ShopApi);
  private readonly session = inject(SessionService);
  private readonly attempt = new OperationAttempt();
  private balanceGeneration = 0;

  protected readonly modes: ModeOption[] = [
    {
      id: 'opening',
      label: 'Opening',
      help: 'Add first stock onto a shelf. Quantity must be greater than zero.',
    },
    {
      id: 'adjustment',
      label: 'Adjust',
      help: 'Add or remove stock on one shelf. Use a negative quantity to remove.',
    },
    {
      id: 'transfer',
      label: 'Transfer',
      help: 'Move stock from one shelf to another. Shop total stays the same.',
    },
  ];

  protected readonly canManage = this.session.canManageInventory;
  protected readonly selectedMode = signal<StockPostMode>('opening');
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly loadFailed = signal(false);
  protected readonly loadingBalances = signal(false);
  protected readonly error = signal('');
  protected readonly findHint = signal('');
  protected readonly retryHint = signal('');
  protected readonly success = signal('');
  protected readonly products = signal<Product[]>([]);
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly productQuery = signal('');
  protected readonly finding = signal(false);
  protected readonly productsTruncated = signal(false);
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

  private readonly queryMode = toSignal(
    this.route.queryParamMap.pipe(map((params) => normalizeStockPostMode(params.get('mode')))),
    { initialValue: normalizeStockPostMode(this.route.snapshot.queryParamMap.get('mode')) },
  );

  private readonly syncMode = effect(() => {
    const mode = this.queryMode();
    untracked(() => this.applyMode(mode, false));
  });

  constructor() {
    this.form.controls.productId.valueChanges.subscribe(() => {
      this.movements.set([]);
      this.success.set('');
      void this.refreshBalances();
    });
    void this.load();
  }

  protected modeHelp(): string {
    return this.modes.find((mode) => mode.id === this.selectedMode())?.help ?? '';
  }

  protected quantityLabel(): string {
    return this.selectedMode() === 'adjustment' ? 'Adjustment quantity' : 'Quantity';
  }

  protected saveLabel(): string {
    if (this.selectedMode() === 'adjustment') {
      return 'Save adjustment';
    }

    if (this.selectedMode() === 'transfer') {
      return 'Save transfer';
    }

    return 'Save opening';
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

  protected selectMode(mode: StockPostMode): void {
    if (mode === this.selectedMode()) {
      return;
    }

    this.applyMode(mode, true);
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { mode: modeQueryValue(mode) },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
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
      this.productsTruncated.set(products.length === 200);
      this.shelves.set(shelves);
    } catch (error) {
      this.loadFailed.set(true);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loading.set(false);
    }
  }

  protected async findProducts(): Promise<void> {
    if (this.finding() || this.saving()) {
      return;
    }

    const query = this.productQuery().trim();
    this.finding.set(true);
    this.error.set('');
    this.findHint.set('');
    try {
      let products = await firstValueFrom(
        this.catalog.listProducts({
          name: query,
          activeOnly: false,
          limit: 200,
          offset: 0,
        }),
      );

      if (products.length === 0 && query) {
        try {
          products = [await firstValueFrom(this.catalog.findProductByBarcode(query))];
        } catch (error) {
          if (!(error instanceof ApiError) || (error.status !== 404 && error.status !== 400)) {
            throw error;
          }
        }
      }

      const selectedId = this.form.controls.productId.value;
      const selected = this.products().find((product) => product.id === selectedId);
      const visible =
        selected && !products.some((product) => product.id === selected.id) ? [selected, ...products] : products;
      this.products.set(visible);
      this.productsTruncated.set(products.length === 200 && !!query);

      if (products.length === 0) {
        this.findHint.set('No products matched that name or barcode.');
        return;
      }

      if (products.length === 1) {
        this.form.controls.productId.setValue(products[0].id);
        this.findHint.set(`Selected ${products[0].name}.`);
        return;
      }

      if (!products.some((product) => product.id === selectedId)) {
        this.form.controls.productId.setValue('');
      }

      this.findHint.set(`${products.length} products found. Choose one below.`);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.finding.set(false);
    }
  }

  protected inputValue(event: Event): string {
    return (event.target as HTMLInputElement).value;
  }

  protected async submit(): Promise<void> {
    if (this.saving()) {
      return;
    }

    this.error.set('');
    this.findHint.set('');
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
        await this.refreshBalances();
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

  private applyMode(mode: StockPostMode, resetFeedback: boolean): void {
    const previous = this.selectedMode();
    this.selectedMode.set(mode);
    if (previous === mode && !resetFeedback) {
      return;
    }

    if (mode === 'transfer') {
      this.form.controls.reasonText.setValue('');
    } else if (mode === 'opening') {
      this.form.controls.reasonText.setValue('opening');
    } else if (previous !== 'adjustment') {
      this.form.controls.reasonText.setValue('');
    }

    if (resetFeedback || previous !== mode) {
      this.error.set('');
      this.findHint.set('');
      this.retryHint.set('');
      this.success.set('');
      this.movements.set([]);
      this.attempt.complete();
      void this.refreshBalances();
    }
  }

  private async refreshBalances(): Promise<void> {
    const productId = this.form.controls.productId.value.trim();
    const generation = ++this.balanceGeneration;
    if (!productId) {
      this.balances.set([]);
      this.loadingBalances.set(false);
      return;
    }

    this.loadingBalances.set(true);
    try {
      const balances = await firstValueFrom(this.inventory.listBalances(productId));
      if (generation !== this.balanceGeneration) {
        return;
      }

      this.balances.set(balances);
    } catch (error) {
      if (generation !== this.balanceGeneration) {
        return;
      }

      this.balances.set([]);
      this.retryHint.set(error instanceof ApiError ? error.message : 'The balance could not be read.');
    } finally {
      if (generation === this.balanceGeneration) {
        this.loadingBalances.set(false);
      }
    }
  }

  private prepare(): { key: string; body: Record<string, string | number> } {
    const value = this.form.getRawValue();
    const productId = value.productId.trim();
    if (!productId) {
      throw new Error('Product was not found.');
    }

    if (this.selectedMode() === 'transfer') {
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

    if (this.selectedMode() === 'adjustment') {
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
    if (this.selectedMode() === 'adjustment') {
      return firstValueFrom(
        this.inventory.adjustStock({ operationId, productId, shelfId: String(body['shelfId']), quantity, reason }),
      );
    }

    if (this.selectedMode() === 'transfer') {
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
}

export function normalizeStockPostMode(value: string | null | undefined): StockPostMode {
  if (value === 'adjust' || value === 'adjustment' || value === 'adjustments') {
    return 'adjustment';
  }

  if (value === 'transfer' || value === 'transfers') {
    return 'transfer';
  }

  return 'opening';
}

function modeQueryValue(mode: StockPostMode): string {
  if (mode === 'adjustment') {
    return 'adjust';
  }

  return mode;
}
