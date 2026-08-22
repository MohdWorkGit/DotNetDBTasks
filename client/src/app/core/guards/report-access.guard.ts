import { inject } from '@angular/core';
import { CanActivateFn, Router, UrlTree } from '@angular/router';
import { AuthService } from '../services/auth.service';
import { PERM } from '../models/permissions';

/**
 * Keeps the report-running pages to accounts that hold <c>reports.run</c>.
 *
 * <p>The same reasoning as <c>queryAccessGuard</c>, against a different capability: running a
 * report is a separate grant from running a query, because a report assembles several of them
 * into a document. A typed URL would otherwise land on a page the API can only answer with 403.</p>
 *
 * <p>Not a security boundary: the API resolves the permission per request and refuses the
 * endpoints outright. This is about not offering a dead end.</p>
 */
export const reportAccessGuard: CanActivateFn = (): boolean | UrlTree => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.has(PERM.reportsRun)) {
    return true;
  }

  return router.createUrlTree([authService.landingRoute()]);
};
