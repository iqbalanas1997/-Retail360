import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('customer screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows customers and searches by the server', async () => {
    remember('Admin');
    const harness = await open();

    expect(text(harness)).toContain('Loading customers…');
    flushList([customer('Amina')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Amina');
    expect(text(harness)).toContain('0300');
    expect(text(harness)).toContain('amina@example.com');
    expect(text(harness)).toContain('Active');

    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'search', '0300');
    const activeOnly = root.querySelector('input[formcontrolname="activeOnly"]') as HTMLInputElement;
    activeOnly.checked = true;
    activeOnly.dispatchEvent(new Event('change'));
    clickButton(root, 'Search');
    expectCustomerQuery({ search: '0300', activeOnly: 'true', limit: '50', offset: '0' }).flush([
      customer('Amina', 'customer-2'),
    ]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Amina');
  });

  it('rejects a blank name before calling the API', async () => {
    remember('Manager');
    const harness = await open('/customers/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', '   ');
    clickButton(root, 'Save customer');
    harness.detectChanges();

    expect(text(harness)).toContain('Name must be between 1 and 200 characters.');
    TestBed.inject(HttpTestingController).expectNone(
      (request) => request.method === 'POST' && request.url === '/api/customers',
    );
  });

  it('creates a customer and shows an API error without dropping the form', async () => {
    remember('Admin');
    const harness = await open('/customers/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', '  Amina  ');
    setInput(root, 'phone', ' 0300 ');
    clickButton(root, 'Save customer');
    clickButton(root, 'Save customer');
    const http = TestBed.inject(HttpTestingController);
    const created = http.expectOne({ method: 'POST', url: '/api/customers' });
    expect(created.request.body).toEqual({
      name: 'Amina',
      phone: '0300',
      email: null,
      address: null,
      notes: null,
    });
    created.flush({ error: 'Customer was not found.' }, { status: 404, statusText: 'Not Found' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Customer was not found.');
    expect((root.querySelector('input[formcontrolname="name"]') as HTMLInputElement).value).toBe('  Amina  ');

    clickButton(root, 'Save customer');
    http.expectOne({ method: 'POST', url: '/api/customers' }).flush(customer('Amina'));
    await turn();
    http.expectOne('/api/customers/customer-1').flush(customer('Amina'));
    await turn();
    flushSales();
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/customers/customer-1');
    expect(text(harness)).toContain('Customer created.');
    expect(text(harness)).toContain('No sales for this customer.');
    expect(text(harness)).not.toContain('owed');
  });

  it('saves an edit, deactivates, and removes the customer', async () => {
    remember('Manager');
    const harness = await open('/customers/customer-1');
    TestBed.inject(HttpTestingController).expectOne('/api/customers/customer-1').flush(customer('Amina'));
    await turn();
    flushSales('customer-1', [
      {
        id: 'sale-1',
        receiptNumber: 4,
        soldAt: '2026-10-19T21:05:46.5421+00:00',
        paymentMethod: 'cash',
        total: 35,
      },
    ]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Receipt');
    expect(text(harness)).toContain('cash');
    expect(text(harness)).toContain('35.00');
    expect(text(harness)).not.toContain('owed');

    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'name', 'Amina Khan');
    clickButton(root, 'Save customer');
    const saved = TestBed.inject(HttpTestingController).expectOne({
      method: 'PUT',
      url: '/api/customers/customer-1',
    });
    expect(saved.request.body.name).toBe('Amina Khan');
    saved.flush(customer('Amina Khan'));
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Customer saved.');

    clickButton(root, 'Deactivate');
    TestBed.inject(HttpTestingController)
      .expectOne({ method: 'POST', url: '/api/customers/customer-1/deactivate' })
      .flush({ ...customer('Amina Khan'), isActive: false });
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Customer is inactive.');

    clickButton(root, 'Activate');
    TestBed.inject(HttpTestingController)
      .expectOne({ method: 'POST', url: '/api/customers/customer-1/activate' })
      .flush(customer('Amina Khan'));
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Customer is active.');

    clickButton(root, 'Remove customer');
    harness.detectChanges();
    clickButton(root, 'Remove');
    TestBed.inject(HttpTestingController)
      .expectOne({ method: 'DELETE', url: '/api/customers/customer-1' })
      .flush(null);
    await turn();
    flushList([]);
    await turn();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/customers');
    expect(text(harness)).toContain('Customer removed.');
  });
});

function customer(name: string, id = 'customer-1') {
  return {
    id,
    name,
    phone: '0300',
    email: 'amina@example.com',
    address: 'Lane 1',
    notes: 'Rice',
    isActive: true,
    createdAt: '',
    updatedAt: '',
  };
}

async function open(url = '/customers'): Promise<RouterTestingHarness> {
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

function flushList(body: ReturnType<typeof customer>[]): void {
  TestBed.inject(HttpTestingController)
    .expectOne('/api/customers?activeOnly=false&limit=50&offset=0')
    .flush(body);
}

function expectCustomerQuery(query: Record<string, string>) {
  return TestBed.inject(HttpTestingController).expectOne((request) => {
    if (request.method !== 'GET' || request.url !== '/api/customers') {
      return false;
    }

    return Object.entries(query).every(([key, value]) => request.params.get(key) === value);
  });
}

function flushSales(
  id = 'customer-1',
  body: Array<{
    id: string;
    receiptNumber: number;
    soldAt: string;
    paymentMethod: string;
    total: number;
  }> = [],
): void {
  TestBed.inject(HttpTestingController)
    .expectOne(`/api/customers/${id}/sales?limit=50&offset=0`)
    .flush(body);
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
