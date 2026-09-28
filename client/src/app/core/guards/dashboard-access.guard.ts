import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { PERM } from '../models/permissions';

/**
 * Keeps the dashboard pages to accounts that hold <c>dashboards.run</c>, for the same reason
 * <c>reportAccessGuard</c> exists: a typed URL would otherwise land on a page the API can only
 * answer with 403.
 *
 * <p>Not a security boundary: the API checks the permission, the dashboard grant and every
 * tile's query grant per request.</p>
 */
export const dashboardAccessGuard: CanActivateFn = (): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.has(PERM.dashboardsRun)) {
    return true;
  }

  return router.createUrlTree([authService.landingRoute()]);
};
