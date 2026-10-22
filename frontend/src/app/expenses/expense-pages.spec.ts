import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { ExpenseFormPage } from './expense-form-page';
import { ExpenseListPage } from './expense-list-page';
import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('expense screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('shows the filtered total and a category row', async () => {
    remember('Cashier');
    const harness = await open();

    expect(text(harness)).toContain('Loading expenses…');
    flushList({ expenses: [expense()], total: 12.5 });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Total 12.50');
    expect(text(harness)).toContain('2026-10-22');
    expect(text(harness)).toContain('Rent');
    expect(text(harness)).toContain('Shop rent');
    expect(text(harness)).toContain('Cash');
    expect(text(harness)).not.toContain('New expense');
  });

  it('sends the date and category filter', async () => {
    remember('Admin');
    const harness = await open();
    flushList({ expenses: [], total: 0 });
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(harness, 'from', '2026-10-01');
    setControl(harness, 'to', '2026-10-31');
    setControl(harness, 'category', 'rent');
    clickButton(root, 'Search');
    await turn();

    const request = TestBed.inject(HttpTestingController).expectOne((candidate) => {
      return candidate.method === 'GET'
        && candidate.url === '/api/expenses'
        && candidate.params.get('from') === '2026-10-01'
        && candidate.params.get('to') === '2026-10-31'
        && candidate.params.get('category') === 'rent'
        && candidate.params.get('limit') === '50'
        && candidate.params.get('offset') === '0';
    });
    request.flush({ expenses: [expense()], total: 12.5 });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Total 12.50');
  });

  it('rejects a zero amount before saving and reuses the operation id after a failure', async () => {
    remember('Manager');
    const harness = await open('/expenses/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(harness, 'category', 'utilities');
    setControl(harness, 'amount', '0');
    setControl(harness, 'expenseDate', '2026-10-22');
    clickButton(root, 'Record expense');
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Amount must be greater than zero');
    TestBed.inject(HttpTestingController).expectNone((request) => request.method === 'POST');

    setControl(harness, 'amount', '8.75');
    clickButton(root, 'Record expense');
    await turn();
    const failed = TestBed.inject(HttpTestingController).expectOne('/api/expenses');
    const operationId = failed.request.body.operationId as string;
    expect(failed.request.body).toEqual({
      operationId,
      category: 'utilities',
      amount: 8.75,
      expenseDate: '2026-10-22',
      notes: null,
      paymentMethod: 'cash',
    });
    failed.flush({ error: 'The shop API could not be reached.' }, { status: 500, statusText: 'Error' });
    await turn();
    harness.detectChanges();
    clickButton(root, 'Record expense');
    await turn();
    const retry = TestBed.inject(HttpTestingController).expectOne('/api/expenses');
    expect(retry.request.body.operationId).toBe(operationId);
    retry.flush(expense('expense-2', 'utilities', 8.75));
    await turn();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/expenses/expense-2')
      .flush(expense('expense-2', 'utilities', 8.75));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Expense recorded.');
  });

  it('saves an edit and asks before removing the expense', async () => {
    remember('Admin');
    const harness = await open('/expenses/expense-1');
    TestBed.inject(HttpTestingController).expectOne('/api/expenses/expense-1').flush(expense());
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setControl(harness, 'amount', '15.00');
    clickButton(root, 'Save expense');
    await turn();
    const update = TestBed.inject(HttpTestingController).expectOne('/api/expenses/expense-1');
    expect(update.request.method).toBe('PUT');
    expect(update.request.body.amount).toBe(15);
    expect(update.request.body.operationId).toBeUndefined();
    update.flush(expense('expense-1', 'rent', 15));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Expense saved.');
    clickButton(root, 'Remove expense');
    await turn();
    harness.detectChanges();
    expect(text(harness)).toContain('Remove this expense?');
    TestBed.inject(HttpTestingController).expectNone((request) => request.method === 'DELETE');
    clickButton(root, 'Remove');
    await turn();
    const removed = TestBed.inject(HttpTestingController).expectOne('/api/expenses/expense-1');
    expect(removed.request.method).toBe('DELETE');
    removed.flush(null);
    await turn();
    flushList({ expenses: [], total: 0 });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Expense removed.');
  });

  it('hides write actions from inventory staff', async () => {
    remember('Inventory Staff');
    const harness = await open('/expenses/expense-1');
    TestBed.inject(HttpTestingController).expectOne('/api/expenses/expense-1').flush(expense());
    await turn();
    harness.detectChanges();

    const body = text(harness);
    expect(body).toContain('Only an Admin or Manager can change them.');
    expect(body).not.toContain('Save expense');
    expect(body).not.toContain('Remove expense');
  });
});

async function open(url = '/expenses'): Promise<RouterTestingHarness> {
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

function expense(id = 'expense-1', category = 'rent', amount = 12.5) {
  return {
    id,
    category,
    amount,
    expenseDate: '2026-10-22',
    notes: 'Shop rent',
    paymentMethod: 'cash',
    createdAt: '2026-10-22T08:00:00Z',
    updatedAt: '2026-10-22T08:00:00Z',
  };
}

function flushList(body: { expenses: ReturnType<typeof expense>[]; total: number }): void {
  TestBed.inject(HttpTestingController)
    .expectOne((request) => request.method === 'GET' && request.url === '/api/expenses' && request.params.get('offset') === '0')
    .flush(body);
}

function setControl(harness: RouterTestingHarness, name: string, value: string): void {
  const routed = harness.fixture.debugElement.query(By.directive(ExpenseListPage))
    ?? harness.fixture.debugElement.query(By.directive(ExpenseFormPage));
  const instance = (routed?.componentInstance ?? harness.fixture.componentInstance) as {
    form: { controls: Record<string, { setValue: (next: string) => void }> };
  };
  instance.form.controls[name].setValue(value);
  harness.detectChanges();
}

function clickButton(root: HTMLElement, label: string): void {
  const button = [...root.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === label,
  ) as HTMLButtonElement;
  button.click();
}

function text(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
