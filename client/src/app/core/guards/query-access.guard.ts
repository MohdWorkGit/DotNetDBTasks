import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';

/**
 * Keeps the query-running pages to accounts that hold <c>queries.run</c>.
 *
 * <p>The nav already hides My Queries and History from a role without it, but a typed URL or an
 * old bookmark still lands there — on a page the API can only ever answer with 403. Bouncing to
 * the role's own landing page says that more clearly than a blank screen does.</p>
 *
 * <p>Not a security boundary: the API resolves the permission per request and refuses the
 * endpoints outright. This is about not offering a dead end.</p>
 */
export const queryAccessGuard: CanActivateFn = (): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.canRunQueries()) {
    return true;
  }

  return router.createUrlTree([authService.landingRoute()]);
};
