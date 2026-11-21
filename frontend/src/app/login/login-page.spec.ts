import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { authInterceptor } from '../core/auth.interceptor';
import { LoginPage } from './login-page';

@Component({ template: '' })
class DestinationStub {}

describe('LoginPage', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([
          { path: 'pos', component: DestinationStub },
          { path: 'catalog/products', component: DestinationStub },
        ]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    TestBed.inject(Router).initialNavigation();
  });

  it('asks for both fields before calling the API', () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    submit(fixture.nativeElement);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Enter the user name and password.');
    TestBed.inject(HttpTestingController).expectNone('/api/auth/login');
  });

  it('shows the API failure and does not store the password', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    setInput(fixture.nativeElement, 'username', 'admin');
    setInput(fixture.nativeElement, 'current-password', 'shop-secret');
    submit(fixture.nativeElement);

    const request = TestBed.inject(HttpTestingController).expectOne('/api/auth/login');
    expect(request.request.body).toEqual({ userName: 'admin', password: 'shop-secret' });
    request.flush({ error: 'The user name or password is incorrect.' }, { status: 401, statusText: 'Unauthorized' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The user name or password is incorrect.');
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('sends one sign-in request when the button is clicked twice', () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    setInput(fixture.nativeElement, 'username', 'admin');
    setInput(fixture.nativeElement, 'current-password', 'shop-secret');
    submit(fixture.nativeElement);
    submit(fixture.nativeElement);

    const requests = TestBed.inject(HttpTestingController).match('/api/auth/login');
    expect(requests).toHaveLength(1);
    requests[0].flush({ error: 'The user name or password is incorrect.' }, { status: 401, statusText: 'Unauthorized' });
  });

  it('opens the counter for a cashier and the catalog for an admin', async () => {
    const fixture = TestBed.createComponent(LoginPage);
    fixture.detectChanges();
    setInput(fixture.nativeElement, 'username', 'cashier');
    setInput(fixture.nativeElement, 'current-password', 'shop-secret');
    submit(fixture.nativeElement);
    TestBed.inject(HttpTestingController).expectOne('/api/auth/login').flush(session('Cashier'));
    await fixture.whenStable();

    expect(TestBed.inject(Router).url).toBe('/pos');

    fixture.destroy();
    sessionStorage.clear();
    const admin = TestBed.createComponent(LoginPage);
    admin.detectChanges();
    setInput(admin.nativeElement, 'username', 'admin');
    setInput(admin.nativeElement, 'current-password', 'shop-secret');
    submit(admin.nativeElement);
    TestBed.inject(HttpTestingController).expectOne('/api/auth/login').flush(session('Admin'));
    await admin.whenStable();

    expect(TestBed.inject(Router).url).toBe('/catalog/products');
  });
});

function session(role: string) {
  const body = btoa(JSON.stringify({ role })).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  return {
    accessToken: `header.${body}.signature`,
    tokenType: 'Bearer',
    expiresAt: new Date(Date.now() + 60_000).toISOString(),
    userId: 'user-1',
    userName: role,
  };
}

function setInput(root: HTMLElement, autocomplete: string, value: string): void {
  const input = root.querySelector(`input[autocomplete="${autocomplete}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function submit(root: HTMLElement): void {
  (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
}
