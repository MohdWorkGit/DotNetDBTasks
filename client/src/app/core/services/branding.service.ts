import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, combineLatest, map, tap } from 'rxjs';
import { environment } from '@env/environment';
import { LanguageService } from './language.service';

export interface BrandingLogoInfo {
  hasLogo: boolean;
  fileName?: string | null;
  updatedAt?: string | null;
  hasFavicon?: boolean;
  faviconFileName?: string | null;
  faviconUpdatedAt?: string | null;
  /** Site name for the English UI; null or blank means "use the application name". */
  siteNameEn?: string | null;
  /** Site name for the Arabic UI, independent of the English one. */
  siteNameAr?: string | null;
}

/** Longest site name the server accepts — mirrors SystemSettingKeys.BrandingSiteNameMaxLength. */
export const SITE_NAME_MAX_LENGTH = 60;

/** Recommended favicon edge, in pixels — 32 px covers the tab and the bookmark bar. */
export const FAVICON_RECOMMENDED_SIZE = 32;
export const FAVICON_MAX_BYTES = 256 * 1024;
export const FAVICON_ACCEPT = '.png,.ico,.webp';

/** Displayed height of the banner logo, in CSS pixels. */
export const LOGO_DISPLAY_HEIGHT = 40;
/** Recommended source size — 2x the displayed height, so it stays sharp on HiDPI screens. */
export const LOGO_RECOMMENDED_HEIGHT = 80;
export const LOGO_RECOMMENDED_MAX_WIDTH = 480;
export const LOGO_MAX_BYTES = 1024 * 1024;
export const LOGO_ACCEPT = '.png,.jpg,.jpeg,.webp';

/**
 * What the top banner shows in place of the application name: an uploaded logo, or a site name
 * written per language.
 *
 * Holds the image URL rather than the bytes: the endpoint is anonymous precisely so a plain
 * `<img src>` can load it, which a blob fetched through the JWT interceptor would not allow
 * without juggling object URLs.
 */
@Injectable({ providedIn: 'root' })
export class BrandingService {
  private http = inject(HttpClient);
  private language = inject(LanguageService);
  private readonly baseUrl = `${environment.apiUrl}/branding`;

  private logoUrl = new BehaviorSubject<string | null>(null);
  /** Emits the logo URL, or null when no logo is set and the banner should show a name. */
  logoUrl$ = this.logoUrl.asObservable();

  private info = new BehaviorSubject<BrandingLogoInfo | null>(null);
  info$ = this.info.asObservable();

  private faviconUrl = new BehaviorSubject<string | null>(null);
  /** Emits the tab icon's URL, or null when none is stored. Drives the dialog's preview. */
  faviconUrl$ = this.faviconUrl.asObservable();

  constructor() {
    // Pointed at the endpoint before anyone signs in, so the sign-in page's tab already carries
    // the installation's icon. There is nothing to probe first: the endpoint 404s when no icon
    // is stored and the browser then shows its own default, which is what it did before this
    // existed. The response is `no-cache`, so revalidation — not a version token — is what
    // makes a replacement appear.
    this.applyFavicon();
  }

  /**
   * The site name for the language currently active, or null when none is configured for it
   * and the banner should fall back to the translated application name.
   *
   * Recomputed from the active locale rather than fetched per language, so both names are in
   * hand the moment either is needed. Switching language reloads the page today, but deriving
   * it this way means the banner would still be right if that ever stopped being true.
   */
  siteName$ = combineLatest([this.info$, this.language.activeLocale$]).pipe(
    map(([info, locale]) => {
      const name = locale === 'ar' ? info?.siteNameAr : info?.siteNameEn;
      return name?.trim() ? name.trim() : null;
    })
  );

  /**
   * Loads the current branding. Safe to call when signed out — a failure just leaves the
   * banner showing the application name rather than breaking the shell.
   */
  refresh(): void {
    this.http.get<BrandingLogoInfo>(this.baseUrl).subscribe({
      next: (info) => this.apply(info),
      error: () => this.apply({ hasLogo: false })
    });
  }

  /** Writes both names; a blank one clears that language back to the application name. */
  setSiteName(siteNameEn: string, siteNameAr: string): Observable<void> {
    return this.http
      .put<void>(`${this.baseUrl}/site-name`, { siteNameEn, siteNameAr })
      .pipe(tap(() => this.refresh()));
  }

  upload(file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<void>(`${this.baseUrl}/logo`, form).pipe(tap(() => this.refresh()));
  }

  remove(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/logo`).pipe(tap(() => this.refresh()));
  }

  uploadFavicon(file: File): Observable<void> {
    const form = new FormData();
    form.append('file', file, file.name);
    return this.http.post<void>(`${this.baseUrl}/favicon`, form).pipe(tap(() => this.refresh()));
  }

  removeFavicon(): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/favicon`).pipe(tap(() => this.refresh()));
  }

  private apply(info: BrandingLogoInfo): void {
    this.info.next(info);
    // The URL is constant, so without the timestamp the browser would keep showing the old
    // logo from cache after a replacement upload.
    const version = info.updatedAt ? encodeURIComponent(info.updatedAt) : '';
    this.logoUrl.next(info.hasLogo ? `${this.baseUrl}/logo?v=${version}` : null);

    const faviconVersion = info.faviconUpdatedAt ? encodeURIComponent(info.faviconUpdatedAt) : '';
    this.faviconUrl.next(info.hasFavicon ? `${this.baseUrl}/favicon?v=${faviconVersion}` : null);
    // Re-stamped with the upload's timestamp once it is known. Chrome in particular holds a
    // favicon well past its cache headers, and a changed URL is what reliably dislodges it.
    this.applyFavicon(faviconVersion);
  }

  /**
   * Points the document's `<link rel="icon">` at the branding endpoint, creating the element
   * when the page has none — index.html deliberately ships without one, so an installation
   * that never uploads an icon keeps the browser's default rather than a placeholder.
   */
  private applyFavicon(version = ''): void {
    const href = version ? `${this.baseUrl}/favicon?v=${version}` : `${this.baseUrl}/favicon`;
    let link = document.querySelector<HTMLLinkElement>('link[rel~="icon"]');
    if (!link) {
      link = document.createElement('link');
      link.rel = 'icon';
      document.head.appendChild(link);
    }
    if (link.href !== href) link.href = href;
  }
}
