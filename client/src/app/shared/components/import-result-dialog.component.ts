import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { MatButtonModule } from '@angular/material/button';
import { MatDialogModule, MatDialogRef, MAT_DIALOG_DATA } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { ImportedQueryResult, QueryImportResult } from '@core/models/dynamic-query.model';
import { TransferImportResult } from '@core/models/transfer.model';

/**
 * Reports exactly what an import did. A toast would be the wrong shape here: an import can
 * rename queries and silently drop assignments whose roles or users do not exist on this
 * system, and those are things the admin has to act on rather than watch disappear.
 */
@Component({
  standalone: true,
  selector: 'app-import-result-dialog',
  imports: [CommonModule, MatButtonModule, MatDialogModule, MatIconModule, TranslocoModule],
  template: `
    <h2 mat-dialog-title class="title">
      <mat-icon aria-hidden="true">download_done</mat-icon>
      {{ titleKey | transloco: { count: data.importedCount } }}
    </h2>

    <mat-dialog-content>
      <p *ngIf="data.renamedCount" class="renamed-note">
        <mat-icon inline aria-hidden="true">info</mat-icon>
        {{ (data.renamedCount === 1 ? 'admin.queries.renamedOne' : 'admin.queries.renamedMany')
           | transloco: { count: data.renamedCount } }}
      </p>

      <!-- A query-only import lists its queries as before; a transfer file is sectioned by kind. -->
      <ng-container *ngFor="let section of sections">
        <h3 class="section-title" *ngIf="sections.length > 1">
          {{ section.titleKey | transloco: { count: section.items.length } }}
        </h3>
        <ul class="query-list">
          <li *ngFor="let q of section.items">
            <span class="name" dir="auto">{{ q.importedName }}</span>
            <span *ngIf="q.wasRenamed" class="from">{{ 'admin.queries.renamedFrom' | transloco: { name: q.originalName } }}</span>
          </li>
        </ul>
      </ng-container>

      <div *ngIf="data.warnings.length" class="warnings">
        <h3 class="warnings-title">
          <mat-icon inline aria-hidden="true">warning</mat-icon>
          {{ 'admin.queries.importWarnings' | transloco: { count: data.warnings.length } }}
        </h3>
        <ul>
          <li *ngFor="let w of data.warnings">{{ w }}</li>
        </ul>
      </div>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button mat-raised-button color="primary" (click)="dialogRef.close()" cdkFocusInitial>{{ 'common.done' | transloco }}</button>
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
    .query-list { margin: 0; padding-inline-start: 20px; }
    .section-title { margin: 12px 0 4px; font-size: 14px; font-weight: 600; color: var(--text-primary); }
    .query-list li { margin-bottom: 4px; }
    .name { font-weight: 500; }
    .from { margin-inline-start: 6px; font-size: 12px; color: var(--text-secondary); }
    .warnings {
      margin-top: 16px;
      padding: 12px;
      border-radius: 8px;
      background: var(--bg-surface);
      border-inline-start: 3px solid var(--status-warning);
    }
    .warnings-title {
      margin: 0 0 8px;
      font-size: 14px;
      display: flex;
      align-items: center;
      gap: 6px;
      color: var(--status-warning);
    }
    .warnings ul { margin: 0; padding-inline-start: 20px; }
    .warnings li { margin-bottom: 6px; font-size: 13px; color: var(--text-primary); }
  `]
})
export class ImportResultDialogComponent {
  readonly dialogRef = inject<MatDialogRef<ImportResultDialogComponent>>(MatDialogRef);
  readonly data = inject<QueryImportResult | TransferImportResult>(MAT_DIALOG_DATA);

  /** A transfer file can hold several kinds, so its title counts items, not queries. */
  readonly titleKey = 'reports' in this.data
    ? (this.data.importedCount === 1 ? 'admin.transfer.importedOne' : 'admin.transfer.importedMany')
    : (this.data.importedCount === 1 ? 'admin.queries.importedOne' : 'admin.queries.importedMany');

  /** Only the kinds that brought something in, in the order they were imported. */
  readonly sections: { titleKey: string; items: ImportedQueryResult[] }[] = [
    { titleKey: 'admin.transfer.sectionQueries', items: this.data.queries ?? [] },
    { titleKey: 'admin.transfer.sectionReports', items: (this.data as TransferImportResult).reports ?? [] },
    { titleKey: 'admin.transfer.sectionDashboards', items: (this.data as TransferImportResult).dashboards ?? [] }
  ].filter(s => s.items.length > 0);
}
