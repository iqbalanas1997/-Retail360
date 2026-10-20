import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError, toApiError } from '../core/api-error';
import { type Product } from '../core/catalog.models';
import { OperationAttempt } from '../core/inventory-rules';
import { InventoryApi } from '../core/inventory-api.service';
import { type Shelf } from '../core/inventory.models';
import { formatMoney } from '../core/purchase-rules';
import { SaleApi } from '../core/sale-api.service';
import {
  MAX_SALE_LINES,
  parseSaleQuantity,
  saleAttemptKey,
  saleLineTotal,
  saleTotal,
} from '../core/sale-rules';
import { type CompleteSaleBody } from '../core/sale.models';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';

interface CartLine {
  key: string;
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: string;
  shelfId: string;
}

@Component({
  selector: 'app-pos-page',
  imports: [FormsModule, RouterLink],
  templateUrl: './pos-page.html',
})
export class PosPage {
  private readonly shopApi = inject(ShopApi);
  private readonly inventoryApi = inject(InventoryApi);
  private readonly salesApi = inject(SaleApi);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly attempt = new OperationAttempt();
  private lineKey = 0;
  private lookupGeneration = 0;
  private shelfGeneration = 0;
  private barcodeInFlight = '';
  private completedSale: { id: string; key: string } | null = null;

  protected readonly canSell = this.session.canCompleteSales;
  protected readonly maxLines = MAX_SALE_LINES;
  protected readonly loading = signal(true);
  protected readonly finding = signal(false);
  protected readonly selling = signal(false);
  protected readonly shelfError = signal(false);
  protected readonly error = signal('');
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly matches = signal<Product[]>([]);
  protected readonly lines = signal<CartLine[]>([]);
  protected readonly barcode = signal('');
  protected readonly productQuery = signal('');
  protected readonly quantity = signal('1');
  protected readonly shelfId = signal('');

