import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('supplier screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows the loading state and then a supplier row', async () => {
    remember('Admin');
    const harness = await open();

    expect(text(harness)).toContain('Loading suppliers…');
    flushList([supplier('Sufi')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Sufi');
    expect(text(harness)).toContain('Amina');
    expect(text(harness)).toContain('0300');
    expect(text(harness)).toContain('shop@example.com');
    expect(text(harness)).toContain('Active');
    expect(text(harness)).not.toContain('Loading suppliers…');
  });

  it('searches and pages on the server', async () => {
    remember('Admin');
    const harness = await open();
    flushList([supplier('Alpha')]);
    await turn();
    harness.detectChanges();

    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', 'Sufi');
    const activeOnly = root.querySelector('input[formcontrolname="activeOnly"]') as HTMLInputElement;
    activeOnly.checked = true;
    activeOnly.dispatchEvent(new Event('change'));
    clickButton(root, 'Search');
    const searched = expectSupplierQuery(TestBed.inject(HttpTestingController), {
      name: 'Sufi',
      activeOnly: 'true',
      limit: '50',
      offset: '0',
    });
    searched.flush([supplier('Sufi Traders', 'supplier-2')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Sufi Traders');
    expect(text(harness)).not.toContain('Alpha');

    const page = Array.from({ length: 50 }, (_, index) => supplier(`Supplier ${index}`, `id-${index}`));
    clickButton(root, 'Search');
    expectSupplierQuery(TestBed.inject(HttpTestingController), {
      name: 'Sufi',
      activeOnly: 'true',
      limit: '50',
      offset: '0',
    }).flush(page);
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Show more');
    expectSupplierQuery(TestBed.inject(HttpTestingController), {
      name: 'Sufi',
      activeOnly: 'true',
      limit: '50',
      offset: '50',
    }).flush([supplier('Last mill', 'id-last')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier 0');
    expect(text(harness)).toContain('Last mill');
  });

  it('keeps the newer search when an older response arrives later', async () => {
    remember('Manager');
    const harness = await open();
    const http = TestBed.inject(HttpTestingController);
    const first = http.expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0');
    clickButton(harness.fixture.nativeElement, 'Search');
    const second = http.expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0');
    second.flush([supplier('Sufi')]);
    await turn();
    harness.detectChanges();
    first.flush([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Sufi');
    expect(text(harness)).not.toContain('No suppliers match this search.');
  });

  it('shows an empty search and a retry after an error', async () => {
    remember('Cashier');
    const harness = await open();
    flushList([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('No suppliers match this search.');
    expect(text(harness)).not.toContain('New supplier');

    clickButton(harness.fixture.nativeElement, 'Search');
    TestBed.inject(HttpTestingController)
      .expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0')
      .flush('down', { status: 503, statusText: 'Unavailable' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).not.toContain('No suppliers match this search.');
    clickButton(harness.fixture.nativeElement, 'Try again');
    flushList([supplier('Sufi')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Sufi');
  });

  it('shows a proxy failure instead of an empty supplier list', async () => {
    remember('Admin');
    const harness = await open();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0')
      .error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).toContain('Try again');
    expect(text(harness)).not.toContain('No suppliers match this search.');
  });

  it('shows a forbidden supplier list instead of an empty one', async () => {
    remember('Cashier');
    const harness = await open();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect(text(harness)).not.toContain('No suppliers match this search.');
    expect(text(harness)).not.toContain('New supplier');
  });

  it('keeps the loaded page when the next page cannot be reached', async () => {
    remember('Admin');
    const harness = await open();
    const http = TestBed.inject(HttpTestingController);
    const page = Array.from({ length: 50 }, (_, index) => supplier(`Supplier ${index}`, `id-${index}`));
    http.expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0').flush(page);
    await turn();
    harness.detectChanges();

    clickButton(harness.fixture.nativeElement, 'Show more');
    http
      .expectOne((request) => request.url === '/api/suppliers' && request.params.get('offset') === '50')
      .error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier 0');
    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).not.toContain('No suppliers match this search.');
  });

  it('sends an unsigned visitor to sign-in and leaves when the session expires', async () => {
    const signedOut = await open();
    expect(TestBed.inject(Router).url).toBe('/login');
    expect(text(signedOut)).not.toContain('Loading suppliers…');

    TestBed.resetTestingModule();
    remember('Admin', new Date(Date.now() + 300).toISOString());
    const harness = await open();
    flushList([]);
    await turn();
    await new Promise((resolve) => setTimeout(resolve, 400));
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('returns to sign-in when the supplier list is unauthorized', async () => {
    remember('Admin');
    const harness = await open();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0')
      .flush({ error: 'Authentication is required.' }, { status: 401, statusText: 'Unauthorized' });
    await turn();
    harness.detectChanges();
    await harness.fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
  });

  it('rejects an empty name and a bad email before saving', async () => {
    remember('Admin');
    const harness = await open('/suppliers/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    clickButton(root, 'Save supplier');
    harness.detectChanges();

    expect(text(harness)).toContain('Name must be between 1 and 200 characters.');
    expect(TestBed.inject(HttpTestingController).match('/api/suppliers')).toHaveLength(0);

    setInput(root, 'name', 'Sufi');
    setInput(root, 'email', 'not-an-email');
    clickButton(root, 'Save supplier');
    harness.detectChanges();

    expect(text(harness)).toContain('Email must be empty or a single address of at most 200 characters.');
    expect(TestBed.inject(HttpTestingController).match('/api/suppliers')).toHaveLength(0);

    setInput(root, 'email', '');
    setInput(root, 'phone', '1'.repeat(41));
    clickButton(root, 'Save supplier');
    harness.detectChanges();

    expect(text(harness)).toContain('Phone must be between 1 and 40 characters.');
    expect(TestBed.inject(HttpTestingController).match('/api/suppliers')).toHaveLength(0);
  });

  it('creates a supplier once and keeps the entered details after a duplicate name', async () => {
    remember('Admin');
    const harness = await open('/suppliers/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', '  Sufi  ');
    setInput(root, 'phone', ' 0300 ');
    const save = root.querySelector('button[type="submit"]') as HTMLButtonElement;
    save.click();
    save.click();
    const http = TestBed.inject(HttpTestingController);
    const created = http.expectOne({ method: 'POST', url: '/api/suppliers' });
    expect(created.request.body).toEqual({
      name: 'Sufi',
      contactPerson: null,
      phone: '0300',
      email: null,
      address: null,
      notes: null,
    });
    created.flush(
      { error: 'A supplier with this name already exists.' },
      { status: 409, statusText: 'Conflict' },
    );
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('A supplier with this name already exists.');
    expect((root.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('  Sufi  ');

    save.click();
    const retry = http.expectOne({ method: 'POST', url: '/api/suppliers' });
    retry.flush(supplier('Sufi'));
    await turn();
    http.expectOne('/api/suppliers/supplier-1').flush(supplier('Sufi'));
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/suppliers/supplier-1');
    expect(text(harness)).toContain('Supplier created.');
  });

  it('keeps the entered supplier when save is rejected or the API cannot be reached', async () => {
    remember('Manager');
    const harness = await open('/suppliers/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', 'Sufi');
    clickButton(root, 'Save supplier');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne({ method: 'POST', url: '/api/suppliers' }).flush(
      { error: 'Phone must be between 1 and 40 characters.' },
      { status: 400, statusText: 'Bad Request' },
    );
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/suppliers/new');
    expect(text(harness)).toContain('Phone must be between 1 and 40 characters.');
    expect((root.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('Sufi');

    clickButton(root, 'Save supplier');
    http.expectOne({ method: 'POST', url: '/api/suppliers' }).flush(
      { error: 'You do not have access to this action.' },
      { status: 403, statusText: 'Forbidden' },
    );
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect((root.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('Sufi');

    clickButton(root, 'Save supplier');
    http.expectOne({ method: 'POST', url: '/api/suppliers' }).error(new ProgressEvent('error'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The shop API could not be reached.');
    expect(text(harness)).toContain('Save supplier');
    expect((root.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('Sufi');
  });

  it('shows a missing supplier instead of an empty form', async () => {
    remember('Admin');
    const harness = await open('/suppliers/missing-supplier');
    TestBed.inject(HttpTestingController)
      .expectOne('/api/suppliers/missing-supplier')
      .flush({ error: 'Supplier was not found.' }, { status: 404, statusText: 'Not Found' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier was not found.');
    expect(text(harness)).toContain('Try again');
    expect(text(harness)).not.toContain('Save supplier');
  });

  it('saves supplier details without changing the inactive state', async () => {
    remember('Manager');
    const harness = await open('/suppliers/supplier-1');
    TestBed.inject(HttpTestingController).expectOne('/api/suppliers/supplier-1').flush({
      ...supplier('Sufi'),
      isActive: false,
    });
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'phone', '0311');
    clickButton(root, 'Save supplier');
    const update = TestBed.inject(HttpTestingController).expectOne({
      method: 'PUT',
      url: '/api/suppliers/supplier-1',
    });
    expect(update.request.body).toEqual({
      name: 'Sufi',
      contactPerson: 'Amina',
      phone: '0311',
      email: 'shop@example.com',
      address: 'Karachi',
      notes: 'Rice',
    });
    expect(Object.hasOwn(update.request.body as object, 'isActive')).toBe(false);
    update.flush({ ...supplier('Sufi'), phone: '0311', isActive: false });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier saved.');
    expect(text(harness)).toContain('Activate');
    expect(text(harness)).not.toContain('Deactivate');
  });

  it('deactivates once and shows a delete conflict without leaving the supplier', async () => {
    remember('Admin');
    const harness = await open('/suppliers/supplier-1');
    TestBed.inject(HttpTestingController).expectOne('/api/suppliers/supplier-1').flush(supplier('Sufi'));
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    const deactivate = [...root.querySelectorAll('button')].find(
      (item) => item.textContent?.trim() === 'Deactivate',
    ) as HTMLButtonElement;
    deactivate.click();
    deactivate.click();
    const http = TestBed.inject(HttpTestingController);
    const request = http.expectOne('/api/suppliers/supplier-1/deactivate');
    expect(http.match({ method: 'POST' })).toHaveLength(0);
    request.flush({ ...supplier('Sufi'), isActive: false });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Supplier is inactive.');
    clickButton(harness.fixture.nativeElement, 'Remove supplier');
    harness.detectChanges();

    expect(text(harness)).toContain('This hides the supplier from the list. The name can be used again.');
    expect(text(harness)).not.toContain('purchase');
    clickButton(harness.fixture.nativeElement.querySelector('.confirm') as HTMLElement, 'Cancel');
    harness.detectChanges();

    expect(text(harness)).toContain('Remove supplier');
    expect(TestBed.inject(HttpTestingController).match({ method: 'DELETE' })).toHaveLength(0);

    clickButton(harness.fixture.nativeElement, 'Remove supplier');
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement.querySelector('.confirm') as HTMLElement, 'Remove');
    http.expectOne({ method: 'DELETE', url: '/api/suppliers/supplier-1' }).flush(
      { error: 'Supplier could not be removed.' },
      { status: 409, statusText: 'Conflict' },
    );
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/suppliers/supplier-1');
    expect(text(harness)).toContain('Supplier could not be removed.');
    expect((harness.fixture.nativeElement.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe(
      'Sufi',
    );
  });

  it('removes a supplier after confirmation', async () => {
    remember('Admin');
    const harness = await open('/suppliers/supplier-1');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/suppliers/supplier-1').flush(supplier('Sufi'));
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Remove supplier');
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement.querySelector('.confirm') as HTMLElement, 'Remove');
    http.expectOne({ method: 'DELETE', url: '/api/suppliers/supplier-1' }).flush(null, { status: 204, statusText: 'No Content' });
    await turn();
    http.expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0').flush([]);
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/suppliers');
    expect(text(harness)).toContain('Supplier removed.');
  });

  it('hides supplier changes from Inventory Staff and shows them to a Manager', async () => {
    remember('Inventory Staff');
    const staff = await open('/suppliers/supplier-1');
    TestBed.inject(HttpTestingController).expectOne('/api/suppliers/supplier-1').flush(supplier('Sufi'));
    await turn();
    staff.detectChanges();

    expect(text(staff)).toContain('Only an Admin or Manager can change it.');
    expect(text(staff)).not.toContain('Save supplier');
    expect(text(staff)).not.toContain('Deactivate');
    expect(text(staff)).not.toContain('Remove supplier');

    TestBed.resetTestingModule();
    remember('Manager');
    const manager = await open();
    flushList([]);
    await turn();
    manager.detectChanges();

    expect(text(manager)).toContain('New supplier');
    expect(text(manager)).toContain('Suppliers');
  });
});

async function open(url = '/suppliers'): Promise<RouterTestingHarness> {
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

function remember(role: string, expiresAt = new Date(Date.now() + 60_000).toISOString()): void {
  const body = btoa(JSON.stringify({ role })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  writeStoredSession({
    accessToken: `header.${body}.signature`,
    expiresAt,
    userId: 'user-1',
    userName: role,
    roles: [role],
  });
}

function supplier(name: string, id = 'supplier-1') {
  return {
    id,
    name,
    contactPerson: 'Amina',
    phone: '0300',
    email: 'shop@example.com',
    address: 'Karachi',
    notes: 'Rice',
    isActive: true,
    createdAt: '',
    updatedAt: '',
  };
}

function flushList(body: ReturnType<typeof supplier>[]): void {
  TestBed.inject(HttpTestingController)
    .expectOne('/api/suppliers?activeOnly=false&limit=50&offset=0')
    .flush(body);
}

function expectSupplierQuery(
  http: HttpTestingController,
  query: Record<string, string>,
) {
  return http.expectOne((request) => {
    if (request.method !== 'GET' || request.url !== '/api/suppliers') {
      return false;
    }

    return Object.entries(query).every(([key, value]) => request.params.get(key) === value);
  });
}

function text(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function setInput(root: HTMLElement, name: string, value: string): void {
  const input = root.querySelector(`[formcontrolname="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function clickButton(root: HTMLElement, label: string): void {
  const button = [...root.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === label,
  ) as HTMLButtonElement;
  button.click();
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
