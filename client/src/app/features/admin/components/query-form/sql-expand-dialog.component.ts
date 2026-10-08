import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoModule } from '@jsverse/transloco';
import { SqlEditorComponent } from '@shared/components/sql-editor.component';

export interface SqlExpandDialogData {
  /** The query form's own sqlQuery control — editing here edits the query. */
  control: FormControl<string>;
  problemPattern: RegExp;
  maxLength: number;
  /** The form's current SQL messages (forbidden text, too long), re-read on every change. */
  problems: () => string[];
}

/**
 * The SQL editor at almost full screen, for reading and editing a long query. It is bound to the
 * same form control as the box on the page, so there is nothing to apply: closing (Done, Esc or
 * the backdrop) keeps every edit. The same highlighting, messages and counter are shown here so
 * nothing has to be checked back on the page.
 */
@Component({
  standalone: true,
  selector: 'app-sql-expand-dialog',
  imports: [
    CommonModule, ReactiveFormsModule, MatDialogModule, MatButtonModule, MatIconModule, MatTooltipModule,
    TranslocoModule, SqlEditorComponent
  ],
  template: `
    <div class="frame">
      <div class="head">
        <h2 mat-dialog-title>{{ 'admin.queryForm.sqlParameterized' | transloco }}</h2>
        <button mat-icon-button mat-dialog-close type="button"
                [attr.aria-label]="'admin.queryForm.collapseSql' | transloco"
                [matTooltip]="'admin.queryForm.collapseSql' | transloco">
          <mat-icon>close_fullscreen</mat-icon>
        </button>
      </div>

      <div class="body">
        <app-sql-editor [formControl]="data.control" [fill]="true" [autoFocus]="true"
                        [problemPattern]="data.problemPattern"
                        [ariaLabel]="'admin.queryForm.sqlParameterized' | transloco"></app-sql-editor>
      </div>

      <div class="foot">
        <div class="messages">
          <span *ngFor="let p of data.problems()" class="problem" role="alert">{{ p }}</span>
        </div>
        <span class="counter" dir="ltr">{{ data.control.value?.length || 0 }} / {{ data.maxLength }}</span>
        <button mat-raised-button color="primary" mat-dialog-close type="button">{{ 'admin.queryForm.sqlDone' | transloco }}</button>
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .frame { display: flex; flex-direction: column; height: 100%; }
    .head { display: flex; align-items: center; justify-content: space-between; padding-inline-end: 12px; }
    .head h2 { margin: 0; }
    .body { flex: 1; min-height: 0; padding: 0 24px; }
    .foot { display: flex; align-items: center; gap: 16px; padding: 12px 24px 16px; }
    .messages { flex: 1; display: flex; flex-direction: column; font-size: 12px; }
    .problem { color: var(--status-error); }
    .counter { font-size: 12px; color: var(--text-secondary); white-space: nowrap; }
  `]
})
export class SqlExpandDialogComponent {
  readonly data: SqlExpandDialogData = inject(MAT_DIALOG_DATA);
}
