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
  });

  it('hides Audit from a Cashier', () => {
    remember(new Date(Date.now() + 60_000).toISOString(), 'Cashier');
    TestBed.configureTestingModule({
      imports: [Shell],
      providers: [provideRouter([{ path: 'login', component: LoginStub }])],
    });
    const fixture = TestBed.createComponent(Shell);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).not.toContain('Audit');
    expect(fixture.nativeElement.textContent).not.toContain('Users');
    expect(fixture.nativeElement.textContent).not.toContain('Customers');
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
