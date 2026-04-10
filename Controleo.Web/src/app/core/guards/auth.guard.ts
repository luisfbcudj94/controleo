import { inject } from '@angular/core';
import { CanActivateChildFn, CanActivateFn, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

async function canActivateInternal(): Promise<boolean | ReturnType<Router['createUrlTree']>> {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.initialize();
  if (auth.isAuthenticated) {
    return true;
  }

  return router.createUrlTree(['/login']);
}

export const authGuard: CanActivateFn = () => canActivateInternal();

export const authChildGuard: CanActivateChildFn = () => canActivateInternal();

export const adminGuard: CanActivateFn = async () => {
  const auth = inject(AuthService);
  const router = inject(Router);

  await auth.initialize();
  if (!auth.isAuthenticated) {
    return router.createUrlTree(['/login']);
  }

  if (auth.isAdmin) {
    return true;
  }

  return router.createUrlTree(['/dashboard']);
};
