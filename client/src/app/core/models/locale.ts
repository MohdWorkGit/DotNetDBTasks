/**
 * Interpolation values for a translation key, e.g. `{ name: query.name }`.
 *
 * Transloco's own `HashMap` lives in an internal path that its package index does not
 * re-export, so this is declared locally rather than reached for through a deep import.
 */
export type TranslationParams = Record<string, unknown>;

/** The locales the UI ships translations for. */
export type AppLocale = 'en' | 'ar';

export const LOCALES: readonly AppLocale[] = ['en', 'ar'] as const;

export const DEFAULT_LOCALE: AppLocale = 'en';

/** Each locale's own name, so the switcher reads correctly whichever language is active. */
export const LOCALE_LABELS: Record<AppLocale, string> = {
  en: 'English',
  ar: 'العربية'
};

export function directionOf(locale: AppLocale): 'ltr' | 'rtl' {
  return locale === 'ar' ? 'rtl' : 'ltr';
}

export function isAppLocale(value: unknown): value is AppLocale {
  return typeof value === 'string' && (LOCALES as readonly string[]).includes(value);
}
