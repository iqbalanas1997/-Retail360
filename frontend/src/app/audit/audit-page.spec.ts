import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('audit history', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('filters activity and does not show a secret detail', async () => {
    remember('Admin');
    const harness = await open('/audit');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/users?activeOnly=false&limit=200&offset=0').flush([
      { id: 'user-2', userName: 'Amina', role: 'Cashier', isActive: true, createdAt: '', updatedAt: '' },
    ]);
    const initial = http.expectOne('/api/audit?limit=50&offset=0');
    initial.flush([
      entry('user.created', { userName: 'Amina', role: 'Cashier', password: 'secret-value' }),
    ]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Amina');
    expect(text(harness)).toContain('User created');
    expect(text(harness)).not.toContain('secret-value');

    setInput(harness.fixture.nativeElement, 'from', '2026-10-22');
    setInput(harness.fixture.nativeElement, 'to', '2026-10-22');
    setSelect(harness.fixture.nativeElement, 'action', 'user.created');
    setSelect(harness.fixture.nativeElement, 'entityType', 'user');
    clickButton(harness.fixture.nativeElement, 'Apply');
    const filtered = http.expectOne(
      (request) =>
        request.url === '/api/audit' &&
        request.params.get('from') === '2026-10-22' &&
        request.params.get('to') === '2026-10-22' &&
        request.params.get('action') === 'user.created' &&
        request.params.get('entityType') === 'user' &&
        request.params.get('limit') === '50' &&
        request.params.get('offset') === '0',
    );
    filtered.flush([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('No activity matches this search.');
  });

  it('loads the next page', async () => {
    remember('Admin');
    const harness = await open('/audit');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/users?activeOnly=false&limit=200&offset=0').flush([]);
    http.expectOne('/api/audit?limit=50&offset=0').flush(
      Array.from({ length: 50 }, (_, index) => entry('sale.completed', { receiptNumber: String(index + 1) }, `audit-${index}`)),
    );
    await turn();
    harness.detectChanges();

    clickButton(harness.fixture.nativeElement, 'Show more');
    const more = http.expectOne('/api/audit?limit=50&offset=50');
    more.flush([entry('sale.completed', { receiptNumber: '51' }, 'audit-50')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Receipt 1');
    expect(text(harness)).toContain('Receipt 51');
  });

  it('shows the access error to a cashier', async () => {
    remember('Cashier');
    const harness = await open('/audit');
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/users?activeOnly=false&limit=200&offset=0')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    http
      .expectOne('/api/audit?limit=50&offset=0')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect(text(harness)).not.toContain('Show more');
  });
});

async function open(url: string): Promise<RouterTestingHarness> {
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

function entry(action: string, details: Record<string, string>, id = 'audit-1') {
  return {
    id,
    actorUserId: 'user-1',
    actorUserName: 'admin',
    action,
    entityType: action.split('.')[0],
    entityId: 'entity-1',
    occurredAt: '2026-10-22T00:30:00+00:00',
    details,
  };
}

function text(harness: RouterTestingHarness): string {
  return harness.fixture.nativeElement.textContent as string;
}

function setInput(root: HTMLElement, name: string, value: string): void {
  const input = root.querySelector(`[formcontrolname="${name}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function setSelect(root: HTMLElement, name: string, value: string): void {
  const select = root.querySelector(`[formcontrolname="${name}"]`) as HTMLSelectElement;
  select.value = value;
  select.dispatchEvent(new Event('change'));
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
