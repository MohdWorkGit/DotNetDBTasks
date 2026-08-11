import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { Router } from '@angular/router';
import { environment } from '@env/environment';
import { AuthResult, LoginRequest, RefreshTokenRequest } from '../models/auth.model';

@Injectable({
  providedIn: 'root'
})
export class AuthService {
  private readonly TOKEN_KEY = 'access_token';
  private readonly REFRESH_KEY = 'refresh_token';
  private readonly USER_KEY = 'user_data';

  private isAuthenticatedSubject = new BehaviorSubject<boolean>(this.hasToken());
  public isAuthenticated$ = this.isAuthenticatedSubject.asObservable();

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

  logout(): void {
    localStorage.removeItem(this.TOKEN_KEY);
    localStorage.removeItem(this.REFRESH_KEY);
    localStorage.removeItem(this.USER_KEY);
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

  isAdmin(): boolean {
    return this.getUserRoles().includes('Admin');
  }

  /** Reads execution logs and scheduled-task history. Nothing else under /admin. */
  isAuditor(): boolean {
    return this.getUserRoles().includes('Auditor');
  }

  /**
   * Manages who may reach each query and query group. Cannot read a query's SQL,
   * edit anything, or run anything — the API enforces all three.
   */
  isAccessManager(): boolean {
    return this.getUserRoles().includes('AccessManager');
  }

  isAdminOrAuditor(): boolean {
    return this.isAdmin() || this.isAuditor();
  }

  isAdminOrAccessManager(): boolean {
    return this.isAdmin() || this.isAccessManager();
  }

  /** Any role with at least one page under /admin. */
  canReachAdminArea(): boolean {
    return this.isAdmin() || this.isAuditor() || this.isAccessManager();
  }

  /**
   * Where a signed-in user belongs after login. Each role's first reachable page:
   * Admin and Access Manager both land on the query list, Auditors on the logs.
   */
  landingRoute(): string {
    if (this.isAdmin() || this.isAccessManager()) return '/admin/queries';
    if (this.isAuditor()) return '/admin/logs';
    return '/user/queries';
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

  private hasToken(): boolean {
    return !!localStorage.getItem(this.TOKEN_KEY);
  }
}
