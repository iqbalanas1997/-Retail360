import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { writeStoredSession } from '../core/shop-session';
import { Shell } from './shell';

@Component({ template: 'Sign in' })
class LoginStub {}

describe('shell session expiry', () => {
  beforeEach(() => {
    sessionStorage.clear();
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
    const pos = [...root.querySelectorAll('a')].find((link) => link.textContent?.trim() === 'POS');
    pos?.click();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/pos');
    expect(root.querySelector('.shell')?.classList.contains('nav-open')).toBe(false);
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
