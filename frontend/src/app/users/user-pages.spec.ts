import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { routes } from '../app.routes';
import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';

describe('user screens', () => {
  beforeEach(() => {
    sessionStorage.clear();
    TestBed.resetTestingModule();
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('searches users and keeps the form when the list fails', async () => {
    remember('Admin');
    const harness = await open('/users');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/users?activeOnly=false&limit=50&offset=0').flush([user('Amina')]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('Amina');
    expect(text(harness)).toContain('New user');
    setInput(harness.fixture.nativeElement, 'name', 'Cash');
    clickButton(harness.fixture.nativeElement, 'Search');
    http
      .expectOne(
        (request) =>
          request.url === '/api/users' &&
          request.params.get('name') === 'Cash' &&
          request.params.get('activeOnly') === 'false' &&
          request.params.get('limit') === '50' &&
          request.params.get('offset') === '0',
      )
      .flush([]);
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('No users match this search.');
    clickButton(harness.fixture.nativeElement, 'Search');
    http
      .expectOne((request) => request.url === '/api/users')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect(text(harness)).not.toContain('No users match this search.');
  });

  it('hides user management from a cashier and shows the access error', async () => {
    remember('Cashier');
    const products = await open('/catalog/products');
    TestBed.inject(HttpTestingController).expectOne('/api/products?activeOnly=false&limit=50&offset=0').flush([]);
    await turn();
    products.detectChanges();

    expect(text(products)).not.toContain('Users');

    TestBed.resetTestingModule();
    remember('Cashier');
    const harness = await open('/users');
    TestBed.inject(HttpTestingController)
      .expectOne('/api/users?activeOnly=false&limit=50&offset=0')
      .flush({ error: 'You do not have access to this action.' }, { status: 403, statusText: 'Forbidden' });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You do not have access to this action.');
    expect(text(harness)).not.toContain('New user');
  });

  it('creates a user and does not send a blank password on a later save', async () => {
    remember('Admin');
    const harness = await open('/users/new');
    await turn();
    harness.detectChanges();
    const root = harness.fixture.nativeElement as HTMLElement;
    setInput(root, 'userName', '  counter  ');
    setInput(root, 'password', 'correct-horse');
    clickButton(root, 'Create user');
    const http = TestBed.inject(HttpTestingController);
    const created = http.expectOne({ method: 'POST', url: '/api/users' });
    expect(created.request.body).toEqual({
      userName: 'counter',
      role: 'Cashier',
      password: 'correct-horse',
    });
    expect(JSON.stringify(created.request.body)).not.toContain('passwordHash');
    created.flush(user('counter', 'user-2'));
    await turn();
    http.expectOne('/api/users/user-2').flush(user('counter', 'user-2'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('User created.');
    setInput(harness.fixture.nativeElement, 'userName', 'counter');
    clickButton(harness.fixture.nativeElement, 'Save user');
    const update = http.expectOne({ method: 'PUT', url: '/api/users/user-2' });
    expect(update.request.body).toEqual({
      userName: 'counter',
      role: 'Cashier',
      password: null,
    });
    update.flush(user('counter', 'user-2'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('User saved.');
  });

  it('does not offer to deactivate the signed-in admin', async () => {
    remember('Admin');
    const harness = await open('/users/user-1');
    TestBed.inject(HttpTestingController).expectOne('/api/users/user-1').flush(user('admin', 'user-1', 'Admin'));
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('You cannot deactivate or remove it.');
    expect(text(harness)).not.toContain('Deactivate');
    expect(text(harness)).not.toContain('Remove user');
    expect(text(harness)).toContain('Save user');
  });

  it('deactivates another user from the form', async () => {
    remember('Admin');
    const harness = await open('/users/user-2');
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/users/user-2').flush(user('counter', 'user-2'));
    await turn();
    harness.detectChanges();
    clickButton(harness.fixture.nativeElement, 'Deactivate');
    const request = http.expectOne({ method: 'POST', url: '/api/users/user-2/deactivate' });
    request.flush({ ...user('counter', 'user-2'), isActive: false });
    await turn();
    harness.detectChanges();

    expect(text(harness)).toContain('User is inactive.');
    expect(text(harness)).toContain('Activate');
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

function user(userName: string, id = 'user-2', role = 'Cashier') {
  return {
    id,
    userName,
    role,
    isActive: true,
    createdAt: '2026-10-23T00:00:00Z',
    updatedAt: '2026-10-23T00:00:00Z',
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

function clickButton(root: HTMLElement, label: string): void {
  const button = [...root.querySelectorAll('button')].find(
    (item) => item.textContent?.trim() === label,
  ) as HTMLButtonElement;
  button.click();
}

function turn(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}
