import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of, tap } from 'rxjs';
import { catchError, map } from 'rxjs/operators';
import { Router } from '@angular/router';
import { environment } from '@env/environment';
import { AuthResult, LoginRequest, RefreshTokenRequest } from '../models/auth.model';
import { PERM } from '../models/permissions';

/** What /api/auth/me answers: who you are and what your roles currently let you do. */
interface Identity {
  username: string;
  roles: string[];
  permissions: string[];
}

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly TOKEN_KEY = 'access_token';
  private readonly REFRESH_KEY = 'refresh_token';
  private readonly USER_KEY = 'user_data';
  private readonly PERMISSIONS_KEY = 'user_permissions';

  private isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasToken());
  public isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

  /**
   * Cached so a template can ask synchronously — an *ngIf cannot await. Refreshed from the
   * server on sign-in and on every application load, because permissions are edited at runtime
   * and the token that carries the roles is not reissued when they change.
   */
  private permissions = new Set<string>(this.readStoredPermissions());

  constructor(private http: HttpClient, private router: Router) {}

  login(request: LoginRequest): Observable<AuthResult> {
    return this.http.post<AuthResult>(`${environment.apiUrl}/auth/login`, request).pipe(
      tap(result => this.storeTokens(result))
    );
  }

  refreshToken(): Observable<AuthResult> {
    const request: RefreshTokenRequest = {
      accessToken: this.getAccessToken() || '',
      refreshToken: this.getRefreshToken() || ''
    };
    return this.http.post<AuthResult>(`${environment.apiUrl}/auth/refresh`, request).pipe(
      tap(result => this.storeTokens(result))
    );
  }

  /**
   * Pulls the current permission set. Call after sign-in and on application start; the result
   * is cached for the synchronous checks below.
   */
  loadPermissions(): Observable<string[]> {
    if (!this.getAccessToken()) {
      return of([]);
    }

    return this.http.get<Identity>(`${environment.apiUrl}/auth/me`).pipe(
      map(identity => identity.permissions || []),
      tap(permissions => {
        this.permissions = new Set(permissions);
        localStorage.setItem(this.PERMISSIONS_KEY, JSON.stringify(permissions));
      }),
      // Keep whatever was cached: a failed refresh should not blank the navigation of someone
      // who is still signed in. The server is the gate regardless.
      catchError(() => of([...this.permissions]))
    );
  }

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_KEY);
    localStorage.removeItem(this.USER_KEY);
    localStorage.removeItem(this.PERMISSIONS_KEY);
    this.permissions = new Set();
    this.isAuthenticatedSubject.next(false);
    this.router.navigate(['/login']);
  }

  getAccessToken(): string | null {
    return localStorage.getItem(this.TOKEN_KEY);
  }

  getRefreshToken(): string | null {
    return localStorage.getItem(this.REFRESH_KEY);
  }

  getUserRoles(): string[] {
    const userData = localStorage.getItem(this.USER_KEY);
    if (!userData) return [];
    try {
      return JSON.parse(userData).roles || [];
    } catch {
      return [];
    }
  }

  getUsername(): string {
    const userData = localStorage.getItem(this.USER_KEY);
    if (!userData) return '';
    try {
      return JSON.parse(userData).username || '';
    } catch {
      return '';
    }
  }

  /** True when the caller holds this capability. The one check the UI should be making. */
  has(permission: string): boolean {
    return this.permissions.has(permission);
  }

  /** True when the caller holds any of these — for a menu that opens onto several pages. */
  hasAny(...permissions: string[]): boolean {
    return permissions.some(p => this.permissions.has(p));
  }

  /**
   * The pinned role. Still meaningful after the move to permissions: Admin holds everything by
   * definition and its row cannot be edited, which is what the Permissions tab greys out.
   */
  isAdmin(): boolean {
    return this.getUserRoles().includes('Admin');
  }

  /** Any page under /admin at all. */
  canReachAdminArea(): boolean {
    return this.hasAny(
      PERM.queriesView, PERM.queriesManage, PERM.queryGroupsManage,
      PERM.usersView, PERM.userGroupsView, PERM.directoryView, PERM.databaseUsersManage,
      PERM.scheduledTasksViewAll, PERM.scheduledTasksManage,
      PERM.logsView, PERM.auditView, PERM.settingsManage, PERM.rolesManage);
  }

  /** My Queries and History are about queries this account can run. */
  canRunQueries(): boolean {
    return this.has(PERM.queriesRun);
  }

  /**
   * Where a signed-in user belongs after login: the first page their permissions actually open.
   * Ordered from most to least specific, so an Auditor lands on the logs rather than an empty
   * query list.
   */
  landingRoute(): string {
    if (this.canRunQueries()) return '/user/queries';
    if (this.has(PERM.queriesView)) return '/admin/queries';
    if (this.has(PERM.logsView)) return '/admin/logs';
    if (this.has(PERM.auditView)) return '/admin/system-audit';
    if (this.has(PERM.usersView)) return '/admin/users';
    if (this.has(PERM.userGroupsView)) return '/admin/user-groups';
    if (this.has(PERM.scheduledTasksViewAll)) return '/admin/scheduled-tasks';
    if (this.has(PERM.settingsManage) || this.has(PERM.rolesManage)) return '/admin/settings';
    return '/user/schedules';
  }

  private storeTokens(result: AuthResult): void {
    localStorage.setItem(this.TOKEN_KEY, result.accessToken);
    localStorage.setItem(this.REFRESH_KEY, result.refreshToken);
    localStorage.setItem(this.USER_KEY, JSON.stringify({
      username: result.username,
      roles: result.roles
    }));
    this.isAuthenticatedSubject.next(true);
  }

  private readStoredPermissions(): string[] {
    const raw = localStorage.getItem(this.PERMISSIONS_KEY);
    if (!raw) return [];
    try {
      return JSON.parse(raw) || [];
    } catch {
      return [];
    }
  }

  private hasToken(): boolean {
    return !!localStorage.getItem(this.TOKEN_KEY);
  }
}
