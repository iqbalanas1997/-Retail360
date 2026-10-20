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
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(text(harness)).toContain('35.00');
    expect(text(harness)).toContain('43.75');
    expect((root.querySelector('[name="lineShelf"]') as HTMLSelectElement).value).toBe('shelf-1');
    clickButton(root, 'Complete sale');
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
    TestBed.inject(HttpTestingController).expectOne('/api/sales/sale-1').flush(sale());
    await turn();
    harness.detectChanges();

    expect(harness.routeNativeElement?.textContent).toContain('Receipt 1');
    expect(text(harness)).toContain('Sale completed.');
    expect(text(harness)).toContain('Front B');
    expect(text(harness)).toContain('2026-10-19 21:05:46 UTC');
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
    submitForm(root, 'Product');
    TestBed.inject(HttpTestingController)
      .expectOne(
        (request) =>
          request.url === '/api/products' &&
          request.params.get('name') === 'Tea' &&
          request.params.get('activeOnly') === 'true',
      )
      .flush([product()]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Add');
    harness.detectChanges();
    clickButton(root, 'Complete sale');
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
    const changed = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(changed.request.body.operationId).not.toBe(operationId);
    expect(changed.request.body.lines).toEqual([{ productId: 'product-1', shelfId: 'shelf-1', quantity: 2 }]);
    changed.flush(sale());
    await turn();
    harness.detectChanges();
    http.expectOne('/api/sales/sale-1').flush(sale());
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
    expect(text(harness)).toContain('cash');
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
    submitForm(root, 'Product');
    setNamed(root, 'productName', 'Rice');
    submitForm(root, 'Product');
    const http = TestBed.inject(HttpTestingController);
    const tea = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Tea',
    );
    const rice = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Rice',
    );
    tea.flush([product()]);
    rice.flush([{ ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
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
    submitForm(root, 'Product');
    const http = TestBed.inject(HttpTestingController);
    const barcode = http.expectOne((request) => request.url === '/api/products/by-barcode/TEA');
    const rice = http.expectOne(
      (request) => request.url === '/api/products' && request.params.get('name') === 'Rice',
    );
    rice.flush([{ ...product(), id: 'product-2', name: 'Rice', sellingPrice: 2 }]);
    barcode.flush(product());
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Rice');
    expect(text(harness)).toContain('2.00');
    expect(text(harness)).toContain('Tea');
    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
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
    harness.detectChanges();

    expect(root.querySelectorAll('[name="lineQuantity"]').length).toBe(1);
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
    submitForm(root, 'Product');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products')
      .flush([product()]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Add');
    harness.detectChanges();

    expect(text(harness)).toContain('Quantity must be greater than zero with at most 3 decimal places.');
    expect(root.querySelector('[name="lineQuantity"]')).toBeNull();
    setNamed(root, 'quantity', '1.250');
    clickButton(root, 'Add');
    harness.detectChanges();
    expect(text(harness)).toContain('43.75');
    clickButton(root, 'Complete sale');
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
    const retry = http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(retry.request.body.operationId).toBe(operationId);
    retry.flush(sale());
    await turn();
    harness.detectChanges();
    http.expectOne('/api/sales/sale-1').flush(sale());
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
    submitForm(root, 'Product');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/products')
      .flush([product()]);
    await turn();
    harness.detectChanges();
    clickButton(root, 'Add');
    harness.detectChanges();
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(false);
    clickButton(root, 'Complete sale');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.method === 'POST' && request.url === '/api/sales').flush(sale());
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Tea');
    expect(root.querySelector('[name="lineQuantity"]')).not.toBeNull();
    clickButton(root, 'Complete sale');
    await turn();
    harness.detectChanges();
    http.expectNone((request) => request.method === 'POST' && request.url === '/api/sales');
    expect(navigate).toHaveBeenCalledTimes(2);
    navigate.mockRestore();
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

function text(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
