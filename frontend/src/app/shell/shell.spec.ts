import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { writeStoredSession } from '../core/shop-session';
import { NAV_COLLAPSED_KEY, NAV_GROUPS_KEY, Shell } from './shell';

@Component({ template: 'Sign in' })
class LoginStub {}

describe('shell session expiry', () => {
  beforeEach(() => {
    sessionStorage.clear();
    localStorage.clear();
    TestBed.resetTestingModule();
  });

  it('returns to sign-in when the open session reaches its expiry', async () => {
    remember(new Date(Date.now() + 30).toISOString());
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    await new Promise((resolve) => setTimeout(resolve, 80));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('returns to sign-in when the tab becomes visible after expiry', async () => {
    remember(new Date(Date.now() + 60_000).toISOString());
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    writeStoredSession({
      accessToken: token(),
      expiresAt: new Date(Date.now() - 1000).toISOString(),
      userId: 'user-1',
      userName: 'admin',
      roles: ['Admin'],
    });
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => 'visible' });
    document.dispatchEvent(new Event('visibilitychange'));
    fixture.detectChanges();
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/login');
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('shows Audit to an Admin', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Admin');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Audit');
    expect(fixture.nativeElement.textContent).toContain('Users');
    expect(fixture.nativeElement.textContent).toContain('Customers');
    expect(fixture.nativeElement.textContent).toContain('Password');
  });

  it('hides Audit from a Cashier', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    const text = fixture.nativeElement.textContent as string;
    expect(text).not.toContain('Audit');
    expect(text).not.toContain('Users');
    expect(text).not.toContain('Customers');
    expect(text).toContain('Password');
    expect(text).toContain('POS');
    expect(text.indexOf('POS')).toBeLessThan(text.indexOf('Products'));
  });

  it('hides POS from inventory staff and keeps stock available', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Inventory Staff');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;

    expect(text).not.toContain('POS');
    expect(text).not.toContain('Audit');
    expect(text).not.toContain('Users');
    expect(text).not.toContain('Customers');
    expect(text).toContain('Stock');
    expect(text).toContain('Opening');
    expect(text).toContain('Sales');
    expect(text).toContain('Purchases');
  });

  it('shows the manager shop links and hides Admin accounts', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Manager');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const text = fixture.nativeElement.textContent as string;

    expect(text).toContain('Dashboard');
    expect(text).toContain('Customers');
    expect(text).toContain('POS');
    expect(text.indexOf('POS')).toBeLessThan(text.indexOf('Products'));
    expect(text).not.toContain('Audit');
    expect(text).not.toContain('Users');
  });

  it('closes the menu after a route is chosen', async () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([
          { path: 'login', component: LoginStub },
          { path: 'pos', component: LoginStub },
        ]),
      ],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const menu = [...root.querySelectorAll('button')].find((button) => button.textContent?.trim() === 'Menu');
    menu?.click();
    fixture.detectChanges();

    expect(root.querySelector('.shell')?.classList.contains('nav-open')).toBe(true);
    const pos = [...root.querySelectorAll('a')].find((link) => link.getAttribute('title') === 'POS');
    pos?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(root.querySelector('.shell')?.classList.contains('nav-open')).toBe(false);
  });

  it('collapses the desktop rail and remembers the choice', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Admin');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const shell = root.querySelector('.shell') as HTMLElement;
    const collapse = root.querySelector('.nav-collapse') as HTMLButtonElement;

    expect(shell.classList.contains('nav-collapsed')).toBe(false);
    expect(collapse.getAttribute('aria-expanded')).toBe('true');
    collapse.click();
    fixture.detectChanges();

    expect(shell.classList.contains('nav-collapsed')).toBe(true);
    expect(localStorage.getItem(NAV_COLLAPSED_KEY)).toBe('1');
    expect(collapse.getAttribute('aria-label')).toBe('Expand menu');
    expect(root.querySelector('a[title="Audit"]')).not.toBeNull();
  });

  it('restores a collapsed rail from localStorage', () => {
    localStorage.setItem(NAV_COLLAPSED_KEY, '1');
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const text = root.textContent as string;

    expect(root.querySelector('.shell')?.classList.contains('nav-collapsed')).toBe(true);
    expect(text).toContain('POS');
    expect(text).not.toContain('Audit');
    expect(text).not.toContain('Customers');
  });

  it('collapses a nav group and remembers the choice', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Admin');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const people = [...root.querySelectorAll('.nav-label')].find((button) =>
      button.textContent?.includes('People'),
    ) as HTMLButtonElement;
    const peopleLinks = people.closest('.nav-group')?.querySelector('.nav-group-links') as HTMLElement;

    expect(people.getAttribute('aria-expanded')).toBe('true');
    expect(peopleLinks.hidden).toBe(false);
    people.click();
    fixture.detectChanges();

    expect(people.getAttribute('aria-expanded')).toBe('false');
    expect(peopleLinks.hidden).toBe(true);
    expect(localStorage.getItem(NAV_GROUPS_KEY)).toContain('"people":false');
    expect(root.querySelector('a[title="Audit"]')).not.toBeNull();
  });

  it('restores closed groups from localStorage and still shows Cashier links', () => {
    localStorage.setItem(
      NAV_GROUPS_KEY,
      JSON.stringify({ daily: true, catalog: false, people: false, management: true }),
    );
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const labels = [...root.querySelectorAll('.nav-label')].map((button) =>
      button.querySelector('.nav-label-text')?.textContent?.trim(),
    );

    expect(labels).toEqual(['Daily operations', 'Products and inventory', 'People']);
    expect(root.querySelector('a[title="Products"]')?.closest('.nav-group-links')?.hasAttribute('hidden')).toBe(
      true,
    );
    expect(root.querySelector('a[title="POS"]')?.closest('.nav-group-links')?.hasAttribute('hidden')).toBe(false);
    expect(root.querySelector('a[title="Audit"]')).toBeNull();
  });

  it('opens a closed group when navigating into it', async () => {
    localStorage.setItem(
      NAV_GROUPS_KEY,
      JSON.stringify({ daily: false, catalog: true, people: true, management: true }),
    );
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [
        provideRouter([
          { path: 'login', component: LoginStub },
          { path: 'pos', component: LoginStub },
        ]),
      ],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;
    const daily = [...root.querySelectorAll('.nav-label')].find((button) =>
      button.textContent?.includes('Daily operations'),
    ) as HTMLButtonElement;

    expect(daily.getAttribute('aria-expanded')).toBe('false');
    await TestBed.inject(Router).navigateByUrl('/pos');
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(daily.getAttribute('aria-expanded')).toBe('true');
    expect(localStorage.getItem(NAV_GROUPS_KEY)).toContain('"daily":true');
    expect(root.querySelector('.shell')?.classList.contains('nav-open')).toBe(false);
  });

  it('keeps group links visible when the rail is collapsed', () => {
    localStorage.setItem(NAV_COLLAPSED_KEY, '1');
    localStorage.setItem(
      NAV_GROUPS_KEY,
      JSON.stringify({ daily: false, catalog: false, people: false, management: false }),
    );
    remember(new Date(Date.now() + 60_000).toISOString(), 'Admin');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();
    const root = fixture.nativeElement as HTMLElement;

    expect(root.querySelector('.shell')?.classList.contains('nav-collapsed')).toBe(true);
    expect(root.querySelector('a[title="Sales"]')?.closest('.nav-group-links')?.hasAttribute('hidden')).toBe(false);
    expect(root.querySelector('a[title="Products"]')?.closest('.nav-group-links')?.hasAttribute('hidden')).toBe(
      false,
    );
    expect(root.querySelector('a[title="Audit"]')?.closest('.nav-group-links')?.hasAttribute('hidden')).toBe(false);
  });
});

function remember(expiresAt: string, role = 'Admin'): void {
  writeStoredSession({
    accessToken: token(),
    expiresAt,
    userId: 'user-1',
    userName: role,
    roles: [role],
  });
}

function token(): string {
  const body = btoa(JSON.stringify({ role: 'Admin' })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  return `header.${body}.signature`;
}
