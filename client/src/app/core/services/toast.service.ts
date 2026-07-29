import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';

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
 */
export function extractApiError(err: unknown, fallback: string): string {
  const body = (err as { error?: unknown } | null | undefined)?.error;

  if (typeof body === 'string' && body.trim()) {
    return body;
  }

  const message = (body as { message?: unknown } | null | undefined)?.message;
  if (typeof message === 'string' && message.trim()) {
    return message;
  }

  const errors = (body as { errors?: unknown } | null | undefined)?.errors;
  if (errors && typeof errors === 'object') {
    const flattened = Object.values(errors as Record<string, unknown>)
      .flat()
      .filter((v): v is string => typeof v === 'string' && v.trim().length > 0);
    if (flattened.length) {
      return flattened.join(', ');
    }
  }

  return fallback;
}

/**
 * Single entry point for transient user feedback.
 *
 * Wraps MatSnackBar so that every message gets a status colour and an announcement
 * politeness. Previously 95 call sites opened snackbars directly and exactly one
 * passed a panelClass, so success and failure were visually identical everywhere.
 */
@Injectable({ providedIn: 'root' })
export class ToastService {
  private snackBar = inject(MatSnackBar);

  success(message: string, duration = 3000): void {
    this.snackBar.open(message, 'Close', {
      duration,
      panelClass: ['success-snackbar'],
      politeness: 'polite'
    });
  }

  /**
   * `err` may be an HttpErrorResponse or an already-extracted string; `fallback`
   * is used when the response carries nothing readable.
   */
  error(err: unknown, fallback: string, duration = 5000): void {
    const message = typeof err === 'string' ? err : extractApiError(err, fallback);
    this.snackBar.open(message, 'Close', {
      duration,
      panelClass: ['error-snackbar'],
      // Failures interrupt: they must be announced even if the user is mid-task.
      politeness: 'assertive'
    });
  }

  info(message: string, duration = 4000): void {
    this.snackBar.open(message, 'Close', { duration, politeness: 'polite' });
  }
}
