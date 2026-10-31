import { Component } from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';

import { authInterceptor } from '../core/auth.interceptor';
import { writeStoredSession } from '../core/shop-session';
import { PasswordPage } from './password-page';

@Component({ template: 'Sign in' })
class LoginStub {}

describe('PasswordPage', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    writeStoredSession({
      accessToken: 'header.payload.signature',
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-1',
      userName: 'cashier',
      roles: ['Cashier'],
    });
    await TestBed.configureTestingModule({
      imports: [PasswordPage],
      providers: [
        provideRouter([{ path: 'login', component: LoginStub }]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
    await TestBed.inject(Router).initialNavigation();
  });

  it('asks for both passwords before calling the API', () => {
    const fixture = TestBed.createComponent(PasswordPage);
    fixture.detectChanges();
    submit(fixture.nativeElement);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('Enter the current password and a new password.');
    TestBed.inject(HttpTestingController).expectNone('/api/auth/password');
    expect(sessionStorage.getItem('retail360.session')).not.toBeNull();
  });

  it('keeps the session when the current password is rejected', async () => {
    const fixture = TestBed.createComponent(PasswordPage);
    fixture.detectChanges();
    setInput(fixture.nativeElement, 'current-password', 'old-secret');
    setInput(fixture.nativeElement, 'new-password', 'new-secret');
    submit(fixture.nativeElement);

    const request = TestBed.inject(HttpTestingController).expectOne('/api/auth/password');
    expect(request.request.body).toEqual({ currentPassword: 'old-secret', newPassword: 'new-secret' });
    request.flush({ error: 'The current password is incorrect.' }, { status: 400, statusText: 'Bad Request' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('The current password is incorrect.');
    expect(sessionStorage.getItem('retail360.session')).not.toBeNull();
    expect(TestBed.inject(Router).url).not.toBe('/login');
  });

  it('clears the session after the password is changed', async () => {
    const fixture = TestBed.createComponent(PasswordPage);
    fixture.detectChanges();
    setInput(fixture.nativeElement, 'current-password', 'old-secret');
    setInput(fixture.nativeElement, 'new-password', 'new-secret');
    submit(fixture.nativeElement);

    TestBed.inject(HttpTestingController)
      .expectOne('/api/auth/password')
      .flush(null, { status: 204, statusText: 'No Content' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(sessionStorage.getItem('retail360.session')).toBeNull();
    expect(TestBed.inject(Router).url).toBe('/login');
  });
});

function setInput(root: HTMLElement, autocomplete: string, value: string): void {
  const input = root.querySelector(`input[autocomplete="${autocomplete}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function submit(root: HTMLElement): void {
  (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
}
