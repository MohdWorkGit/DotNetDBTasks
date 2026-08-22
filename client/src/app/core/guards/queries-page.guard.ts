import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { PERM } from '../models/permissions';

/**
 * Gates the shared "My Queries" page, which lists queries and reports together.
 *
 * <p>Either capability opens it, because either kind of thing can appear on it: someone who may
 * only run reports still has a list to look at. The narrower <c>queryAccessGuard</c> still
 * protects the query runner itself, so this does not hand anyone a query they could not run.</p>
 */
export const queriesPageGuard: CanActivateFn = (): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.canRunQueries() || authService.has(PERM.reportsRun)) {
    return true;
  }

  return router.createUrlTree([authService.landingRoute()]);
};
