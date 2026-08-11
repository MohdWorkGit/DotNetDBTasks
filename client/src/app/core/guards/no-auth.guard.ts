import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const noAuthGuard: CanActivateFn = (): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.getAccessToken()) {
    return router.createUrlTree([authService.landingRoute()]);
  }
  return true;
};

/**
 * Guard for the root path that always redirects:
 * authenticated users → their dashboard, unauthenticated → login.
 */
export const rootRedirectGuard: CanActivateFn = (): UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.getAccessToken()) {
    return router.createUrlTree([authService.landingRoute()]);
  }
  return router.createUrlTree(['/login']);
};
