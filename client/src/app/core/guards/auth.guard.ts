import { Injectable } from '@angular/core';
import { CanActivate, ActivatedRouteSnapshot, Router } from '@angular/router';
import { AuthService } from '../services/auth.service';

@Injectable({
  providedIn: 'root'
})
export class AuthGuard implements CanActivate {
  constructor(private authService: AuthService, private router: Router) {}

  canActivate(route: ActivatedRouteSnapshot): boolean {
    if (!this.authService.getAccessToken()) {
      this.router.navigate(['/login']);
      return false;
    }

    const requiredRoles = route.data['roles'] as string[];
    if (requiredRoles && requiredRoles.length > 0) {
      const userRoles = this.authService.getUserRoles();
      const hasRole = requiredRoles.some(role => userRoles.includes(role));
      if (!hasRole) {
        this.router.navigate(['/user/queries']);
        return false;
      }
    }

    return true;
  }
}
