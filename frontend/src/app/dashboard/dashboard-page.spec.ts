import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';
import { DashboardPage } from './dashboard-page';

interface DashboardControls {
  form: { setValue: (value: { from: string; to: string }) => void };
  applyCustom: () => void;
}

describe('dashboard screen', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows loading, then zero totals for an empty period', async () => {
    remember('Admin');
    const harness = await open();

    expect(text(harness)).toContain('Loading dashboard…');
    flushDashboard(emptyReport('2026-10-22', '2026-10-22'));
    flushStock({ count: 0, products: [] });
    await turn();
    harness.detectChanges();

    const body = text(harness);
    expect(body).toContain('Sales total');
    expect(body).toContain('0.0');
    expect(body).toContain('No sales');
    expect(body).toContain('No sales in this period.');
    expect(body).toContain('No expenses in this period.');
    expect(body).toContain('No active products are low on stock.');
    expect(body).not.toContain('Profit');
  });

  it('applies an explicit range and ignores an older response', async () => {
    remember('Manager');
    const harness = await open();
    flushDashboard(emptyReport('2026-10-22', '2026-10-22'));
    flushStock({ count: 1, products: [{ id: 'rice', name: 'Rice', onHand: 3, minimumStock: 3 }] });
    await turn();
    harness.detectChanges();

    const page = dashboard(harness);
    page.form.setValue({ from: '2026-10-01', to: '2026-10-07' });
    page.applyCustom();
    page.form.setValue({ from: '2026-10-08', to: '2026-10-14' });
    page.applyCustom();
    await turn();

    const requests = TestBed.inject(HttpTestingController).match(
      (request) => request.url === '/api/reports/dashboard',
    );
    expect(requests.map((request) => request.request.params.get('from'))).toEqual(['2026-10-01', '2026-10-08']);
    requests[1].flush(reportWithSale('2026-10-08', '2026-10-14', 8.5));
    requests[0].flush(reportWithSale('2026-10-01', '2026-10-07', 99));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('8.50');
    expect(text(harness)).not.toContain('99.00');
    expect(text(harness)).toContain('Rice');
    expect(text(harness)).toContain('3.000');
    TestBed.inject(HttpTestingController).expectNone('/api/reports/low-stock?limit=10');
  });

  it('shows the error and does not request a reversed range', async () => {
    remember('Admin');
    const harness = await open();
    const first = TestBed.inject(HttpTestingController).expectOne(
      (request) => request.url === '/api/reports/dashboard',
    );
    first.flush({ error: 'The request could not be completed.' }, { status: 500, statusText: 'Error' });
    flushStock({ count: 0, products: [] });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('The request could not be completed.');
    const page = dashboard(harness);
    page.form.setValue({ from: '2026-10-22', to: '2026-10-21' });
    page.applyCustom();
    harness.detectChanges();

    expect(text(harness)).toContain('The start date must be on or before the end date.');
    TestBed.inject(HttpTestingController).expectNone((request) => request.url === '/api/reports/dashboard');
  });

  it('hides the dashboard link from a cashier', async () => {
    remember('Cashier');
    const harness = await open('/catalog/products');
    TestBed.inject(HttpTestingController).expectOne('/api/products?activeOnly=false&limit=50&offset=0').flush([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).not.toContain('Dashboard');
  });
});

async function open(url = '/dashboard'): Promise<RouterTestingHarness> {
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
  const encoded = btoa(JSON.stringify({ role })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  writeStoredSession({
    accessToken: `header.${encoded}.signature`,
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    userId: 'user-1',
    userName: role,
    roles: [role],
  });
}

function dashboard(harness: RouterTestingHarness): DashboardControls {
  const routed = harness.fixture.debugElement.query(
    (node) => node.componentInstance instanceof DashboardPage,
  );
  return (routed?.componentInstance ?? harness.fixture.componentInstance) as unknown as DashboardControls;
}

function flushDashboard(body: object): void {
  TestBed.inject(HttpTestingController)
    .expectOne((request) => request.url === '/api/reports/dashboard')
    .flush(body);
}

function flushStock(body: object): void {
  TestBed.inject(HttpTestingController).expectOne('/api/reports/low-stock?limit=10').flush(body);
}

function emptyReport(from: string, to: string) {
  return {
    from,
    to,
    sales: { count: 0, total: 0, average: null },
    expenses: {
      count: 0,
      total: 0,
      categories: [{ category: 'rent', count: 0, total: 0 }],
    },
    purchases: { recordedCount: 0, recordedTotal: 0, receivedCount: 0, receivedTotal: 0 },
    recentSales: [],
    recentExpenses: [],
  };
}

function reportWithSale(from: string, to: string, total: number) {
  return {
    ...emptyReport(from, to),
    sales: { count: 1, total, average: total },
    recentSales: [{ id: 'sale-1', receiptNumber: 4, soldAt: '2026-10-08T08:00:00+00:00', total }],
  };
}

async function turn(): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, 0));
}

function text(harness: RouterTestingHarness): string {
  return (harness.fixture.nativeElement as HTMLElement).textContent ?? '';
}
