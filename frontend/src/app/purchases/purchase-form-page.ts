import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { type Product } from '../core/catalog.models';
import { OperationAttempt } from '../core/inventory-rules';
import { type Shelf } from '../core/inventory.models';
import { InventoryApi } from '../core/inventory-api.service';
import { PurchaseApi } from '../core/purchase-api.service';
import {
  MAX_PURCHASE_LINES,
  formatMoney,
  parsePurchaseDate,
  parsePurchaseNotes,
  parsePurchaseQuantity,
  parsePurchaseReference,
  parseUnitCost,
  purchaseLineTotal,
  purchaseTotal,
} from '../core/purchase-rules';
import { type Purchase, type SavePurchaseBody } from '../core/purchase.models';
import { SessionService } from '../core/session.service';
import { ShopApi } from '../core/shop-api.service';
import { SupplierApi } from '../core/supplier-api.service';
import { type Supplier } from '../core/supplier.models';

interface DraftLine {
  key: string;
  productId: string;
  quantity: string;
  unitCost: string;
}

@Component({
  selector: 'app-purchase-form-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './purchase-form-page.html',
})
export class PurchaseFormPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly purchasesApi = inject(PurchaseApi);
  private readonly suppliersApi = inject(SupplierApi);
  private readonly shopApi = inject(ShopApi);
  private readonly inventoryApi = inject(InventoryApi);
  private readonly session = inject(SessionService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly attempt = new OperationAttempt();
  private lineKey = 0;

  protected readonly canRecord = this.session.canRecordPurchases;
  protected readonly canReceive = this.session.canReceivePurchases;
  protected readonly maxLines = MAX_PURCHASE_LINES;
  protected readonly loading = signal(true);
  protected readonly loadFailed = signal(false);
  protected readonly saving = signal(false);
  protected readonly receiving = signal(false);
  protected readonly finding = signal(false);
  protected readonly error = signal('');
  protected readonly success = signal('');
  protected readonly confirmDelete = signal(false);
  protected readonly purchase = signal<Purchase | null>(null);
  protected readonly suppliers = signal<Supplier[]>([]);
  protected readonly products = signal<Product[]>([]);
  protected readonly productsTruncated = signal(false);
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly lines = signal<DraftLine[]>([this.blankLine()]);
  protected readonly shelfChoices = signal<Record<string, string>>({});
  protected readonly productQuery = signal('');
  protected readonly form = this.formBuilder.nonNullable.group({
    supplierId: '',
    purchaseDate: today(),
    reference: '',
    notes: '',
  });

  constructor() {
    this.route.paramMap.subscribe(() => {
      void this.load();
    });
  }

  protected recorded(): boolean {
    return this.purchase()?.status === 'recorded';
  }

  protected received(): boolean {
    return this.purchase()?.status === 'received';
  }

  protected editable(): boolean {
    return this.canRecord() && !this.received() && !this.loading() && !this.loadFailed();
  }

  protected money(value: number): string {
    return formatMoney(value);
  }

  protected lineMoney(line: DraftLine): string {
    try {
      return formatMoney(purchaseLineTotal(parsePurchaseQuantity(line.quantity), parseUnitCost(line.unitCost)));
    } catch {
      return '—';
    }
  }

  protected documentTotal(): string {
    try {
      return formatMoney(purchaseTotal(this.lines().map((line) => this.parsedLineTotal(line))));
    } catch {
      return '—';
    }
  }

  protected shelfName(shelfId: string | null): string {
    if (!shelfId) {
      return 'Not received';
    }

    const shelf = this.shelves().find((item) => item.id === shelfId);
    return shelf ? `${shelf.areaName} / ${shelf.name}` : shelfId;
  }

  protected activeShelves(): Shelf[] {
    return this.shelves().filter((shelf) => shelf.isActive);
  }

  protected async findProducts(): Promise<void> {
    if (this.finding() || this.saving()) {
      return;
    }

    this.finding.set(true);
    this.error.set('');
    try {
      const products = await firstValueFrom(
        this.shopApi.listProducts({
          name: this.productQuery(),
          activeOnly: false,
          limit: 200,
          offset: 0,
        }),
      );
      this.products.set(this.withSelectedProducts(products));
      this.productsTruncated.set(products.length === 200);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.finding.set(false);
    }
  }

  protected addLine(): void {
    if (!this.editable() || this.lines().length >= MAX_PURCHASE_LINES) {
      return;
    }

    this.lines.update((lines) => [...lines, this.blankLine()]);
  }

  protected removeLine(key: string): void {
    if (!this.editable()) {
      return;
    }

    this.lines.update((lines) => lines.filter((line) => line.key !== key));
  }

  protected updateLine(key: string, field: 'productId' | 'quantity' | 'unitCost', value: string): void {
    this.lines.update((lines) => lines.map((line) => (line.key === key ? { ...line, [field]: value } : line)));
  }

  protected setShelf(lineId: string, value: string): void {
    this.shelfChoices.update((choices) => ({ ...choices, [lineId]: value }));
  }

  protected async save(): Promise<void> {
    if (!this.editable() || this.saving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    let body: SavePurchaseBody;
    try {
      body = this.buildBody();
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.saving.set(true);
    const existing = this.purchase();
    try {
      const saved = existing
        ? await firstValueFrom(this.purchasesApi.update(existing.id, body))
        : await firstValueFrom(this.purchasesApi.create(body));
      if (!existing) {
        await this.router.navigate(['/purchases', saved.id], { state: { success: 'Purchase recorded.' } });
        return;
      }

      this.purchase.set(saved);
      this.applyPurchase(saved);
      this.success.set('Purchase saved.');
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async receive(): Promise<void> {
    const current = this.purchase();
    if (!current || !this.recorded() || !this.canReceive() || this.receiving()) {
      return;
    }

    this.error.set('');
    this.success.set('');
    const choices = this.shelfChoices();
    if (current.lines.some((line) => !choices[line.id])) {
      this.error.set('Choose a shelf for every line.');
      return;
    }

    const key = current.lines.map((line) => `${line.id}|${choices[line.id]}`).join('|');
    const operationId = this.attempt.idFor(key);
    this.receiving.set(true);
    try {
      const saved = await firstValueFrom(
        this.purchasesApi.receive(current.id, {
          operationId,
          lines: current.lines.map((line) => ({ lineId: line.id, shelfId: choices[line.id] })),
        }),
      );
      this.attempt.complete();
      this.purchase.set(saved);
      this.applyPurchase(saved);
      this.success.set('Purchase received.');
      await this.refreshStock(saved);
    } catch (error) {
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.receiving.set(false);
    }
  }

  protected askDelete(): void {
    if (!this.editable() || !this.purchase()) {
      return;
    }

    this.confirmDelete.set(true);
  }

  protected cancelDelete(): void {
    this.confirmDelete.set(false);
  }

  protected async remove(): Promise<void> {
    const current = this.purchase();
    if (!current || !this.editable() || this.saving()) {
      return;
    }

    this.saving.set(true);
    this.error.set('');
    try {
      await firstValueFrom(this.purchasesApi.delete(current.id));
      await this.router.navigate(['/purchases'], { state: { success: 'Purchase removed.' } });
    } catch (error) {
      this.confirmDelete.set(false);
      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.saving.set(false);
    }
  }

  protected async load(): Promise<void> {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.error.set('');
    this.success.set('');
    this.confirmDelete.set(false);
    const message = readSuccess(this.router);
    const id = this.route.snapshot.paramMap.get('id');
    try {
      const [suppliers, products, shelves, purchase] = await Promise.all([
        firstValueFrom(this.suppliersApi.list({ name: '', activeOnly: !id, limit: 200, offset: 0 })),
        firstValueFrom(this.shopApi.listProducts({ name: '', activeOnly: false, limit: 200, offset: 0 })),
        firstValueFrom(this.inventoryApi.listShelves()),
        id ? firstValueFrom(this.purchasesApi.get(id)) : Promise.resolve(null),
      ]);
      this.suppliers.set(suppliers);
      this.products.set(products);
      this.productsTruncated.set(products.length === 200);
      this.shelves.set(shelves);
      this.purchase.set(purchase);
      if (purchase) {
        this.applyPurchase(purchase);
      } else {
        this.form.reset({ supplierId: '', purchaseDate: today(), reference: '', notes: '' });
        this.lines.set([this.blankLine()]);
        this.shelfChoices.set({});
      }

      if (this.canRecord() && purchase?.status !== 'received') {
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

  private applyPurchase(purchase: Purchase): void {
    this.form.setValue({
      supplierId: purchase.supplierId,
      purchaseDate: purchase.purchaseDate,
      reference: purchase.reference ?? '',
      notes: purchase.notes ?? '',
    });
    this.lines.set(
      purchase.lines.map((line) => ({
        key: line.id,
        productId: line.productId,
        quantity: formatMoneyQuantity(line.quantity),
        unitCost: formatMoney(line.unitCost),
      })),
    );
    this.products.update((products) => this.withSelectedProducts(products));
    const choices: Record<string, string> = {};
    for (const line of purchase.lines) {
      if (line.shelfId) {
        choices[line.id] = line.shelfId;
      }
    }
    this.shelfChoices.set(choices);
    if (purchase.status === 'received' || !this.canRecord()) {
      this.form.disable();
    } else {
      this.form.enable();
    }
  }

  private buildBody(): SavePurchaseBody {
    const raw = this.form.getRawValue();
    const lines = this.lines();
    if (lines.length === 0) {
      throw new Error('A purchase needs at least one line.');
    }

    if (!raw.supplierId) {
      throw new Error('Supplier is required.');
    }

    const parsed = lines.map((line) => {
      if (!line.productId) {
        throw new Error('Product is required.');
      }

      const quantity = parsePurchaseQuantity(line.quantity);
      const unitCost = parseUnitCost(line.unitCost);
      purchaseLineTotal(quantity, unitCost);
      return { productId: line.productId, quantity, unitCost };
    });
    purchaseTotal(parsed.map((line) => purchaseLineTotal(line.quantity, line.unitCost)));
    return {
      supplierId: raw.supplierId,
      purchaseDate: parsePurchaseDate(raw.purchaseDate),
      reference: parsePurchaseReference(raw.reference),
      notes: parsePurchaseNotes(raw.notes),
      lines: parsed,
    };
  }

  private parsedLineTotal(line: DraftLine): number {
    return purchaseLineTotal(parsePurchaseQuantity(line.quantity), parseUnitCost(line.unitCost));
  }

  private withSelectedProducts(products: Product[]): Product[] {
    const known = new Map(products.map((product) => [product.id, product]));
    for (const line of this.purchase()?.lines ?? []) {
      if (!known.has(line.productId)) {
        known.set(line.productId, {
          id: line.productId,
          name: line.productName,
          categoryId: null,
          categoryName: null,
          brandId: null,
          brandName: null,
          unitId: '',
          unitName: '',
          purchasePrice: line.unitCost,
          sellingPrice: 0,
          minimumStock: 0,
          isActive: true,
          barcodes: [],
          createdAt: '',
          updatedAt: '',
        });
      }
    }

    return [...known.values()];
  }

  private async refreshStock(purchase: Purchase): Promise<void> {
    try {
      await Promise.all(
        purchase.lines.map((line) =>
          firstValueFrom(this.inventoryApi.listBalances(line.productId, line.shelfId ?? undefined)),
        ),
      );
    } catch (error) {
      const message = error instanceof ApiError ? error.message : 'The request failed.';
      this.error.set(`Purchase received. Stock could not be refreshed. ${message}`);
    }
  }

  private blankLine(): DraftLine {
    this.lineKey += 1;
    return { key: `line-${this.lineKey}`, productId: '', quantity: '', unitCost: '' };
  }
}

function today(): string {
  const now = new Date();
  const month = String(now.getMonth() + 1).padStart(2, '0');
  const day = String(now.getDate()).padStart(2, '0');
  return `${now.getFullYear()}-${month}-${day}`;
}

function formatMoneyQuantity(value: number): string {
  return formatScaled(value, 3);
}

function readSuccess(router: Router): string {
  const candidates = [
    router.getCurrentNavigation()?.extras?.state,
    router.lastSuccessfulNavigation()?.extras?.state,
    history.state,
  ];
  for (const state of candidates) {
    if (state && typeof state === 'object' && 'success' in state && typeof state.success === 'string') {
      if (history.state && typeof history.state === 'object' && 'success' in history.state) {
        const next = { ...history.state };
        delete next.success;
        history.replaceState(next, '');
      }

      return state.success;
    }
  }

  return '';
}
