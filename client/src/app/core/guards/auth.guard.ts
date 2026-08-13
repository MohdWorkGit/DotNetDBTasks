import { inject } from '@angular/core';
import { CanActivateFn, ActivatedRouteSnapshot, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Signed in, and holding at least one of the permissions the route names.
 *
 * <p>Routes used to name roles. They name capabilities now, so a role an administrator invents
 * reaches exactly the pages its permissions allow, with no client-side list to keep in step.
 * These mirror the `[RequirePermission]` attributes on the API — the server is the real gate;
 * this keeps the UI from offering a guaranteed 403.</p>
 */
export const authGuard: CanActivateFn = (route: ActivatedRouteSnapshot): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (!authService.getAccessToken()) {
    return router.createUrlTree(['/login']);
  }

  const required = route.data['permissions'] as string[] | undefined;
  if (required && required.length > 0 && !authService.hasAny(...required)) {
    // Bounce to the page this account actually has, not a blanket /user/queries — someone who
    // cannot run queries would land on a page that refuses them.
    return router.createUrlTree([authService.landingRoute()]);
  }

  return true;
};
