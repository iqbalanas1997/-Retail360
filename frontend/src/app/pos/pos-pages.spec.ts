import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
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
  const body = btoa(JSON.stringify({ role })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  writeStoredSession({
    accessToken: `header.${body}.signature`,
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    userId: 'user-1',
    userName: role,
    roles: [role],
  });
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
