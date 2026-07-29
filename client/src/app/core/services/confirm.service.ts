import { Injectable, inject } from '@angular/core';
import { MatDialog } from '@angular/material/dialog';
import { Observable, map } from 'rxjs';
import { ConfirmDialogComponent, ConfirmDialogData } from '@shared/components/confirm-dialog.component';

/**
 * Opens the shared confirm dialog. Keeps the MatDialog plumbing out of the six
 * components that need a confirmation, so a call site reads as one line.
 */
@Injectable({ providedIn: 'root' })
export class ConfirmService {
  private dialog = inject(MatDialog);

  /** Emits true only when the user confirms; false on cancel, Escape, or backdrop click. */
  ask(data: ConfirmDialogData): Observable<boolean> {
    return this.dialog
      // ariaModal defaults to false in Angular Material; set it so assistive tech
      // treats the backdrop content as inert (WCAG 4.1.2 / 1.3.2).
      .open(ConfirmDialogComponent, { data, width: '440px', autoFocus: 'dialog', ariaModal: true })
      .afterClosed()
      .pipe(map(result => result === true));
  }

  /** Convenience for the common case: run `action` only if the user confirms. */
  askThen(data: ConfirmDialogData, action: () => void): void {
    this.ask(data).subscribe(ok => {
      if (ok) action();
    });
  }
}
