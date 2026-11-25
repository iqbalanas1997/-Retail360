import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { vi } from 'vitest';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('POS and sales screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('adds a scanned product and completes a sale without sending a price', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '1.250');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(text(harness)).toContain('35.00');
    expect(text(harness)).toContain('43.75');
    expect(text(harness)).toContain('100');
    expect(text(harness)).toContain('Available');
    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-1');
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const created = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.method === 'POST' && request.url === '/api/sales',
    );

    expect(created.request.body).toEqual({
      operationId: created.request.body.operationId,
      lines: [{ productId: 'product-1', shelfId: 'shelf-1', quantity: 1.25 }],
    });
    expect(created.request.body.operationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    expect(JSON.stringify(created.request.body)).not.toContain('unitPrice');
    expect(JSON.stringify(created.request.body)).not.toContain('total');
    created.flush(sale());
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(harness.routeNativeElement?.textContent).toContain('Receipt 1');
    expect(text(harness)).toContain('Sale completed.');
    expect((root.querySelector('[name="shelf"]') as HTMLSelectElement).value).toBe('shelf-1');
    expect((root.querySelector('[name="quantity"]') as HTMLInputElement).value).toBe('1');
    expect(document.activeElement).toBe(root.querySelector('[name="barcode"]'));
    expect(text(harness)).toContain('Front B');
    expect(text(harness)).toContain('2026-10-19 21:05:46 UTC');
    expect(text(harness)).toContain('No customer');
  });

  it('selects a customer for one sale and can clear that customer', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    clickButton(root, 'Add customer');
    harness.detectChanges();
    setNamed(root, 'customer', 'Amina');
    submitForm(root, 'Find customer');
    TestBed.inject(HttpTestingController)
      .expectOne(
        (request) =>
          request.method === 'GET' &&
          request.url === '/api/customers' &&
          request.params.get('search') === 'Amina' &&
          request.params.get('activeOnly') === 'true',
      )
      .flush([
        {
          id: 'customer-1',
          name: 'Amina',
          phone: '0300',
          email: 'amina@example.com',
          address: 'Lane 1',
          notes: 'private note',
          isActive: true,
          createdAt: '',
          updatedAt: '',
        },
      ]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Select');
    harness.detectChanges();

    expect(text(harness)).toContain('Amina · 0300');
    expect(text(harness)).not.toContain('private note');
    expect(text(harness)).not.toContain('Lane 1');

    clickButton(root, 'Clear customer');
    harness.detectChanges();
    expect(text(harness)).not.toContain('Amina · 0300');
    expect(text(harness)).toContain('Walk-in sale');

    clickButton(root, 'Add customer');
    harness.detectChanges();
    setNamed(root, 'customer', 'Amina');
    submitForm(root, 'Find customer');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/customers')
      .flush([
        {
          id: 'customer-1',
          name: 'Amina',
          phone: '0300',
          email: null,
          address: null,
          notes: null,
          isActive: true,
          createdAt: '',
          updatedAt: '',
        },
      ]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Select');
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const created = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.method === 'POST' && request.url === '/api/sales',
    );
    expect(created.request.body.customerId).toBe('customer-1');
    expect(created.request.body.lines).toEqual([
      { productId: 'product-1', shelfId: 'shelf-1', quantity: 1 },
    ]);
    expect(JSON.stringify(created.request.body)).not.toContain('unitPrice');
    const receipt = { ...sale(), customerId: 'customer-1', customerName: 'Amina' };
    created.flush(receipt);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Customer Amina');
    expect(text(harness)).toContain('cash');
    expect(text(harness)).not.toContain('private note');
  });

  it('reuses the operation id after an error and shows insufficient stock', async () => {
    remember('Admin');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'productName', 'Tea');
    submitForm(root, 'Search products');
    flushSearch('Tea', [product()]);
    flushShelfStock();
    await turn();
    harness.detectChanges();
    clickProduct(root);
    flushProductStock();
    await turn();
    harness.detectChanges();
    clickButton(root, 'Complete sale');
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    const operationId = first.request.body.operationId as string;
    clickButton(root, 'Complete sale');
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
    first.flush({ error: 'Shelf does not have enough stock.' }, { status: 409, statusText: 'Conflict' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Shelf does not have enough stock.');
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const retry = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(retry.request.body.operationId).toBe(operationId);
    retry.flush({ error: 'Product is inactive.' }, { status: 409, statusText: 'Conflict' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Product is inactive.');
    const quantity = root.querySelector('[name="lineQuantity"]') as HTMLInputElement;
    quantity.value = '2';
    quantity.dispatchEvent(new Event('input'));
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const changed = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(changed.request.body.operationId).not.toBe(operationId);
    expect(changed.request.body.lines).toEqual([{ productId: 'product-1', shelfId: 'shelf-1', quantity: 2 }]);
    changed.flush(sale());
    await turn();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/pos');
  });

  it('does not let inventory staff complete a sale', async () => {
    remember('Inventory Staff');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Inventory staff can read sales and cannot complete one.');
    expect(text(harness)).not.toContain('Complete sale');
  });

  it('lists sales and opens a receipt', async () => {
    remember('Inventory Staff');
    const harness = await open('/sales');

    expect(text(harness)).toContain('Loading sales…');
    TestBed.inject(HttpTestingController)
      .expectOne(
        (request) =>
          request.url === '/api/sales' &&
          request.params.get('limit') === '50' &&
          request.params.get('offset') === '0' &&
          request.params.get('receiptNumber') === null,
      )
      .flush([sale()]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('1');
    expect(text(harness)).toContain('35.00');
    expect(text(harness)).toContain('No customer');
    expect(text(harness)).toContain('2026-10-19 21:05:46 UTC');
    expect(text(harness)).not.toContain('New sale');
    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(root, 'receiptNumber', '1');
    clickButton(root, 'Search');
    TestBed.inject(HttpTestingController)
      .expectOne(
        (request) => request.url === '/api/sales' && request.params.get('receiptNumber') === '1',
      )
      .flush([sale()]);
    await turn();
    harness.detectChanges();
    const link = [...root.querySelectorAll('a')].find((item) => item.textContent?.trim() === 'Open') as HTMLAnchorElement;
    link.click();
    await turn();
    harness.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/sales/sale-1').flush(sale());
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Receipt 1');
    expect(text(harness)).toContain('Tea');
    expect(text(harness)).toContain('Front B');
    expect(text(harness)).toContain('Cash');
    expect(text(harness)).toContain('Print');
    const print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    clickButton(harness.fixture.nativeElement as HTMLElement, 'Print');
    expect(print).toHaveBeenCalledOnce();
    print.mockRestore();
  });

  it('keeps the newer product search and ignores a stale response', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'productName', 'Tea');
    submitForm(root, 'Search products');
    setNamed(root, 'productName', 'Rice');
    submitForm(root, 'Search products');
    const http = TestBed.inject(HttpTestingController);
    const tea = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Tea',
    );
    const rice = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Rice',
    );
    tea.flush([product()]);
    rice.flush([{ ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    flushBarcodeMiss(http, 'Tea');
    flushBarcodeMiss(http, 'Rice');
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Rice');
    expect(text(harness)).not.toContain('Tea');
    expect(text(harness)).toContain('2.00');
  });

  it('adds a scanned product that returns after a newer search without replacing that search', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    setNamed(root, 'productName', 'Rice');
    submitForm(root, 'Search products');
    const http = TestBed.inject(HttpTestingController);
    const barcode = http.expectOne((request) => request.url === '/api/products/by-barcode/TEA');
    const rice = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Rice',
    );
    rice.flush([{ ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    flushBarcodeMiss(http, 'Rice');
    flushShelfStock();
    barcode.flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Rice');
    expect(text(harness)).toContain('2.00');
    expect(text(harness)).toContain('Tea');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
  });

  it('adds a product from a barcode typed into search', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '2');
    setNamed(root, 'productName', 'TEA');
    submitForm(root, 'Search products');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products' && request.params.get('name') === 'TEA').flush([]);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    flushShelfStock();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Pack');
    clickProduct(root);
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('2.000');
    expect((root.querySelector('[name="quantity"]') as HTMLInputElement).value).toBe('1');
    expect((root.querySelector('[name="productName"]') as HTMLInputElement).value).toBe('');
  });

  it('adds the same product twice from browse and keeps the list open', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    clickButton(root, 'Browse products');
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne(
        (request) =>
          request.url === '/api/products' &&
          request.params.get('name') === null &&
          request.params.get('limit') === '20' &&
          request.params.get('offset') === '0',
      )
      .flush([product(), { ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    flushShelfStock('shelf-1', [{ productId: 'product-1', quantity: 4 }]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Rice');
    expect(root.querySelector('.pos-products')?.textContent).not.toContain('On shelf');
    const products = root.querySelector('.pos-products');
    const cart = root.querySelector('.pos-cart');
    const dialog = root.querySelector('[role="dialog"]');
    expect(products).not.toBeNull();
    expect(dialog?.getAttribute('aria-modal')).toBe('true');
    expect(cart?.contains(products)).toBe(false);
    expect(root.querySelector('.pos-stage-results')).toBeNull();
    expect([...root.querySelectorAll('.pos-products button')].some((button) => button.textContent?.trim() === 'Add')).toBe(
      false,
    );
    clickProduct(root);
    await turn();
    flushProductStock();
    await turn();
    http
      .expectOne((request) => request.url === '/api/products' && request.params.get('offset') === '0')
      .flush([product(), { ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    await turn();
    harness.detectChanges();
    clickProduct(root);
    await turn();
    http
      .expectOne((request) => request.url === '/api/products' && request.params.get('offset') === '0')
      .flush([product(), { ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('2.000');
    expect(text(harness)).toContain('Close browse');
    expect(root.querySelector('.pos-cart')?.contains(root.querySelector('.pos-products'))).toBe(false);
    clickButton(root, 'Close browse');
    harness.detectChanges();
    expect(root.querySelector('.pos-products')).toBeNull();
    expect(document.activeElement).toBe(root.querySelector('[name="barcode"]'));
  });

  it('searches while typing and adds the highlighted product with Enter', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'productName', 'Tea');
    await new Promise((resolve) => setTimeout(resolve, 300));
    flushSearch('Tea', [product(), { ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    flushShelfStock();
    await turn();
    harness.detectChanges();

    expect(root.querySelector('.pos-cart')?.contains(root.querySelector('.pos-products'))).toBe(false);
    const input = root.querySelector('[name="productName"]') as HTMLInputElement;
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowDown', bubbles: true }));
    harness.detectChanges();
    expect(root.querySelector('tr.pos-match')?.textContent).toContain('Tea');
    input.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    expect((root.querySelector('[name="productName"]') as HTMLInputElement).value).toBe('');
  });

  it('ignores a repeated barcode scan while the first lookup is running', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    const lookup = http.expectOne((request) => request.url === '/api/products/by-barcode/TEA');
    http.expectNone((request) => request.url === '/api/products/by-barcode/TEA');
    lookup.flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
  });

  it('clears the scan field after Enter and keeps a leading zero in the lookup', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', '00123');
    submitForm(root, 'Barcode');
    harness.detectChanges();

    const barcode = root.querySelector('[name="barcode"]') as HTMLInputElement;
    expect(barcode.value).toBe('');
    expect(document.activeElement).toBe(barcode);
    const lookup = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.url === '/api/products/by-barcode/00123',
    );
    lookup.flush({ ...product(), barcodes: ['00123'], sellingPrice: 35 });
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(text(harness)).toContain('35.00');
    expect(barcode.value).toBe('');
    expect(document.activeElement).toBe(barcode);
    TestBed.inject(HttpTestingController).expectNone(
      (request) => request.method === 'POST' && request.url === '/api/sales',
    );
  });

  it('shows an unknown barcode and retries after the lookup fails', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', '00999');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne((request) => request.url === '/api/products/by-barcode/00999')
      .flush({ error: 'Product was not found.' }, { status: 404, statusText: 'Not Found' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('No product has barcode 00999.');
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
  });

  it('adds both products when two different scans overlap', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    setNamed(root, 'barcode', 'RICE');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    const tea = http.expectOne((request) => request.url === '/api/products/by-barcode/TEA');
    const rice = http.expectOne((request) => request.url === '/api/products/by-barcode/RICE');
    tea.flush(product());
    rice.flush({ ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2, barcodes: ['RICE'] });
    await turn();
    flushProductStock('product-1');
    flushProductStock('product-2');
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(text(harness)).toContain('Rice');
    expect(text(harness)).toContain('37.00');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(2);
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
  });

  it('adds the next scan of the same shelf onto the existing line and refuses an inactive product', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('2.000');
    clickButton(root, '+');
    harness.detectChanges();
    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('3.000');
    setNamed(root, 'barcode', 'OLD');
    submitForm(root, 'Barcode');
    http
      .expectOne((request) => request.url === '/api/products/by-barcode/OLD')
      .flush({ ...product(), isActive: false });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Product is inactive.');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
  });

  it('rejects an invalid quantity and preserves the cart after a network failure', async () => {
    remember('Manager');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '1.2501');
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'productName', 'Tea');
    submitForm(root, 'Search products');
    flushSearch('Tea', [product()]);
    flushShelfStock();
    await turn();
    harness.detectChanges();
    clickProduct(root);
    harness.detectChanges();

    expect(text(harness)).toContain('Quantity must be greater than zero with at most 3 decimal places.');
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
    setNamed(root, 'quantity', '1.250');
    clickProduct(root);
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('43.75');
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const http = TestBed.inject(HttpTestingController);
    const failed = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    const operationId = failed.request.body.operationId as string;
    failed.error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).toContain('Tea');
    expect(root.querySelector('[name="lineQuantity"]')).not.toBeNull();
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const retry = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(retry.request.body.operationId).toBe(operationId);
    retry.flush(sale());
    await turn();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(text(harness)).toContain('Receipt 1');
  });

  it('retries a failed shelf load and shows an authorization error instead of an empty sale list', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/shelves')
      .error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    clickButton(harness.fixture.nativeElement as HTMLElement, 'Try again');
    flushShelves();
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Front B');

    await harness.navigateByUrl('/sales');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/sales')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect(text(harness)).not.toContain('No sales match this filter.');
  });

  it('keeps the cart when the saved sale cannot be opened and does not submit it again', async () => {
    remember('Admin');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'productName', 'Tea');
    submitForm(root, 'Search products');
    flushSearch('Tea', [product()]);
    flushShelfStock();
    await turn();
    harness.detectChanges();
    clickProduct(root);
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales').flush(sale());
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(text(harness)).toContain('Receipt 1');
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
    expect([...root.querySelectorAll('button')].some((button) => button.textContent?.trim() === 'Complete sale')).toBe(
      false,
    );
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
  });

  it('returns to sign-in when the session expires on the counter', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    writeStoredSession({
      accessToken: token('Cashier'),
      expiresAt: new Date(Date.now() - 1000).toISOString(),
      userId: 'user-1',
      userName: 'Cashier',
      roles: ['Cashier'],
    });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('sends a credit sale only after a customer is selected and records a payment apart from the cart', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;

    expect(root.querySelector('[name="salePayment"]')).toBeNull();
    expect(text(harness)).toContain('Walk-in sale');
    expect(document.activeElement).toBe(root.querySelector('[name="barcode"]'));

    clickButton(root, 'Add customer');
    harness.detectChanges();
    setNamed(root, 'customer', 'Amina');
    submitForm(root, 'Find customer');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/customers')
      .flush([
        {
          id: 'customer-1',
          name: 'Amina',
          phone: '0300',
          email: null,
          address: null,
          notes: null,
          isActive: true,
          createdAt: '',
          updatedAt: '',
          outstanding: 12.5,
        },
      ]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Select');
    harness.detectChanges();

    expect(text(harness)).toContain('Outstanding 12.50');
    setNamed(root, 'salePayment', 'credit');
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    setNamed(root, 'paymentAmount', '2.50');
    submitForm(root, 'Collect outstanding');
    const payment = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.method === 'POST' && request.url === '/api/customers/customer-1/payments',
    );
    expect(payment.request.body.amount).toBe(2.5);
    expect(payment.request.body.operationId).toEqual(expect.any(String));
    payment.flush({
      id: 'payment-1',
      customerId: 'customer-1',
      amount: 2.5,
      occurredAt: '2026-11-09T00:00:00Z',
      outstanding: 10,
      allocations: [{ saleId: 'sale-9', receiptNumber: 9, amount: 2.5 }],
    });
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Payment recorded.');
    expect(text(harness)).toContain('Outstanding 10.00');
    setNamed(root, 'paymentAmount', '10');
    submitForm(root, 'Collect outstanding');
    const exact = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.method === 'POST' && request.url === '/api/customers/customer-1/payments',
    );
    expect(exact.request.body.amount).toBe(10);
    exact.flush({
      id: 'payment-2',
      customerId: 'customer-1',
      amount: 10,
      occurredAt: '2026-11-09T00:00:00Z',
      outstanding: 0,
      allocations: [{ saleId: 'sale-9', receiptNumber: 9, amount: 10 }],
    });
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Outstanding 0.0');

    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    const created = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.method === 'POST' && request.url === '/api/sales',
    );
    expect(created.request.body.paymentMethod).toBe('credit');
    expect(created.request.body.customerId).toBe('customer-1');
    created.flush({ ...sale(), paymentMethod: 'credit', customerId: 'customer-1', customerName: 'Amina' });
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(text(harness)).toContain('credit');
    expect(text(harness)).toContain('Total 35.00');
    expect(text(harness)).not.toContain('Total 37.50');
  });

  it('resets the next quantity, keeps the shelf, and starts a new sale on the counter', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '2');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();

    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('2.000');
    expect((root.querySelector('[name="quantity"]') as HTMLInputElement).value).toBe('1');
    clickButton(root, 'Complete sale');
    flushProductStock();
    await turn();
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.method === 'POST' && request.url === '/api/sales')
      .flush(sale());
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect((root.querySelector('[name="shelf"]') as HTMLSelectElement).value).toBe('shelf-1');
    const openReceipt = [...root.querySelectorAll('a')].find((item) => item.textContent?.trim() === 'Open receipt');
    expect(openReceipt?.getAttribute('href')).toBe('/sales/sale-1');
    clickButton(root, 'New sale');
    harness.detectChanges();

    expect(text(harness)).not.toContain('Receipt 1');
    expect(text(harness)).toContain('Walk-in sale');
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
    expect((root.querySelector('[name="shelf"]') as HTMLSelectElement).value).toBe('shelf-1');
    expect((root.querySelector('[name="quantity"]') as HTMLInputElement).value).toBe('1');
    expect(document.activeElement).toBe(root.querySelector('[name="barcode"]'));
  });

  it('starts the next sale from a scan and keeps another shelf on its own line', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/shelves')
      .flush([
        {
          id: 'shelf-1',
          areaId: 'area-1',
          areaName: 'Shop Floor',
          name: 'Front B',
          isActive: true,
          createdAt: '2026-10-20T00:00:00Z',
          updatedAt: '2026-10-20T00:00:00Z',
        },
        {
          id: 'shelf-2',
          areaId: 'area-1',
          areaName: 'Shop Floor',
          name: 'Back',
          isActive: true,
          createdAt: '2026-10-20T00:00:00Z',
          updatedAt: '2026-10-20T00:00:00Z',
        },
      ]);
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-1', quantity: 100 },
      { shelfId: 'shelf-2', quantity: 100 },
    ]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Complete sale');
    flushProductStock('product-1', [
      { shelfId: 'shelf-1', quantity: 100 },
      { shelfId: 'shelf-2', quantity: 100 },
    ]);
    await turn();
    http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales').flush(sale());
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Receipt 1');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-1', quantity: 100 },
      { shelfId: 'shelf-2', quantity: 100 },
    ]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).not.toContain('Receipt 1');
    expect(text(harness)).toContain('Walk-in sale');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    setNamed(root, 'shelf', 'shelf-2');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(2);
    const shelves = [...root.querySelectorAll('[name="lineShelf"]')] as HTMLSelectElement[];
    expect(shelves.map((shelf) => shelf.value)).toEqual(['shelf-1', 'shelf-2']);
    root.querySelector('[name="quantity"]')?.dispatchEvent(
      new KeyboardEvent('keydown', { key: 'Enter', ctrlKey: true, bubbles: true, cancelable: true }),
    );
    flushProductStock('product-1', [
      { shelfId: 'shelf-1', quantity: 100 },
      { shelfId: 'shelf-2', quantity: 100 },
    ]);
    await turn();
    const created = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(created.request.body.lines).toEqual([
      { productId: 'product-1', shelfId: 'shelf-1', quantity: 1 },
      { productId: 'product-1', shelfId: 'shelf-2', quantity: 1 },
    ]);
    expect(created.request.body.customerId).toBeUndefined();
    created.flush(sale());
    await turn();
    harness.detectChanges();
  });

  it('asks for another shelf when the remembered shelf is gone', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'missing-shelf');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;

    expect(text(harness)).toContain('The selected shelf is no longer available. Choose another shelf.');
    expect((root.querySelector('[name="shelf"]') as HTMLSelectElement).value).toBe('');
    expect(sessionStorage.getItem('retail360.posShelf')).toBeNull();
  });
});

