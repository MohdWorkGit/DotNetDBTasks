import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, map } from 'rxjs';
import { TranslocoService } from '@jsverse/transloco';
import { TranslationParams } from '../models/locale';
import { ConfirmDialogComponent, ConfirmDialogData } from '@shared/components/confirm-dialog.component';

/** A confirmation described by translation keys rather than sentences. */
export interface ConfirmRequest extends Omit<ConfirmDialogData, 'title' | 'message'> {
  titleKey: string;
  messageKey: string;
  /** Interpolation values for either key — e.g. `{ name: query.name }`. */
  params?: TranslationParams;
}

/**
 * Opens the shared confirm dialog. Keeps the MatDialog plumbing out of the six
 * components that need a confirmation, so a call site reads as one line.
 *
 * <p>Like ToastService, this resolves translation keys itself so call sites never inject
 * TranslocoService. Note the dialog renders `message` with `white-space: pre-wrap`, so a
 * catalog entry may contain `\n` to break a long warning into paragraphs.</p>
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private dialog = inject(MatDialog);
  private transloco = inject(TranslocoService);

  /** Emits true only when the user confirms; false on cancel, Escape, or backdrop click. */
  ask(request: ConfirmRequest): Observable<boolean> {
    const { titleKey, messageKey, params, ...rest } = request;
    const data: ConfirmDialogData = {
      ...rest,
      title: this.transloco.translate(titleKey, params),
      message: this.transloco.translate(messageKey, params)
    };

    return this.dialog
      // ariaModal defaults to false in Angular Material; set it so assistive tech
      // treats the backdrop content as inert (WCAG 4.1.2 / 1.3.2).
      .open(ConfirmDialogComponent, { data, width: '440px', autoFocus: 'dialog', ariaModal: true })
      .afterClosed()
      .pipe(map(result => result === true));
  }

  /** Convenience for the common case: run `action` only if the user confirms. */
  askThen(request: ConfirmRequest, action: () => void): void {
    this.ask(request).subscribe(ok => {
      if (ok) action();
    });
  }
}
