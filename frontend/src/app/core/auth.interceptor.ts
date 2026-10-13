import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';

import { toApiError } from './api-error';
import { SessionService } from './session.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const session = inject(SessionService);
  const router = inject(Router);
  const token = session.accessToken();
  const outgoing = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(outgoing).pipe(
    catchError((error: unknown) => {
      const apiError = toApiError(error);
      const loginRequest = request.url.includes('/api/auth/login');
      if (apiError.status === 401 && !loginRequest) {
        session.clear();
        void router.navigateByUrl('/login');
      }

      return throwError(() => apiError);
    }),
  );
};