function open(url: string): Promise<RouterTestingHarness> {
  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  return RouterTestingHarness.create().then((harness) => harness.navigateByUrl(url).then(() => harness));
}

function remember(role: string): void {
  writeStoredSession({
    accessToken: token(role),
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    userId: 'user-1',
    userName: role,
    roles: [role],
  });
}

  it('shows shelf stock on every line and keeps the remembered shelf when it can cover the quantity', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'shelf-a');
    const harness = await open('/pos');
    flushNamedShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '5');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-a', quantity: 20 },
      { shelfId: 'shelf-b', quantity: 50 },
    ]);
    await turn();
    harness.detectChanges();

    const select = root.querySelector('[name="lineShelf"]') as HTMLSelectElement;
    expect(select.value).toBe('shelf-a');
    expect(text(harness)).toContain('20');
    expect(text(harness)).toContain('Available');
    expect(select.textContent).toContain('Shop Floor / Shelf A – 20 available (sufficient)');
    expect(select.textContent).toContain('Shop Floor / Shelf B – 50 available (sufficient)');
    expect(select.options[0].disabled).toBe(false);
    expect(select.options[1].disabled).toBe(false);
  });

  it('selects another shelf when the remembered shelf is short and blocks that shelf', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'shelf-a');
    const harness = await open('/pos');
    flushNamedShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '10');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-a', quantity: 5 },
      { shelfId: 'shelf-b', quantity: 15 },
    ]);
    await turn();
    harness.detectChanges();

    const select = root.querySelector('[name="lineShelf"]') as HTMLSelectElement;
    const options = [...select.options];
    expect(select.value).toBe('shelf-b');
    expect(text(harness)).toContain('Available');
    expect(options.find((option) => option.value === 'shelf-a')?.disabled).toBe(true);
    expect(options.find((option) => option.value === 'shelf-a')?.textContent).toContain('5 available (insufficient)');
    expect(options.find((option) => option.value === 'shelf-b')?.disabled).toBe(false);
    expect(options.find((option) => option.value === 'shelf-b')?.textContent).toContain('15 available (sufficient)');
    setNamed(root, 'lineShelf', 'shelf-a');
    harness.detectChanges();
    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-b');
  });

  it('updates eligibility when the quantity changes and says when no shelf can cover it', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'shelf-a');
    const harness = await open('/pos');
    flushNamedShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '5');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products/by-barcode/TEA')
      .flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-a', quantity: 10 },
      { shelfId: 'shelf-b', quantity: 20 },
    ]);
    await turn();
    harness.detectChanges();

    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-a');
    const quantity = root.querySelector('[name="lineQuantity"]') as HTMLInputElement;
    quantity.value = '25';
    quantity.dispatchEvent(new Event('input'));
    harness.detectChanges();

    expect(text(harness)).toContain('25 required. No single shelf has sufficient stock.');
    expect(text(harness)).toContain('Insufficient');
    expect((root.querySelector('.pos-complete') as HTMLButtonElement).disabled).toBe(true);
    const blocked = [...(root.querySelector('[name="lineShelf"]') as HTMLSelectElement).options];
    expect(blocked.filter((option) => option.value !== '').every((option) => option.disabled)).toBe(true);

    quantity.value = '15';
    quantity.dispatchEvent(new Event('input'));
    harness.detectChanges();

    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-b');
    expect(text(harness)).toContain('Available');
    expect((root.querySelector('.pos-complete') as HTMLButtonElement).disabled).toBe(false);
  });

  it('combines the same product on one shelf and frees that stock when the line is removed', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'shelf-a');
    const harness = await open('/pos');
    flushNamedShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '6');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock('product-1', [{ shelfId: 'shelf-a', quantity: 10 }]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Available');
    setNamed(root, 'quantity', '5');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    expect((root.querySelector('[name="lineQuantity"]') as HTMLInputElement).value).toBe('11.000');
    expect(text(harness)).toContain('11 required. No single shelf has sufficient stock.');
    expect(root.querySelector('.pos-stock-short')?.textContent).toContain('Insufficient');
    clickButton(root, 'Remove');
    harness.detectChanges();
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
  });

  it('moves a line onto a shelf that becomes free when another line is removed', async () => {
    remember('Cashier');
    sessionStorage.setItem('retail360.posShelf', 'shelf-a');
    const harness = await open('/pos');
    flushNamedShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'quantity', '15');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock('product-1', [
      { shelfId: 'shelf-a', quantity: 20 },
      { shelfId: 'shelf-b', quantity: 10 },
    ]);
    await turn();
    harness.detectChanges();
    setNamed(root, 'shelf', 'shelf-b');
    setNamed(root, 'quantity', '8');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(2);
    const second = root.querySelectorAll('[name="lineQuantity"]')[1] as HTMLInputElement;
    second.value = '12';
    second.dispatchEvent(new Event('input'));
    harness.detectChanges();
    expect(text(harness)).toContain('12 required. No single shelf has sufficient stock.');
    clickButton(root, 'Remove');
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-a');
    expect(text(harness)).toContain('Available');
  });

  it('names every product when stock changes before the sale is stored', async () => {
    remember('Cashier');
    const harness = await open('/pos');
    flushShelves();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '10');
    setNamed(root, 'barcode', 'TEA');
    submitForm(root, 'Barcode');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/products/by-barcode/TEA').flush(product());
    await turn();
    flushProductStock();
    await turn();
    harness.detectChanges();
    setNamed(root, 'shelf', 'shelf-1');
    setNamed(root, 'quantity', '4');
    setNamed(root, 'barcode', 'RICE');
    submitForm(root, 'Barcode');
    http
      .expectOne((request) => request.url === '/api/products/by-barcode/RICE')
      .flush({ ...product(), id: 'product-2', name: 'Basmati Rice', sellingPrice: 2, barcodes: ['RICE'] });
    await turn();
    flushProductStock('product-2');
    await turn();
    harness.detectChanges();
    clickButton(root, 'Complete sale');
    flushProductStock();
    flushProductStock('product-2');
    await turn();
    const created = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    created.flush(
      {
        error: 'Shelf does not have enough stock.',
        shortages: [
          {
            productId: 'product-1',
            productName: 'Tea',
            shelfId: 'shelf-1',
            shelfName: 'Shop Floor / Front B',
            required: 10,
            available: 6,
          },
          {
            productId: 'product-2',
            productName: 'Basmati Rice',
            shelfId: 'shelf-1',
            shelfName: 'Shop Floor / Front B',
            required: 4,
            available: 2,
          },
        ],
      },
      { status: 409, statusText: 'Conflict' },
    );
    await turn();
    flushProductStock('product-1', [{ shelfId: 'shelf-1', quantity: 6 }]);
    flushProductStock('product-2', [{ shelfId: 'shelf-1', quantity: 2 }]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Insufficient stock: Tea – Shop Floor / Front B. Required: 10, Available: 6.');
    expect(text(harness)).toContain(
      'Insufficient stock: Basmati Rice – Shop Floor / Front B. Required: 4, Available: 2.',
    );
    expect(text(harness)).not.toContain('Shelf does not have enough stock.');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(2);
    expect((root.querySelector('.pos-complete') as HTMLButtonElement).disabled).toBe(true);
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
  });

