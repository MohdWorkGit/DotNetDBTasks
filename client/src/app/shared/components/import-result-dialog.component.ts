import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { QueryImportResult } from '@core/models/dynamic-query.model';

/**
 * Reports exactly what an import did. A toast would be the wrong shape here: an import can
 * rename queries and silently drop assignments whose roles or users do not exist on this
 * system, and those are things the admin has to act on rather than watch disappear.
 */
@Component({
  standalone: true,
  selector: 'app-import-result-dialog',
  imports: [CommonModule, MatButtonModule, MatDialogModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="title">
      <mat-icon aria-hidden="true">download_done</mat-icon>
      Imported {{ data.importedCount }}
      {{ data.importedCount === 1 ? 'query' : 'queries' }}
    </h2>

    <mat-dialog-content>
      <p *ngIf="data.renamedCount" class="renamed-note">
        <mat-icon inline aria-hidden="true">info</mat-icon>
        {{ data.renamedCount }} already existed by name and
        {{ data.renamedCount === 1 ? 'was' : 'were' }} imported as
        {{ data.renamedCount === 1 ? 'a copy' : 'copies' }} — nothing existing was changed.
      </p>

      <ul class="query-list">
        <li *ngFor="let q of data.queries">
          <span class="name">{{ q.importedName }}</span>
          <span *ngIf="q.wasRenamed" class="from">was "{{ q.originalName }}"</span>
        </li>
      </ul>

      <div *ngIf="data.warnings.length" class="warnings">
        <h3 class="warnings-title">
          <mat-icon inline aria-hidden="true">warning</mat-icon>
          Needs your attention ({{ data.warnings.length }})
        </h3>
        <ul>
          <li *ngFor="let w of data.warnings">{{ w }}</li>
        </ul>
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" (click)="dialogRef.close()" cdkFocusInitial>Done</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .title { display: flex; align-items: center; gap: 8px; }
    .renamed-note {
      margin: 0 0 12px;
      font-size: 13px;
      color: var(--text-secondary);
      display: flex;
      align-items: flex-start;
      gap: 6px;
    }
    .query-list { margin: 0; padding-left: 20px; }
    .query-list li { margin-bottom: 4px; }
    .name { font-weight: 500; }
    .from { margin-left: 6px; font-size: 12px; color: var(--text-secondary); }
    .warnings {
      margin-top: 16px;
      padding: 12px;
      border-radius: 8px;
      background: var(--bg-surface);
      border-left: 3px solid var(--status-warning);
    }
    .warnings-title {
      margin: 0 0 8px;
      font-size: 14px;
      display: flex;
      align-items: center;
      gap: 6px;
      color: var(--status-warning);
    }
    .warnings ul { margin: 0; padding-left: 20px; }
    .warnings li { margin-bottom: 6px; font-size: 13px; color: var(--text-primary); }
  `]
})
export class ImportResultDialogComponent {
  readonly dialogRef = inject<MatDialogRef<ImportResultDialogComponent>>(MatDialogRef);
  readonly data = inject<QueryImportResult>(MAT_DIALOG_DATA);
}
