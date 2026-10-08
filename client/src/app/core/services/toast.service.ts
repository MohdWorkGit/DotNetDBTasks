import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslocoService } from '@jsverse/transloco';
import { TranslationParams } from '../models/locale';

/**
 * Pulls a human-readable message out of an HTTP error.
 *
 * Handles the three error shapes this app actually produces:
 *   1. `{ error: { message: '...' } }`                  — the normal API error
 *   2. `{ error: { errors: { field: ['msg'] } } }`      — ASP.NET model validation
 *   3. `{ error: { message: 'Request timed out.' } }`   — the synthetic error the
 *      components' `timeout(30000)` / `catchError` pipes throw
 *
 * Extracting this once matters: the hand-rolled version in query-form.component.ts
 * relied on `||` binding tighter than `?:`, so an error carrying `message` but no
 * `errors` object called `Object.values(undefined)` and threw *inside* the error
 * callback — the snackbar never opened and a failed save showed the user nothing.
 *
 * The per-field `errors` win over `message` when both are present. A validation failure
 * carries both, and its `message` is only the envelope ("One or more validation errors
 * occurred") — preferring it meant every rejected save told the user that something was
 * wrong and never what.
 */
export function extractApiError(err: unknown, fallback: string): string {
  const reasons = extractValidationErrors(err);
  if (reasons.length) {
    return reasons.map(r => r.message).join(' ');
  }

  const body = (err as { error?: unknown } | null | undefined)?.error;

  if (typeof body === 'string' && body.trim()) {
    return body;
  }

  const message = (body as { message?: unknown } | null | undefined)?.message;
  if (typeof message === 'string' && message.trim()) {
    return message;
  }

  return fallback;
}

/** One reason a request was refused, with the field it was raised against (e.g. "Parameters[1].Name"). */
export interface ApiValidationError {
  field: string;
  message: string;
}

/**
 * Every per-field reason in a validation failure, in the order the server listed them — for a
 * form that wants to show them all rather than squeeze them into one toast. Empty when the
 * error is not a validation failure.
 */
export function extractValidationErrors(err: unknown): ApiValidationError[] {
  const body = (err as { error?: unknown } | null | undefined)?.error;
  const errors = (body as { errors?: unknown } | null | undefined)?.errors;
  if (!errors || typeof errors !== 'object') {
    return [];
  }

  const out: ApiValidationError[] = [];
  for (const [field, value] of Object.entries(errors as Record<string, unknown>)) {
    for (const message of [value].flat()) {
      if (typeof message === 'string' && message.trim() && !out.some(e => e.message === message && e.field === field)) {
        out.push({ field, message });
      }
    }
  }
  return out;
}

/**
 * Single entry point for transient user feedback.
 *
 * Wraps MatSnackBar so that every message gets a status colour and an announcement
 * politeness. Previously 95 call sites opened snackbars directly and exactly one
 * passed a panelClass, so success and failure were visually identical everywhere.
 *
 * <p>Call sites pass a **translation key**, not a sentence — `toast.success('admin.users.created')`.
 * Translating here rather than at each of the ~106 call sites keeps them one-liners and means no
 * component has to inject TranslocoService just to show a message. Anything that is not a known
 * key falls through unchanged, which is what lets server-generated text (already localized via
 * the Accept-Language header) be displayed verbatim.</p>
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private snackBar = inject(MatSnackBar);
  private transloco = inject(TranslocoService);

  /**
   * Third argument is either interpolation values or a duration in ms — existing call sites
   * pass a bare number, and both readings are unambiguous at runtime.
   */
  success(key: string, paramsOrDuration?: TranslationParams | number, duration = 3000): void {
    const [params, ms] = split(paramsOrDuration, duration);
    this.snackBar.open(this.text(key, params), this.closeLabel(), {
      duration: ms,
      panelClass: ['success-snackbar'],
      politeness: 'polite'
    });
  }

  /**
   * `err` may be an HttpErrorResponse or an already-extracted string; `fallbackKey`
   * is used when the response carries nothing readable.
   *
   * The server's own message wins when there is one — it arrives in the active language
   * because every request carries Accept-Language.
   */
  error(err: unknown, fallbackKey: string, paramsOrDuration?: TranslationParams | number, duration = 5000): void {
    const [params, ms] = split(paramsOrDuration, duration);
    const fallback = this.text(fallbackKey, params);
    const message = typeof err === 'string' ? this.text(err, params) : extractApiError(err, fallback);
    this.snackBar.open(message, this.closeLabel(), {
      duration: ms,
      panelClass: ['error-snackbar'],
      // Failures interrupt: they must be announced even if the user is mid-task.
      politeness: 'assertive'
    });
  }

  info(key: string, paramsOrDuration?: TranslationParams | number, duration = 4000): void {
    const [params, ms] = split(paramsOrDuration, duration);
    this.snackBar.open(this.text(key, params), this.closeLabel(), { duration: ms, politeness: 'polite' });
  }

  /**
   * Resolves a key, or returns the input untouched when it is not one. Transloco echoes the key
   * back on a miss, so that echo is the signal that this was a literal (a server message) rather
   * than a lookup failure.
   */
  private text(keyOrMessage: string, params?: TranslationParams): string {
    const translated = this.transloco.translate(keyOrMessage, params);
    return translated === keyOrMessage ? keyOrMessage : translated;
  }

  private closeLabel(): string {
    return this.transloco.translate('common.close');
  }
}

/** Disambiguates the overloaded third argument shared by success/error/info. */
function split(
  paramsOrDuration: TranslationParams | number | undefined,
  fallbackDuration: number
): [TranslationParams | undefined, number] {
  return typeof paramsOrDuration === 'number'
    ? [undefined, paramsOrDuration]
    : [paramsOrDuration, fallbackDuration];
}
