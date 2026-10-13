import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('catalog screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('shows a product list failure instead of an empty catalog', async () => {
    remember('Admin');
    const harness = await open();
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/products?activeOnly=false&limit=50&offset=0').flush(
      { error: 'You do not have access to this action.' },
      { status: 403, statusText: 'Forbidden' },
    );
    await turn();
    harness.detectChanges();

    expect(harness.fixture.nativeElement.textContent).toContain('You do not have access to this action.');
    expect(harness.fixture.nativeElement.textContent).not.toContain('No products match this search.');
  });

  it('hides new product from a cashier', async () => {
    remember('Cashier');
    const harness = await open();
    TestBed.inject(HttpTestingController).expectOne('/api/products?activeOnly=false&limit=50&offset=0').flush([]);
    await turn();
    harness.detectChanges();

    expect(harness.fixture.nativeElement.textContent).toContain('No products match this search.');
    expect(harness.fixture.nativeElement.textContent).not.toContain('New product');
  });

  it('keeps the newer product page when an older response arrives later', async () => {
    remember('Admin');
    const harness = await open();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne('/api/products?activeOnly=false&limit=50&offset=0');
    (harness.fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement).click();
    const second = http.expectOne('/api/products?activeOnly=false&limit=50&offset=0');
    second.flush([product('Rice')]);
    await turn();
    harness.detectChanges();
    first.flush([]);
    await turn();
    harness.detectChanges();

    expect(harness.fixture.nativeElement.textContent).toContain('Rice');
    expect(harness.fixture.nativeElement.textContent).not.toContain('No products match this search.');
  });

  it('shows the stock-history message when product removal is blocked', async () => {
    remember('Admin');
    const harness = await open('/catalog/products/product-1');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/categories').flush([]);
    http.expectOne('/api/brands').flush([]);
    http.expectOne('/api/units').flush([{ id: 'unit-1', name: 'Piece', createdAt: '', updatedAt: '' }]);
    await turn();
    http.expectOne('/api/products/product-1').flush(product('Rice'));
    await turn();
    harness.detectChanges();

    clickButton(harness.fixture.nativeElement, 'Remove product');
    harness.detectChanges();
    const confirm = harness.fixture.nativeElement.querySelector('.confirm button') as HTMLButtonElement;
    confirm.click();
    http.expectOne('/api/products/product-1').flush(
      { error: 'Product cannot be deleted because it has stock history.' },
      { status: 409, statusText: 'Conflict' },
    );
    await turn();
    harness.detectChanges();

    expect(harness.fixture.nativeElement.textContent).toContain(
      'Product cannot be deleted because it has stock history.',
    );
  });

  it('sends one create request when save is clicked twice', async () => {
    remember('Admin');
    const harness = await open('/catalog/products/new');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/categories').flush([]);
    http.expectOne('/api/brands').flush([]);
    http.expectOne('/api/units').flush([{ id: 'unit-1', name: 'Piece', createdAt: '', updatedAt: '' }]);
    await turn();
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    const name = root.querySelector('input[formcontrolname="name"]') as HTMLInputElement;
    name.value = 'Rice';
    name.dispatchEvent(new Event('input'));
    const unit = root.querySelector('select[formcontrolname="unitId"]') as HTMLSelectElement;
    unit.value = 'unit-1';
    unit.dispatchEvent(new Event('change'));
    const save = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    save.click();
    save.click();

    expect(http.match('/api/products')).toHaveLength(1);
  });
});

async function open(url = '/catalog/products'): Promise<RouterTestingHarness> {
  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  const harness = await RouterTestingHarness.create();
  await harness.navigateByUrl(url);
  return harness;
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

function product(name: string) {
  return {
    id: 'product-1',
    name,
    categoryId: null,
    categoryName: null,
    brandId: null,
    brandName: null,
    unitId: 'unit-1',
    unitName: 'Piece',
    purchasePrice: 1,
    sellingPrice: 2,
    minimumStock: 1,
    isActive: true,
    barcodes: ['RICE'],
    createdAt: '',
    updatedAt: '',
  };
}

function clickButton(root: HTMLElement, label: string): void {
  const button = [...root.querySelectorAll('button')].find(
    (item: HTMLButtonElement) => item.textContent?.trim() === label,
  ) as HTMLButtonElement;
  button.click();
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
