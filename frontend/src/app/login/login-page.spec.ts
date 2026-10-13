import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '../core/auth.interceptor';
import { LoginPage } from './login-page';

describe('LoginPage', () => {
  beforeEach(async () => {
    sessionStorage.clear();
    await TestBed.configureTestingModule({
      imports: [LoginPage],
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    }).compileComponents();
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
});

function setInput(root: HTMLElement, autocomplete: string, value: string): void {
  const input = root.querySelector(`input[autocomplete="${autocomplete}"]`) as HTMLInputElement;
  input.value = value;
  input.dispatchEvent(new Event('input'));
}

function submit(root: HTMLElement): void {
  (root.querySelector('button[type="submit"]') as HTMLButtonElement).click();
}