  constructor() {
    void this.loadShelves();
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected shelfLabel(shelf: Shelf): string {
    return shelf.isActive ? `${shelf.areaName} / ${shelf.name}` : `${shelf.areaName} / ${shelf.name} (inactive)`;
  }

  protected lineMoney(line: CartLine): string {
    try {
      return formatMoney(saleLineTotal(parseSaleQuantity(line.quantity), line.unitPrice));
    } catch {
      return '—';
    }
  }

  protected documentTotal(): string {
    try {
      return formatMoney(saleTotal(this.lines().map((line) => this.parsedLineTotal(line))));
    } catch {
      return '—';
    }
  }

  protected async findBarcode(): Promise<void> {
    const barcode = this.barcode().trim();
    if (!barcode || this.selling() || !this.session.isSignedIn()) {
      return;
    }

    if (barcode === this.barcodeInFlight) {
      return;
    }

    this.barcodeInFlight = barcode;
    const generation = ++this.lookupGeneration;
    this.finding.set(true);
    this.error.set('');
    try {
      const product = await firstValueFrom(this.shopApi.findProductByBarcode(barcode));
      const current = generation === this.lookupGeneration;
      if (!product.isActive) {
        if (!current) {
          return;
        }

        throw new ApiError(409, 'Product is inactive.');
      }

      if (current) {
        this.matches.set([product]);
      }

      this.addProduct(product, current);
    } catch (error) {
      if (generation !== this.lookupGeneration) {
        return;
      }

      this.matches.set([]);
      this.error.set(messageFrom(error));
    } finally {
      if (this.barcodeInFlight === barcode) {
        this.barcodeInFlight = '';
      }

      if (generation === this.lookupGeneration) {
        this.finding.set(false);
      }
    }
  }

  protected async searchProducts(): Promise<void> {
    if (this.selling() || !this.session.isSignedIn()) {
      return;
    }

    const generation = ++this.lookupGeneration;
    this.finding.set(true);
    this.error.set('');
    try {
      const products = await firstValueFrom(
        this.shopApi.listProducts({
          name: this.productQuery(),
          activeOnly: true,
          limit: 50,
          offset: 0,
        }),
      );
      if (generation !== this.lookupGeneration) {
        return;
      }

      this.matches.set(products.filter((product) => product.isActive));
      if (this.matches().length === 0) {
        this.error.set('No products match this search.');
      }
    } catch (error) {
      if (generation !== this.lookupGeneration) {
        return;
      }

      this.error.set(messageFrom(error));
    } finally {
      if (generation === this.lookupGeneration) {
        this.finding.set(false);
      }
    }
  }

  protected addProduct(product: Product, report = true): void {
    if (this.selling() || this.lines().length >= MAX_SALE_LINES || !product.isActive) {
      if (!product.isActive && report) {
        this.error.set('Product is inactive.');
      }

      return;
    }

    let quantity: number;
    try {
      quantity = parseSaleQuantity(this.quantity());
    } catch (error) {
      if (report) {
        this.error.set(error instanceof Error ? error.message : 'The request failed.');
      }

      return;
    }

    if (!this.shelfId()) {
      if (report) {
        this.error.set('Choose a shelf for every line.');
      }

      return;
    }

    if (report) {
      this.error.set('');
    }
    this.lines.update((lines) => [
      ...lines,
      {
        key: String(++this.lineKey),
        productId: product.id,
        productName: product.name,
        unitPrice: product.sellingPrice,
        quantity: formatQuantity(quantity),
        shelfId: this.shelfId(),
      },
    ]);
  }

  protected updateQuantity(key: string, value: string): void {
    this.lines.update((lines) => lines.map((line) => (line.key === key ? { ...line, quantity: value } : line)));
  }

  protected updateShelf(key: string, value: string): void {
    this.lines.update((lines) => lines.map((line) => (line.key === key ? { ...line, shelfId: value } : line)));
  }

  protected removeLine(key: string): void {
    if (this.selling()) {
      return;
    }

    this.lines.update((lines) => lines.filter((line) => line.key !== key));
  }

  protected reloadShelves(): void {
    void this.loadShelves();
  }

  protected async complete(): Promise<void> {
    if (!this.canSell() || this.selling() || !this.session.isSignedIn()) {
      return;
    }

    this.error.set('');
    let body: CompleteSaleBody;
    try {
      body = this.buildBody();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    const key = saleAttemptKey(body.lines);
    if (this.completedSale?.key === key) {
      await this.openReceipt(this.completedSale.id);
      return;
    }

    this.selling.set(true);
    try {
      const sale = await firstValueFrom(this.salesApi.complete(body));
      this.completedSale = { id: sale.id, key };
      await this.openReceipt(sale.id);
    } catch (error) {
      this.error.set(messageFrom(error));
    } finally {
      this.selling.set(false);
    }
  }

  private async loadShelves(): Promise<void> {
    const generation = ++this.shelfGeneration;
    this.loading.set(true);
    this.shelfError.set(false);
    this.error.set('');
    try {
      const shelves = await firstValueFrom(this.inventoryApi.listShelves());
      if (generation !== this.shelfGeneration) {
        return;
      }

      this.shelves.set(shelves);
    } catch (error) {
      if (generation !== this.shelfGeneration) {
        return;
      }

      this.shelves.set([]);
      this.shelfError.set(true);
      this.error.set(messageFrom(error));
    } finally {
      if (generation === this.shelfGeneration) {
        this.loading.set(false);
      }
    }
  }

  private async openReceipt(saleId: string): Promise<void> {
    const opened = await this.router.navigate(['/sales', saleId], { state: { success: 'Sale completed.' } });
    if (opened) {
      this.attempt.complete();
      this.lines.set([]);
    }
  }

  private buildBody(): CompleteSaleBody {
    const lines = this.lines().map((line) => ({
      productId: line.productId,
      shelfId: line.shelfId,
      quantity: parseSaleQuantity(line.quantity),
    }));
    if (lines.some((line) => line.shelfId.length === 0)) {
      throw new Error('Choose a shelf for every line.');
    }

    saleTotal(lines.map((line, index) => saleLineTotal(line.quantity, this.lines()[index].unitPrice)));
    return {
      operationId: this.attempt.idFor(saleAttemptKey(lines)),
      lines: lines.map((line) => ({
        productId: line.productId,
        shelfId: line.shelfId,
        quantity: line.quantity,
      })),
    };
  }

  private parsedLineTotal(line: CartLine): number {
    return saleLineTotal(parseSaleQuantity(line.quantity), line.unitPrice);
  }
}

function formatQuantity(value: number): string {
  return value.toFixed(3);
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : toApiError(error).message;
}
