import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, tap } from 'rxjs';
import { environment } from '@env/environment';

export interface BrandingLogoInfo {
  hasLogo: boolean;
  fileName?: string | null;
  updatedAt?: string | null;
}

/** Displayed height of the banner logo, in CSS pixels. */
export const LOGO_DISPLAY_HEIGHT = 40;
/** Recommended source size — 2x the displayed height, so it stays sharp on HiDPI screens. */
export const LOGO_RECOMMENDED_HEIGHT = 80;
export const LOGO_RECOMMENDED_MAX_WIDTH = 480;
export const LOGO_MAX_BYTES = 1024 * 1024;
export const LOGO_ACCEPT = '.png,.jpg,.jpeg,.webp';

/**
 * The site logo shown in the top banner in place of the app name.
 *
 * Holds the image URL rather than the bytes: the endpoint is anonymous precisely so a plain
 * `<img src>` can load it, which a blob fetched through the JWT interceptor would not allow
 * without juggling object URLs.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private http = inject(HttpClient);
  private readonly baseUrl = `${environment.apiUrl}/branding`;

  private logoUrl = new BehaviorSubject<string | null>(null);
  /** Emits the logo URL, or null when no logo is set and the banner should show the app name. */
  logoUrl$ = this.logoUrl.asObservable();

  private info = new BehaviorSubject<BrandingLogoInfo | null>(null);
  info$ = this.info.asObservable();

  /**
   * Loads the current logo state. Safe to call when signed out — a failure just leaves the
   * banner showing the app name rather than breaking the shell.
   */
  refresh(): void {
    this.http.get<BrandingLogoInfo>(`${this.baseUrl}/logo/info`).subscribe({
      next: (info) => this.apply(info),
      error: () => this.apply({ hasLogo: false })
    });
  }

  upload(file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<void>(`${this.baseUrl}/logo`, form).pipe(tap(() => this.refresh()));
  }

  remove(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/logo`).pipe(tap(() => this.refresh()));
  }

  private apply(info: BrandingLogoInfo): void {
    this.info.next(info);
    // The URL is constant, so without the timestamp the browser would keep showing the old
    // logo from cache after a replacement upload.
    const version = info.updatedAt ? encodeURIComponent(info.updatedAt) : '';
    this.logoUrl.next(info.hasLogo ? `${this.baseUrl}/logo?v=${version}` : null);
  }
}