function token(role: string): string {
  const body = btoa(JSON.stringify({ role })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  return `header.${body}.signature`;
}

function product() {
  return {
    id: 'product-1',
    name: 'Tea',
    categoryId: null,
    categoryName: null,
    brandId: null,
    brandName: null,
    unitId: 'unit-1',
    unitName: 'Pack',
    purchasePrice: 20,
    sellingPrice: 35,
    minimumStock: 0,
    isActive: true,
    barcodes: ['TEA'],
    createdAt: '2026-10-20T00:00:00Z',
    updatedAt: '2026-10-20T00:00:00Z',
  };
}

function sale() {
  return {
    id: 'sale-1',
    receiptNumber: 1,
    soldAt: '2026-10-19T21:05:46.5421+00:00',
    paymentMethod: 'cash',
    total: 35,
    lines: [
      {
        id: 'line-1',
        productId: 'product-1',
        productName: 'Tea',
        shelfId: 'shelf-1',
        shelfName: 'Front B',
        quantity: 1,
        unitPrice: 35,
        lineTotal: 35,
      },
    ],
    createdAt: '2026-10-19T21:05:46.5421+00:00',
    updatedAt: '2026-10-19T21:05:46.5421+00:00',
  };
}

function flushSearch(name: string, products: unknown[]): void {
  const http = TestBed.inject(HttpTestingController);
  http
    .expectOne((request) => request.url === '/api/products' && request.params.get('name') === name)
    .flush(products);
  flushBarcodeMiss(http, name);
}

function flushBarcodeMiss(http: HttpTestingController, barcode: string): void {
  http
    .expectOne((request) => request.url === `/api/products/by-barcode/${encodeURIComponent(barcode)}`)
    .flush({ error: 'No product has barcode.' }, { status: 404, statusText: 'Not Found' });
}

function flushProductStock(
  productId = 'product-1',
  rows: { shelfId: string; quantity: number }[] = [{ shelfId: 'shelf-1', quantity: 100 }],
): void {
  TestBed.inject(HttpTestingController)
    .expectOne(
      (request) => request.url === '/api/inventory/balances' && request.params.get('productId') === productId,
    )
    .flush(
      rows.map((row) => ({
        productId,
        productName: 'Tea',
        shelfId: row.shelfId,
        shelfName: row.shelfId,
        areaId: 'area-1',
        areaName: 'Shop Floor',
        quantity: row.quantity,
        shelfIsActive: true,
      })),
    );
}

function flushShelfStock(shelfId = 'shelf-1', rows: unknown[] = []): void {
  TestBed.inject(HttpTestingController)
    .expectOne(
      (request) => request.url === '/api/inventory/balances' && request.params.get('shelfId') === shelfId,
    )
    .flush(rows);
}

function flushNamedShelves(): void {
  TestBed.inject(HttpTestingController)
    .expectOne((request) => request.url === '/api/shelves')
    .flush([
      {
        id: 'shelf-a',
        areaId: 'area-1',
        areaName: 'Shop Floor',
        name: 'Shelf A',
        isActive: true,
        createdAt: '2026-10-20T00:00:00Z',
        updatedAt: '2026-10-20T00:00:00Z',
      },
      {
        id: 'shelf-b',
        areaId: 'area-1',
        areaName: 'Shop Floor',
        name: 'Shelf B',
        isActive: true,
        createdAt: '2026-10-20T00:00:00Z',
        updatedAt: '2026-10-20T00:00:00Z',
      },
    ]);
}

function flushShelves(): void {
  TestBed.inject(HttpTestingController)
    .expectOne((request) => request.url === '/api/shelves')
    .flush([
      {
        id: 'shelf-1',
        areaId: 'area-1',
        areaName: 'Shop Floor',
        name: 'Front B',
        isActive: true,
        createdAt: '2026-10-20T00:00:00Z',
        updatedAt: '2026-10-20T00:00:00Z',
      },
    ]);
}

function setNamed(root: HTMLElement, name: string, value: string): void {
  const input = root.querySelector(`[name="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('change'));
}

function setControl(root: HTMLElement, name: string, value: string): void {
  const input = root.querySelector(`[formcontrolname="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function submitForm(root: HTMLElement, legend: string): void {
  const form = [...root.querySelectorAll('form')].find((item) => item.textContent?.includes(legend)) as HTMLFormElement;
  form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
}

function clickButton(root: HTMLElement, label: string): void {
  const button = [...root.querySelectorAll('button')].find((item) => item.textContent?.trim() === label) as HTMLButtonElement;
  button.click();
}

function clickProduct(root: HTMLElement): void {
  const row = root.querySelector('.pos-products tbody tr') as HTMLElement;
  row.click();
}

function text(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
