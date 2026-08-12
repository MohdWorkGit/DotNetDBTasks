import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { TranslocoModule } from '@jsverse/transloco';

export interface ConfirmDialogData {
  /** Already-translated text — ConfirmService resolves keys before opening the dialog. */
  title: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  /** Colours the confirm button 'warn' and shows a warning icon. */
  destructive?: boolean;
}

/**
 * Themed replacement for the native `window.confirm()`, which is unstyled, renders
 * in OS light chrome even when the app is in dark mode, can be suppressed by the
 * browser, and blocks the JS thread.
 *
 * Resolves to `true` on confirm and `undefined` on cancel/backdrop/Escape, so call
 * sites can guard with a simple `if (!ok) return;`.
 */
@Component({
  standalone: true,
  selector: 'app-confirm-dialog',
  imports: [CommonModule, MatButtonModule, MatDialogModule, MatIconModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title class="title">
      <mat-icon *ngIf="data.destructive" class="warn-icon" aria-hidden="true">warning</mat-icon>
      {{ data.title }}
    </h2>
    <mat-dialog-content>
      <p class="message">{{ data.message }}</p>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="dialogRef.close()">
        {{ data.cancelText || ('common.cancel' | transloco) }}
      </button>
      <button mat-raised-button
              [color]="data.destructive ? 'warn' : 'primary'"
              (click)="dialogRef.close(true)"
              cdkFocusInitial>
        {{ data.confirmText || ('common.confirm' | transloco) }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title { display: flex; align-items: center; gap: 8px; }
    .warn-icon { color: var(--status-warning); }
    .message { margin: 0; color: var(--text-primary); white-space: pre-wrap; }
  `]
})
export class ConfirmDialogComponent {
  readonly dialogRef = inject<MatDialogRef<ConfirmDialogComponent, true | undefined>>(MatDialogRef);
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
}
