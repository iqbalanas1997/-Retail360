import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('purchase screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows a purchase row after loading', async () => {
    remember('Cashier');
    const harness = await open();

    expect(text(harness)).toContain('Loading purchases…');
    flushList([purchase()]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('INV-9');
    expect(text(harness)).toContain('Mill');
    expect(text(harness)).toContain('2026-10-20');
    expect(text(harness)).toContain('0.13');
    expect(text(harness)).toContain('Recorded');
    expect(text(harness)).not.toContain('New purchase');
  });

  it('filters by supplier and status and pages on the server', async () => {
    remember('Admin');
    const harness = await open();
    flushList([purchase()]);
    await turn();
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(root, 'supplierId', 'supplier-1');
    setControl(root, 'status', 'received');
    clickButton(root, 'Search');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/suppliers').flush([supplier()]);
    http
      .expectOne(
        (request) =>
          request.url === '/api/purchases' &&
          request.params.get('supplierId') === 'supplier-1' &&
          request.params.get('status') === 'received' &&
          request.params.get('limit') === '50' &&
          request.params.get('offset') === '0',
      )
      .flush(Array.from({ length: 50 }, (_, index) => purchase(`purchase-${index}`)));
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Show more');
    http
      .expectOne(
        (request) => request.url === '/api/purchases' && request.params.get('offset') === '50',
      )
      .flush([purchase('purchase-last', 'LAST')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('LAST');
    expect(text(harness)).toContain('New purchase');
  });

  it('shows the purchase error and still lists suppliers for the filter', async () => {
    remember('Admin');
    const harness = await open();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/suppliers').flush([supplier()]);
    http
      .expectOne((request) => request.url === '/api/purchases')
      .flush({ error: 'Purchase was not found.' }, { status: 404, statusText: 'Not Found' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Purchase was not found.');
    expect(text(harness)).toContain('Try again');
    expect(text(harness)).not.toContain('No purchases match this filter.');
    expect((harness.fixture.nativeElement as HTMLElement).textContent).toContain('Mill');
  });

  it('keeps loaded rows when show more cannot reach the API', async () => {
    remember('Admin');
    const harness = await open();
    flushList(Array.from({ length: 50 }, (_, index) => purchase(`purchase-${index}`)));
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Show more');
    TestBed.inject(HttpTestingController)
      .expectOne((request) => request.url === '/api/purchases' && request.params.get('offset') === '50')
      .error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).toContain('INV-9');
  });

  it('records a purchase without sending a shelf or a total', async () => {
    remember('Manager');
    const harness = await open('/purchases/new');
    flushForm();
    await turn();
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(root, 'supplierId', 'supplier-1');
    setLine(root, 0, 'product-1', '1.250', '0.10');
    harness.detectChanges();

    expect(text(harness)).toContain('0.13');
    clickButton(root, 'Save purchase');
    const request = TestBed.inject(HttpTestingController).expectOne(
      (item) => item.method === 'POST' && item.url === '/api/purchases',
    );
    const body = request.request.body as {
      supplierId: string;
      purchaseDate: string;
      reference: string | null;
      notes: string | null;
      lines: { productId: string; quantity: number; unitCost: number; shelfId?: string }[];
      total?: number;
    };
    expect(body.supplierId).toBe('supplier-1');
    expect(body.reference).toBeNull();
    expect(body.notes).toBeNull();
    expect(body.purchaseDate).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(body.lines).toEqual([{ productId: 'product-1', quantity: 1.25, unitCost: 0.1 }]);
    expect(body.total).toBeUndefined();
    expect(body.lines[0].shelfId).toBeUndefined();
    request.flush(purchase());
    await turn();
    flushForm('purchase-1');
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Purchase recorded.');
  });

  it('blocks a second save click and shows a conflict on the form', async () => {
    remember('Admin');
    const harness = await open('/purchases/new');
    flushForm();
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(root, 'supplierId', 'supplier-1');
    setLine(root, 0, 'product-1', '1', '2');
    clickButton(root, 'Save purchase');
    clickButton(root, 'Save purchase');
    const http = TestBed.inject(HttpTestingController);
    const requests = http.match((item) => item.method === 'POST' && item.url === '/api/purchases');
    expect(requests).toHaveLength(1);
    requests[0].flush({ error: 'Supplier is inactive.' }, { status: 409, statusText: 'Conflict' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier is inactive.');
    expect(TestBed.inject(HttpTestingController).match(() => true)).toHaveLength(0);
  });

  it('rejects an empty supplier before calling the API', async () => {
    remember('Admin');
    const harness = await open('/purchases/new');
    flushForm();
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Save purchase');
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier is required.');
    expect(TestBed.inject(HttpTestingController).match(() => true)).toHaveLength(0);
  });

  it('lets inventory staff receive a recorded purchase and keeps the operation id on retry', async () => {
    remember('Inventory Staff');
    const harness = await open('/purchases/purchase-1');
    flushForm('purchase-1');
    await turn();
    harness.detectChanges();

    expect(text(harness)).not.toContain('Save purchase');
    expect(text(harness)).toContain('Receive purchase');
    const root = harness.fixture.nativeElement as HTMLElement;
    const shelf = root.querySelector('select') as HTMLSelectElement;
    const receiveSelects = [...root.querySelectorAll('select')].filter((item) =>
      item.textContent?.includes('Front A'),
    );
    const choice = receiveSelects[receiveSelects.length - 1];
    choice.value = 'shelf-1';
    choice.dispatchEvent(new Event('change'));
    clickButton(root, 'Receive purchase');
    const first = TestBed.inject(HttpTestingController).expectOne(
      (item) => item.method === 'POST' && item.url === '/api/purchases/purchase-1/receive',
    );
    expect(first.request.body).toEqual({
      operationId: first.request.body.operationId,
      lines: [{ lineId: 'line-1', shelfId: 'shelf-1' }],
    });
    expect(first.request.body.operationId).toMatch(
      /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i,
    );
    first.flush({ error: 'This operation key was already used for a different stock change.' }, {
      status: 409,
      statusText: 'Conflict',
    });
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('This operation key was already used for a different stock change.');

    clickButton(harness.fixture.nativeElement, 'Receive purchase');
    const retry = TestBed.inject(HttpTestingController).expectOne(
      (item) => item.method === 'POST' && item.url === '/api/purchases/purchase-1/receive',
    );
    expect(retry.request.body.operationId).toBe(first.request.body.operationId);
    retry.flush({ ...purchase(), status: 'received', lines: [{ ...purchase().lines[0], shelfId: 'shelf-1' }] });
    await turn();
    TestBed.inject(HttpTestingController)
      .expectOne((item) => item.url === '/api/inventory/balances')
      .flush([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Purchase received.');
    expect(text(harness)).not.toContain('Receive purchase');
    expect(shelf).toBeTruthy();
  });

  it('hides receive and save on a received purchase', async () => {
    remember('Admin');
    const harness = await open('/purchases/purchase-1');
    flushForm('purchase-1', { ...purchase(), status: 'received' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).not.toContain('Save purchase');
    expect(text(harness)).not.toContain('Receive purchase');
    expect(text(harness)).toContain('cannot be changed');
    expect(text(harness)).toContain('Shop Floor / Front A');
  });

  it('shows a missing purchase without the form', async () => {
    remember('Admin');
    const harness = await open('/purchases/missing');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne((request) => request.url === '/api/suppliers').flush([]);
    http.expectOne((request) => request.url === '/api/products').flush([]);
    http.expectOne((request) => request.url === '/api/shelves').flush([]);
    http
      .expectOne((request) => request.url === '/api/purchases/missing')
      .flush({ error: 'Purchase was not found.' }, { status: 404, statusText: 'Not Found' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Purchase was not found.');
    expect(text(harness)).not.toContain('Save purchase');
  });
});

function open(url = '/purchases'): Promise<RouterTestingHarness> {
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

function purchase(id = 'purchase-1', reference = 'INV-9') {
  return {
    id,
    supplierId: 'supplier-1',
    supplierName: 'Mill',
    purchaseDate: '2026-10-20',
    reference,
    notes: null,
    status: 'recorded',
    total: 0.13,
    receiveOperationId: null,
    receivedAt: null,
    lines: [
      {
        id: 'line-1',
        productId: 'product-1',
        productName: 'Rice',
        quantity: 1.25,
        unitCost: 0.1,
        lineTotal: 0.13,
        shelfId: 'shelf-1',
      },
    ],
    createdAt: '2026-10-20T00:00:00Z',
    updatedAt: '2026-10-20T00:00:00Z',
  };
}

function supplier() {
  return {
    id: 'supplier-1',
    name: 'Mill',
    contactPerson: null,
    phone: null,
    email: null,
    address: null,
    notes: null,
    isActive: true,
    createdAt: '2026-10-20T00:00:00Z',
    updatedAt: '2026-10-20T00:00:00Z',
  };
}

function flushList(rows: unknown[]): void {
  const http = TestBed.inject(HttpTestingController);
  http.expectOne((request) => request.url === '/api/suppliers').flush([supplier()]);
  http.expectOne((request) => request.url === '/api/purchases').flush(rows);
}

function flushForm(id?: string, body: ReturnType<typeof purchase> = purchase()): void {
  const http = TestBed.inject(HttpTestingController);
  http.expectOne((request) => request.url === '/api/suppliers').flush([supplier()]);
  http.expectOne((request) => request.url === '/api/products').flush([
    {
      id: 'product-1',
      name: 'Rice',
      categoryId: null,
      categoryName: null,
      brandId: null,
      brandName: null,
      unitId: 'unit-1',
      unitName: 'Kilogram',
      purchasePrice: 1,
      sellingPrice: 2,
      minimumStock: 0,
      isActive: true,
      barcodes: [],
      createdAt: '2026-10-20T00:00:00Z',
      updatedAt: '2026-10-20T00:00:00Z',
    },
  ]);
  http.expectOne((request) => request.url === '/api/shelves').flush([
    {
      id: 'shelf-1',
      areaId: 'area-1',
      areaName: 'Shop Floor',
      name: 'Front A',
      isActive: true,
      createdAt: '2026-10-20T00:00:00Z',
      updatedAt: '2026-10-20T00:00:00Z',
    },
  ]);
  if (id) {
    http.expectOne((request) => request.url === `/api/purchases/${id}`).flush(body);
  }
}

function setControl(root: HTMLElement, name: string, value: string): void {
  const input = root.querySelector(`[formcontrolname="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
  input.dispatchEvent(new Event('change'));
}

function setLine(root: HTMLElement, index: number, productId: string, quantity: string, unitCost: string): void {
  const groups = [...root.querySelectorAll('.toolbar')].filter((item) => item.textContent?.includes('Unit cost'));
  const group = groups[index];
  const [product, amount, cost] = [...group.querySelectorAll('select, input')] as HTMLInputElement[];
  product.value = productId;
  product.dispatchEvent(new Event('change'));
  amount.value = quantity;
  amount.dispatchEvent(new Event('input'));
  cost.value = unitCost;
  cost.dispatchEvent(new Event('input'));
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
