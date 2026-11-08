import { Component, inject } from '@angular/core';
import { TranslocoModule } from '@jsverse/transloco';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { OldRowsFetch, OldRowsTableComponent } from './old-rows-table.component';

export interface OldRowsDialogData {
  queryName: string;
  /** Endpoint-agnostic page fetcher (admin logs vs. user history). */
  fetch: OldRowsFetch;
}

/**
 * Shows the pre-change row snapshots of an execution log on their own, for the shortcut in the
 * log table's parameters column. The details dialog shows the same table alongside everything
 * else about the run.
 */
@Component({
  standalone: true,
  selector: 'app-old-rows-dialog',
  imports: [MatButtonModule, MatDialogModule, OldRowsTableComponent, TranslocoModule],
  template: `
    <h2 mat-dialog-title>{{ 'admin.logs.affectedRowsTitle' | transloco: { query: data.queryName } }}</h2>
    <mat-dialog-content>
      <app-old-rows-table [fetch]="data.fetch"></app-old-rows-table>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-stroked-button (click)="close()">{{ 'common.close' | transloco }}</button>
    </mat-dialog-actions>
  `
})
export class OldRowsDialogComponent {
  readonly dialogRef = inject(MatDialogRef<OldRowsDialogComponent>);
  readonly data = inject<OldRowsDialogData>(MAT_DIALOG_DATA);

  close(): void {
    this.dialogRef.close();
  }
}
