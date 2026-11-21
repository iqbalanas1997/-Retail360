import { AfterViewInit, ChangeDetectorRef, Component, ElementRef, HostListener, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError, toApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { type Product } from '../core/catalog.models';
import { OperationAttempt } from '../core/inventory-rules';
import { InventoryApi } from '../core/inventory-api.service';
import { type Shelf } from '../core/inventory.models';
import { CustomerApi } from '../core/customer-api.service';
import { type Customer } from '../core/customer.models';
import { parseCustomerPayment } from '../core/customer-rules';
import { formatMoney } from '../core/purchase-rules';
import { SaleApi } from '../core/sale-api.service';
import {
  MAX_SALE_LINES,
  parseSaleQuantity,
  saleAttemptKey,
  saleLineTotal,
  saleTotal,
  formatSoldAt,
} from '../core/sale-rules';
import { type CompleteSaleBody, type Sale } from '../core/sale.models';
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
export class PosPage implements AfterViewInit {
  private readonly shopApi = inject(ShopApi);
  private readonly inventoryApi = inject(InventoryApi);
  private readonly salesApi = inject(SaleApi);
  private readonly customersApi = inject(CustomerApi);
  private readonly session = inject(SessionService);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly attempt = new OperationAttempt();
  private readonly paymentAttempt = new OperationAttempt();
  private readonly barcodeInput = viewChild<ElementRef<HTMLInputElement>>('barcodeInput');
  private readonly quantityInput = viewChild<ElementRef<HTMLInputElement>>('quantityInput');
  private readonly productInput = viewChild<ElementRef<HTMLInputElement>>('productInput');
  private readonly shelfInput = viewChild<ElementRef<HTMLSelectElement>>('shelfInput');
  private lineKey = 0;
  private lookupGeneration = 0;
  private shelfGeneration = 0;
  private customerGeneration = 0;
  private barcodeInFlight = '';
  private searchTimer: ReturnType<typeof setTimeout> | null = null;
  private stockShelfId = '';

  protected readonly canSell = this.session.canCompleteSales;
  protected readonly loading = signal(true);
  protected readonly finding = signal(false);
  protected readonly selling = signal(false);
  protected readonly shelfError = signal(false);
  protected readonly error = signal('');
  protected readonly shelves = signal<Shelf[]>([]);
  protected readonly matches = signal<Product[]>([]);
  protected readonly matchIndex = signal(-1);
  protected readonly lines = signal<CartLine[]>([]);
  protected readonly barcode = signal('');
  protected readonly productQuery = signal('');
  protected readonly browseOpen = signal(false);
  protected readonly browseRows = signal<Product[]>([]);
  protected readonly browseMore = signal(false);
  protected readonly shelfStock = signal<Record<string, number> | null>(null);
  private browseOffset = 0;
  protected readonly quantity = signal('1');
  protected readonly shelfId = signal('');
  protected readonly customerQuery = signal('');
  protected readonly findingCustomer = signal(false);
  protected readonly customerError = signal('');
  protected readonly customerMatches = signal<Customer[]>([]);
  protected readonly customer = signal<Customer | null>(null);
  protected readonly paymentMethod = signal<'cash' | 'credit'>('cash');
  protected readonly outstanding = signal(0);
  protected readonly paymentAmount = signal('');
  protected readonly paying = signal(false);
  protected readonly paymentNotice = signal('');
  protected readonly customerOpen = signal(false);
  protected readonly receipt = signal<Sale | null>(null);

  constructor() {
    void this.loadShelves();
  }

  ngAfterViewInit(): void {
    this.focusEntry();
  }

  @HostListener('keydown', ['$event'])
  protected onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      this.matches.set([]);
      this.matchIndex.set(-1);
      this.browseOpen.set(false);
      if (!this.customer()) {
        this.customerOpen.set(false);
      }
      this.focusBarcode();
      event.preventDefault();
      return;
    }

    if (event.key !== 'Enter' || (!event.ctrlKey && !event.metaKey)) {
      return;
    }

    const name = event.target instanceof HTMLElement ? event.target.getAttribute('name') : '';
    if (name === 'barcode') {
      event.preventDefault();
      return;
    }

    event.preventDefault();
    void this.complete();
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
    this.barcode.set('');
    this.focusBarcode();
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

      this.addProduct(product, current, 'scan');
    } catch (error) {
      if (generation !== this.lookupGeneration) {
        return;
      }

      this.matches.set([]);
      this.error.set(barcodeMessage(barcode, error));
    } finally {
      if (this.barcodeInFlight === barcode) {
        this.barcodeInFlight = '';
      }

      if (generation === this.lookupGeneration) {
        this.finding.set(false);
        this.focusBarcode();
      }
    }
  }

  private resetQuantity(): void {
    this.quantity.set('1');
    const input = this.quantityInput()?.nativeElement;
    if (input) {
      input.value = '1';
    }
  }

  private clearProductQuery(): void {
    this.productQuery.set('');
    const input = this.productInput()?.nativeElement;
    if (input) {
      input.value = '';
    }
  }

  private focusProduct(): void {
    this.productInput()?.nativeElement.focus();
  }

  private focusBarcode(): void {
    const input = this.barcodeInput()?.nativeElement;
    if (!input) {
      return;
    }

    if (this.barcode() === '') {
      input.value = '';
    }

    input.focus();
  }

  protected onProductInput(value: string): void {
    this.productQuery.set(value);
    this.matchIndex.set(-1);
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }

    const query = value.trim();
    if (!query) {
      this.matches.set([]);
      if (this.browseOpen()) {
        void this.loadBrowse(0);
      }
      return;
    }

    this.searchTimer = setTimeout(() => void this.searchProducts(), 250);
  }

  protected async searchProducts(): Promise<void> {
    if (this.searchTimer) {
      clearTimeout(this.searchTimer);
      this.searchTimer = null;
    }

    if (this.selling() || !this.session.isSignedIn()) {
      return;
    }

    const query = this.productQuery().trim();
    if (!query) {
      this.matches.set([]);
      if (this.browseOpen()) {
        void this.loadBrowse(0);
      }
      return;
    }

    const generation = ++this.lookupGeneration;
    this.finding.set(true);
    this.error.set('');
    void this.ensureShelfStock();

    try {
      const [listed, barcode] = await Promise.all([
        firstValueFrom(
          this.shopApi.listProducts({
            name: query,
            activeOnly: true,
            limit: this.browseOpen() ? 20 : 8,
            offset: 0,
          }),
        ),
        this.lookupBarcode(query),
      ]);
      if (generation !== this.lookupGeneration) {
        return;
      }

      const products = listed.filter((product) => product.isActive);
      const found = barcode.product;
      if (found?.isActive && !products.some((product) => product.id === found.id)) {
        products.unshift(found);
      }

      this.matchIndex.set(-1);
      this.matches.set(products);
      if (barcode.inactive || (found && !found.isActive)) {
        this.error.set('Product is inactive.');
      } else if (products.length === 0) {
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

  protected toggleBrowse(): void {
    const open = !this.browseOpen();
    this.browseOpen.set(open);
    if (!open) {
      return;
    }

    void this.ensureShelfStock();
    if (this.productQuery().trim()) {
      void this.searchProducts();
      return;
    }

    void this.loadBrowse(0);
  }

  protected showMoreProducts(): void {
    void this.loadBrowse(this.browseOffset + 20);
  }

  protected visibleProducts(): Product[] {
    if (this.productQuery().trim()) {
      return this.matches();
    }

    return this.browseOpen() ? this.browseRows() : this.matches();
  }

  protected stockLabel(productId: string): string {
    const stock = this.shelfStock();
    if (!this.shelfId() || !stock) {
      return '—';
    }

    return formatScaled(stock[productId] ?? 0, 3);
  }

  protected onProductKey(event: KeyboardEvent): void {
    const matches = this.visibleProducts();
    if ((event.key === 'ArrowDown' || event.key === 'ArrowUp') && matches.length > 0) {
      event.preventDefault();
      const current = this.matchIndex();
      const next =
        event.key === 'ArrowDown'
          ? (current + 1) % matches.length
          : current <= 0
            ? matches.length - 1
            : current - 1;
      this.matchIndex.set(next);
      return;
    }

    if (event.key === 'Enter' && this.matchIndex() >= 0) {
      event.preventDefault();
      event.stopPropagation();
      const product = matches[this.matchIndex()];
      if (product) {
        this.addProduct(product);
      }
    }
  }

  private async lookupBarcode(query: string): Promise<{ product: Product | null; inactive: boolean }> {
    try {
      return { product: await firstValueFrom(this.shopApi.findProductByBarcode(query)), inactive: false };
    } catch (error) {
      const failure = toApiError(error);
      if (failure.status === 404) {
        return { product: null, inactive: false };
      }

      if (failure.status === 409) {
        return { product: null, inactive: true };
      }

      throw error;
    }
  }

  private async loadBrowse(offset: number): Promise<void> {
    if (this.selling() || !this.session.isSignedIn() || this.productQuery().trim()) {
      return;
    }

    const generation = ++this.lookupGeneration;
    this.finding.set(true);
    this.error.set('');
    try {
      const products = await firstValueFrom(
        this.shopApi.listProducts({
          name: '',
          activeOnly: true,
          limit: 20,
          offset,
        }),
      );
      if (generation !== this.lookupGeneration) {
        return;
      }

      const page = products.filter((product) => product.isActive);
      this.browseOffset = offset;
      this.browseMore.set(page.length === 20);
      this.browseRows.set(offset === 0 ? page : [...this.browseRows(), ...page]);
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

  private async ensureShelfStock(): Promise<void> {
    const shelfId = this.shelfId();
    if (!shelfId || this.stockShelfId === shelfId) {
      return;
    }

    this.stockShelfId = shelfId;
    try {
      const rows = await firstValueFrom(this.inventoryApi.listBalances(undefined, shelfId));
      if (this.shelfId() !== shelfId) {
        return;
      }

      const stock: Record<string, number> = {};
      for (const row of rows) {
        stock[row.productId] = row.quantity;
      }
      this.shelfStock.set(stock);
    } catch (error) {
      if (this.shelfId() === shelfId) {
        this.stockShelfId = '';
        this.error.set(messageFrom(error));
      }
    }
  }

  protected onLineKey(event: KeyboardEvent): void {
    if (event.key !== 'Enter') {
      return;
    }

    event.preventDefault();
    this.focusBarcode();
  }

  protected addProduct(product: Product, report = true, source: 'scan' | 'pick' = 'pick'): void {
    if (this.selling() || !product.isActive) {
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

    const shelfId = this.shelfId();
    const existing = this.lines().find((line) => line.productId === product.id && line.shelfId === shelfId);
    if (!existing && this.lines().length >= MAX_SALE_LINES) {
      if (report) {
        this.error.set(`A sale can have at most ${MAX_SALE_LINES} lines.`);
      }

      return;
    }

    let nextQuantity = formatQuantity(quantity);
    if (existing) {
      try {
        nextQuantity = addQuantities(parseSaleQuantity(existing.quantity), quantity);
      } catch (error) {
        if (report) {
          this.error.set(error instanceof Error ? error.message : 'The request failed.');
        }

        return;
      }
    }

    if (this.receipt()) {
      this.receipt.set(null);
      this.clearCustomer();
    }

    if (report) {
      this.error.set('');
      this.matches.set([]);
      this.matchIndex.set(-1);
      if (source === 'scan' && this.browseOpen()) {
        this.clearProductQuery();
        void this.loadBrowse(0);
      }
    }
    if (existing) {
      this.lines.update((lines) =>
        lines.map((line) => (line.key === existing.key ? { ...line, quantity: nextQuantity } : line)),
      );
    } else {
      this.lines.update((lines) => [
        ...lines,
        {
          key: String(++this.lineKey),
          productId: product.id,
          productName: product.name,
          unitPrice: product.sellingPrice,
          quantity: nextQuantity,
          shelfId,
        },
      ]);
    }
    this.resetQuantity();
    if (report && source === 'pick') {
      this.clearProductQuery();
      if (this.browseOpen()) {
        void this.loadBrowse(0);
        this.focusProduct();
        return;
      }
    }

    this.focusBarcode();
  }

  protected rememberShelf(value: string): void {
    if (value !== this.shelfId()) {
      this.stockShelfId = '';
      this.shelfStock.set(null);
    }

    this.shelfId.set(value);
    if (value) {
      sessionStorage.setItem(shelfStorageKey, value);
      if (this.browseOpen()) {
        void this.ensureShelfStock();
      }
      this.focusBarcode();
      return;
    }

    sessionStorage.removeItem(shelfStorageKey);
  }

  protected updateQuantity(key: string, value: string): void {
    this.lines.update((lines) => lines.map((line) => (line.key === key ? { ...line, quantity: value } : line)));
  }

  protected updateShelf(key: string, value: string): void {
    const current = this.lines().find((line) => line.key === key);
    if (!current || current.shelfId === value) {
      return;
    }

    const other = this.lines().find(
      (line) => line.key !== key && line.productId === current.productId && line.shelfId === value && value !== '',
    );
    if (!other) {
      this.lines.update((lines) => lines.map((line) => (line.key === key ? { ...line, shelfId: value } : line)));
      return;
    }

    let quantity: string;
    try {
      quantity = addQuantities(parseSaleQuantity(other.quantity), parseSaleQuantity(current.quantity));
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    this.error.set('');
    this.lines.update((lines) =>
      lines
        .filter((line) => line.key !== key)
        .map((line) => (line.key === other.key ? { ...line, quantity } : line)),
    );
  }

  protected stepQuantity(key: string, direction: 1 | -1): void {
    if (this.selling()) {
      return;
    }

    const line = this.lines().find((item) => item.key === key);
    if (!line) {
      return;
    }

    let quantity: number;
    try {
      quantity = parseSaleQuantity(line.quantity);
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    const thousandths = Math.round(quantity * 1000) + direction * 1000;
    if (thousandths <= 0) {
      this.removeLine(key);
      this.focusBarcode();
      return;
    }

    try {
      this.updateQuantity(key, addQuantities(quantity, direction));
      this.error.set('');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
    }
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

  protected async findCustomer(): Promise<void> {
    if (this.selling()) {
      return;
    }

    const search = this.customerQuery().trim();
    this.customerError.set('');
    this.customerMatches.set([]);
    if (!search) {
      this.customerError.set('Type a name or phone.');
      return;
    }

    const generation = ++this.customerGeneration;
    this.findingCustomer.set(true);
    try {
      const customers = await firstValueFrom(
        this.customersApi.list({
          search,
          activeOnly: true,
          limit: 8,
          offset: 0,
        }),
      );
      if (generation !== this.customerGeneration) {
        return;
      }

      this.customerMatches.set(customers);
      if (customers.length === 0) {
        this.customerError.set('No active customer matches that search.');
      }
    } catch (error) {
      if (generation !== this.customerGeneration) {
        return;
      }

      this.customerError.set(messageFrom(error));
    } finally {
      if (generation === this.customerGeneration) {
        this.findingCustomer.set(false);
      }
    }
  }

  protected chooseCustomer(customer: Customer): void {
    if (this.selling() || !customer.isActive) {
      return;
    }

    this.customer.set(customer);
    this.outstanding.set(customer.outstanding ?? 0);
    this.customerMatches.set([]);
    this.customerError.set('');
    this.customerQuery.set('');
    this.paymentNotice.set('');
  }

  protected clearCustomer(): void {
    if (this.selling()) {
      return;
    }

    this.customer.set(null);
    this.customerMatches.set([]);
    this.customerError.set('');
    this.paymentMethod.set('cash');
    this.outstanding.set(0);
    this.paymentAmount.set('');
    this.paymentNotice.set('');
    this.customerOpen.set(false);
  }

  protected async recordPayment(): Promise<void> {
    const customer = this.customer();
    if (!customer || !this.canSell() || this.paying() || this.selling() || !this.session.isSignedIn()) {
      return;
    }

    this.paymentNotice.set('');
    this.error.set('');
    let amount: number;
    try {
      amount = parseCustomerPayment(this.paymentAmount());
    } catch (error) {
      this.paymentNotice.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    const operationId = this.paymentAttempt.idFor(`${customer.id}|${amount.toFixed(2)}`);
    this.paying.set(true);
    try {
      const payment = await firstValueFrom(
        this.customersApi.recordPayment(customer.id, { operationId, amount }),
      );
      this.paymentAttempt.complete();
      this.outstanding.set(payment.outstanding);
      this.paymentAmount.set('');
      this.paymentNotice.set('Payment recorded.');
    } catch (error) {
      this.paymentNotice.set(messageFrom(error));
    } finally {
      this.paying.set(false);
    }
  }

  protected customerLabel(customer: Customer): string {
    return customer.phone ? `${customer.name} · ${customer.phone}` : customer.name;
  }

  protected soldAt(sale: Sale): string {
    return formatSoldAt(sale.soldAt);
  }

  protected receiptCustomer(sale: Sale): string {
    const name = sale.customerName?.trim();
    return name ? `Customer ${name}` : 'No customer';
  }

  protected newSale(): void {
    if (this.selling() || this.paying()) {
      return;
    }

    this.receipt.set(null);
    this.lines.set([]);
    this.matches.set([]);
    this.barcode.set('');
    this.clearProductQuery();
    this.resetQuantity();
    this.error.set('');
    this.clearCustomer();
    this.focusBarcode();
  }

  protected printReceipt(): void {
    window.print();
  }

  protected async complete(): Promise<void> {
    if (
      !this.canSell() ||
      this.selling() ||
      this.paying() ||
      this.lines().length === 0 ||
      !this.session.isSignedIn()
    ) {
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

    this.selling.set(true);
    try {
      const sale = await firstValueFrom(this.salesApi.complete(body));
      this.attempt.complete();
      this.receipt.set(sale);
      this.lines.set([]);
      this.matches.set([]);
      this.barcode.set('');
      this.resetQuantity();
    } catch (error) {
      this.error.set(messageFrom(error));
    } finally {
      this.selling.set(false);
      this.changes.detectChanges();
      this.focusBarcode();
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
      this.restoreShelf(shelves);
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
        this.focusEntry();
      }
    }
  }

  private focusEntry(): void {
    if (this.selling()) {
      return;
    }

    if (!this.shelfId()) {
      const shelf = this.shelfInput()?.nativeElement;
      if (shelf) {
        shelf.focus();
        return;
      }
    }

    this.focusBarcode();
  }

  private restoreShelf(shelves: Shelf[]): void {
    const saved = this.shelfId() || sessionStorage.getItem(shelfStorageKey) || '';
    if (!saved) {
      return;
    }

    if (shelves.some((shelf) => shelf.id === saved)) {
      this.shelfId.set(saved);
      return;
    }

    this.shelfId.set('');
    sessionStorage.removeItem(shelfStorageKey);
    this.error.set('The selected shelf is no longer available. Choose another shelf.');
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
    const customer = this.customer();
    const paymentMethod = customer && this.paymentMethod() === 'credit' ? 'credit' : 'cash';
    if (paymentMethod === 'credit' && !customer) {
      throw new Error('A credit sale needs a customer.');
    }

    const body: CompleteSaleBody = {
      operationId: this.attempt.idFor(saleAttemptKey(lines, customer?.id ?? '', paymentMethod)),
      lines: lines.map((line) => ({
        productId: line.productId,
        shelfId: line.shelfId,
        quantity: line.quantity,
      })),
    };
    if (customer) {
      body.customerId = customer.id;
    }

    if (paymentMethod === 'credit') {
      body.paymentMethod = 'credit';
    }

    return body;
  }

  private parsedLineTotal(line: CartLine): number {
    return saleLineTotal(parseSaleQuantity(line.quantity), line.unitPrice);
  }
}

const shelfStorageKey = 'retail360.posShelf';

function formatQuantity(value: number): string {
  return value.toFixed(3);
}

function addQuantities(left: number, right: number): string {
  const thousandths = Math.round(left * 1000) + Math.round(right * 1000);
  return parseSaleQuantity((thousandths / 1000).toFixed(3)).toFixed(3);
}

function barcodeMessage(barcode: string, error: unknown): string {
  const failure = toApiError(error);
  if (failure.status === 404) {
    return `No product has barcode ${barcode}.`;
  }

  return failure.message;
}

function messageFrom(error: unknown): string {
  return error instanceof ApiError ? error.message : toApiError(error).message;
}
