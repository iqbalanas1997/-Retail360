import { Type } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';
import { LocationsPage } from './locations-page';
import { StockPage } from './stock-page';
import { StockPostPage } from './stock-post-page';

describe('inventory screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  it('sends an unsigned visitor to sign in', async () => {
    TestBed.configureTestingModule({ providers: [provideRouter(routes)] });
    const harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/inventory/stock');

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('shows an empty stock list and hides opening stock from a cashier', async () => {
    remember('Cashier');
    const fixture = create(StockPage);
    expect(fixture.nativeElement.textContent).toContain('Loading stock…');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/inventory/stock?status=all&limit=50&offset=0').flush([]);
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('No stock rows match this search.');
    expect(fixture.nativeElement.textContent).not.toContain('Opening stock');
  });

  it('shows a stock API failure and leaves the list empty', async () => {
    remember('Admin');
    const fixture = create(StockPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/inventory/stock?status=all&limit=50&offset=0').flush(
      { error: 'You do not have access to this action.' },
      { status: 403, statusText: 'Forbidden' },
    );
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('You do not have access to this action.');
    expect(fixture.nativeElement.textContent).not.toContain('No stock rows match this search.');
  });

  it('sends a 401 back to sign in', async () => {
    remember('Admin');
    const fixture = create(StockPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/inventory/stock?status=all&limit=50&offset=0').flush(
      { error: 'Authentication is required.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('hides location changes from a cashier', async () => {
    remember('Cashier');
    const fixture = create(LocationsPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/areas').flush([{ id: 'area-1', name: 'Front', isActive: true, createdAt: '', updatedAt: '' }]);
    await turn();
    http.expectOne((request) => request.url.startsWith('/api/shelves')).flush([]);
    await turn();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Add area');
    expect(fixture.nativeElement.textContent).toContain('Front');
  });

  it('shows a delete blocked by stock history', async () => {
    remember('Inventory Staff');
    const fixture = create(LocationsPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/areas').flush([{ id: 'area-1', name: 'Front', isActive: true, createdAt: '', updatedAt: '' }]);
    await turn();
    http.expectOne((request) => request.url.startsWith('/api/shelves')).flush([]);
    await turn();
    fixture.detectChanges();

    const remove = [...fixture.nativeElement.querySelectorAll('button')].find(
      (button: HTMLButtonElement) => button.textContent?.trim() === 'Remove',
    ) as HTMLButtonElement;
    remove.click();
    fixture.detectChanges();
    const confirm = [...fixture.nativeElement.querySelectorAll('button')].find(
      (button: HTMLButtonElement) => button.textContent?.trim() === 'Remove' && !button.classList.contains('secondary'),
    ) as HTMLButtonElement;
    confirm.click();
    http.expectOne('/api/areas/area-1').flush(
      { error: 'Area cannot be deleted because it has a shelf or stock history.' },
      { status: 409, statusText: 'Conflict' },
    );
    await turn();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain(
      'Area cannot be deleted because it has a shelf or stock history.',
    );
  });

  it('reuses the operation id after a network failure and shows a stock conflict', async () => {
    remember('Admin');
    const fixture = create(StockPostPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/products?activeOnly=false&limit=200&offset=0').flush([
      { id: 'product-1', name: 'Rice', barcodes: ['RICE-5KG'] },
    ]);
    http.expectOne('/api/shelves').flush([
      { id: 'shelf-1', areaId: 'area-1', areaName: 'Front', name: 'A1', isActive: true },
    ]);
    await fixture.whenStable();
    fixture.detectChanges();

    choose(fixture.nativeElement, 'productId', 'product-1');
    choose(fixture.nativeElement, 'shelfId', 'shelf-1');
    setQuantity(fixture.nativeElement, '2.000');
    submit(fixture.nativeElement);
    const first = http.expectOne('/api/inventory/opening');
    const operationId = first.request.body.operationId as string;
    first.error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Submit again to retry this same stock operation.');
    submit(fixture.nativeElement);
    const retry = http.expectOne('/api/inventory/opening');
    expect(retry.request.body.operationId).toBe(operationId);
    retry.flush({ error: 'Shelf does not have enough stock.' }, { status: 409, statusText: 'Conflict' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Shelf does not have enough stock.');
    http.verify();
  });
});

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function create<T>(component: Type<T>) {
  TestBed.configureTestingModule({
    imports: [component],
    providers: [
      provideRouter(routes),
      provideHttpClient(withInterceptors([authInterceptor])),
      provideHttpClientTesting(),
    ],
  });
  const fixture = TestBed.createComponent(component);
  fixture.detectChanges();
  return fixture;
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

function choose(root: HTMLElement, name: string, value: string): void {
  const select = root.querySelector(`select[formcontrolname="${name}"]`) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('change'));
}

function setQuantity(root: HTMLElement, value: string): void {
  const input = root.querySelector('input[formcontrolname="quantity"]') as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function submit(root: HTMLElement): void {
  (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
}
