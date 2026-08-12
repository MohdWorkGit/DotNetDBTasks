import { Injectable, inject } from '@angular/core';
import { BehaviorSubject } from 'rxjs';
import { TranslocoService } from '@jsverse/transloco';
import { AppLocale, DEFAULT_LOCALE, directionOf, isAppLocale } from '../models/locale';

/**
 * Owns the active locale and the document's `lang`/`dir`.
 *
 * Deliberately shaped like ThemeService — same BehaviorSubject, same localStorage-backed
 * preference read once at construction, same "write the change onto documentElement" step —
 * so the two runtime preferences behave identically and the toolbar toggles look alike.
 */
@Injectable({ providedIn: 'root' })
export class LanguageService {
  private readonly STORAGE_KEY = 'language-preference';
  private readonly transloco = inject(TranslocoService);

  private locale$ = new BehaviorSubject<AppLocale>(this.loadPreference());

  /** Emits on every switch; templates bind to this rather than polling `current()`. */
  activeLocale$ = this.locale$.asObservable();

  constructor() {
    this.apply(this.locale$.value);
  }

  current(): AppLocale {
    return this.locale$.value;
  }

  isRtl(): boolean {
    return directionOf(this.locale$.value) === 'rtl';
  }

  /** The locale a toggle would switch to — there are only two. */
  other(): AppLocale {
    return this.locale$.value === 'ar' ? 'en' : 'ar';
  }

  /**
   * Switches language and reloads the page.
   *
   * <p>The reload is deliberate. Two things in Angular Material latch their state when they
   * are created and do not react to a later change:</p>
   * <ul>
   *   <li><b>Direction.</b> @angular/cdk/bidi reads <c>dir</c> when an overlay is created, so
   *       any menu, dialog or snackbar already open stays mirrored the old way.</li>
   *   <li><b>MatPaginatorIntl labels.</b> A paginator already on screen keeps the strings it
   *       read at construction; pushing <c>changes</c> does not reliably re-read them.</li>
   * </ul>
   *
   * <p>Both are fixable individually with progressively more special-casing, and every new
   * Material widget is a fresh chance to miss one. Reloading is one line and correct for all
   * of them. The cost is real but small: switching language is rare, and the preference is
   * written before the reload so it survives. Everything else Transloco still handles live —
   * this is one build serving both languages, not a per-locale bundle.</p>
   */
  use(locale: AppLocale): void {
    if (locale === this.locale$.value) return;

    localStorage.setItem(this.STORAGE_KEY, locale);

    // Applied before reloading so the correct lang/dir is on <html> for the very first paint
    // of the new page, rather than flashing the old direction.
    this.locale$.next(locale);
    this.apply(locale);

    window.location.reload();
  }

  /**
   * The value for the Accept-Language header. Read synchronously by the interceptor on every
   * request so server-side messages come back in whatever is active right now.
   */
  acceptLanguageHeader(): string {
    const active = this.locale$.value;
    return active === DEFAULT_LOCALE ? active : `${active},${DEFAULT_LOCALE};q=0.8`;
  }

  private loadPreference(): AppLocale {
    const stored = localStorage.getItem(this.STORAGE_KEY);
    if (isAppLocale(stored)) return stored;

    // Fall back to the browser's preference the way ThemeService falls back to
    // prefers-color-scheme, so an Arabic-configured machine starts in Arabic.
    const browser = navigator.language?.split('-')[0];
    return isAppLocale(browser) ? browser : DEFAULT_LOCALE;
  }

  private apply(locale: AppLocale): void {
    this.transloco.setActiveLang(locale);

    // Angular Material reads direction from the document through @angular/cdk/bidi, so setting
    // it here is what mirrors the component library — there is no separate Material call.
    const root = document.documentElement;
    root.setAttribute('lang', locale);
    root.setAttribute('dir', directionOf(locale));
  }
}
