import { inject } from '@angular/core';
import { CanActivateFn, ActivatedRouteSnapshot, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

export const authGuard: CanActivateFn = (route: ActivatedRouteSnapshot): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.getAccessToken()) {
    return router.createUrlTree(['/login']);
  }

  const requiredRoles = route.data['roles'] as string[];
  if (requiredRoles && requiredRoles.length > 0) {
    const userRoles = authService.getUserRoles();
    const hasRole = requiredRoles.some(role => userRoles.includes(role));
    if (!hasRole) {
      // Bounce to the page this role actually has, not a blanket /user/queries —
      // an Auditor sent there would land on an empty query list.
      return router.createUrlTree([authService.landingRoute()]);
    }
  }

  return true;
};
