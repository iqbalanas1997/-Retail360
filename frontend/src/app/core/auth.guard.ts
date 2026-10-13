import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';

import { SessionService } from './session.service';

export const authGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  if (session.isSignedIn()) {
    return true;
  }

  return inject(Router).createUrlTree(['/login']);
};

export const guestGuard: CanActivateFn = () => {
  const session = inject(SessionService);
  if (!session.isSignedIn()) {
    return true;
  }

  return inject(Router).createUrlTree(['/catalog/products']);
};
