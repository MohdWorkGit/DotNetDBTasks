import { Component, OnInit, ChangeDetectorRef, ElementRef, ViewChild, inject } from '@angular/core';
import { AbstractControl, FormBuilder, FormControl, FormGroup, FormArray, ValidationErrors, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService, extractApiError, extractValidationErrors } from '@core/services/toast.service';
import { SqlEditorComponent } from '@shared/components/sql-editor.component';
import { MatDialog } from '@angular/material/dialog';
import { QueryTestDialogComponent, QueryTestDialogData } from './query-test-dialog.component';
import { SqlExpandDialogComponent, SqlExpandDialogData } from './sql-expand-dialog.component';
import { ConfirmService } from '@core/services/confirm.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { DatabaseUser, DropdownOption, DropdownSourceType, DynamicQuery, ParameterType, QueryGroup } from '@core/models/dynamic-query.model';
import { EXPORT_FORMATS } from '@core/models/export-formats';
import { TranslocoService } from '@jsverse/transloco';

/** Mirrors the server's rules (CreateDynamicQueryValidator), so the form can say no first. */
const PARAM_NAME_VALIDATORS = [
  Validators.required, Validators.maxLength(100), Validators.pattern(/^[a-zA-Z_][a-zA-Z0-9_]*$/)
];
const PARAM_DISPLAY_NAME_VALIDATORS = [Validators.required, Validators.maxLength(200)];

/**
 * Mirrors SqlSafetyRules on the server: no "--" line comments, no semicolons, and no calls into
 * system procedures or packages. Checked here too so the author hears about a trailing ";" while
 * typing, not only after pressing Save. The error carries what was found, for the message.
 */
const FORBIDDEN_SQL = /\b(XP_|SP_|DBMS_|UTL_)|--|;/i;
function sqlSafetyValidator(control: AbstractControl): ValidationErrors | null {
  const text: string = control.value ?? '';
  // Every occurrence, not just the first: the editor highlights them all, so the messages must
  // describe them all, or a highlighted line would have no explanation until another was fixed.
  const all: ForbiddenSql[] = [...text.matchAll(new RegExp(FORBIDDEN_SQL.source, 'gi'))]
    .map((m, index) => ({ found: m[0], line: text.slice(0, m.index).split('\n').length, index }));
  return all.length ? { forbiddenSql: all } : null;
}

/** One forbidden occurrence; `index` is its position among the editor's highlights. */
interface ForbiddenSql { found: string; line: number; index: number; }

/** One line in the save-problems panel; `sqlIndex` ones get a "Show" link to that highlight. */
interface SaveProblem { text: string; sqlIndex?: number; }

@Component({
  standalone: false,
  selector: 'app-query-form',
  template: `
    <div class="container">
      <h2>{{ (isEdit ? 'admin.queryForm.editTitle' : (isCopy ? 'admin.queryForm.copyTitle' : 'admin.queryForm.createTitle')) | transloco }}</h2>

      <mat-card>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.queries.name' | transloco }}</mat-label>
              <input matInput formControlName="name">
              <mat-error *ngIf="form.get('name')?.hasError('required')">{{ 'common.nameRequired' | transloco }}</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.queries.description' | transloco }}</mat-label>
              <textarea matInput formControlName="description" rows="3"></textarea>
              <mat-error *ngIf="form.get('description')?.hasError('required')">{{ 'common.descriptionRequired' | transloco }}</mat-error>
            </mat-form-field>

            <!-- A code editor rather than a mat-form-field: the outlined field's notched label
                 cannot sit over a multi-line editor, so the label, hint and errors are laid out
                 here in the same places Material would put them. -->
            <div class="sql-field" [class.sql-invalid]="sqlShowsError">
              <div class="sql-label-row">
                <label class="sql-label" id="sql-label">
                  {{ 'admin.queryForm.sqlParameterized' | transloco }} *
                </label>
                <button mat-icon-button type="button" class="expand-sql" (click)="expandSql()"
                        [attr.aria-label]="'admin.queryForm.expandSql' | transloco"
                        [matTooltip]="'admin.queryForm.expandSql' | transloco">
                  <mat-icon>open_in_full</mat-icon>
                </button>
              </div>
              <app-sql-editor #sqlEditor formControlName="sqlQuery" [problemPattern]="forbiddenSqlPattern"
                              [placeholder]="'admin.queryForm.sqlPlaceholder' | transloco"
                              [ariaLabel]="'admin.queryForm.sqlParameterized' | transloco"></app-sql-editor>
              <div class="sql-subscript">
                <span *ngIf="!sqlShowsError && !sqlForbiddenMessages.length" class="sql-hint">{{ 'admin.queryForm.sqlHint' | transloco }}</span>
                <span *ngIf="sqlShowsError && form.get('sqlQuery')?.hasError('required')" class="sql-error" role="alert">
                  {{ 'admin.queryForm.sqlRequired' | transloco }}
                </span>
                <span *ngIf="sqlShowsError && form.get('sqlQuery')?.hasError('maxlength')" class="sql-error" role="alert">
                  {{ 'admin.queryForm.sqlTooLong' | transloco: { max: sqlMaxLength } }}
                </span>
                <span *ngIf="sqlForbiddenMessages.length" class="sql-error" role="alert">
                  <span *ngFor="let message of sqlForbiddenMessages" class="sql-error-line">{{ message }}</span>
                </span>
                <span class="sql-counter" dir="ltr">{{ form.get('sqlQuery')?.value?.length || 0 }} / {{ sqlMaxLength }}</span>
              </div>
            </div>

            <mat-form-field appearance="outline">
              <mat-label>{{ 'admin.queryForm.timeout' | transloco }}</mat-label>
              <input matInput type="number" min="0" formControlName="timeoutSeconds">
              <mat-hint>{{ 'admin.queryForm.timeoutHint' | transloco }}</mat-hint>
            </mat-form-field>

            <div class="toggle-row">
              <mat-slide-toggle formControlName="isLongRunning">
                {{ 'admin.queryForm.longRunning' | transloco }}
              </mat-slide-toggle>
              <app-hint-icon [text]="'admin.queryForm.longRunningHint' | transloco"></app-hint-icon>
            </div>

            <div class="toggle-row">
              <mat-slide-toggle formControlName="allowRunWithoutConfirmation">
                {{ 'admin.queryForm.allowNoConfirm' | transloco }}
              </mat-slide-toggle>
              <app-hint-icon [text]="'admin.queryForm.allowNoConfirmHint' | transloco"></app-hint-icon>
            </div>

            <div class="toggle-row">
              <mat-slide-toggle formControlName="saveOldValues">
                {{ 'admin.queryForm.saveOldValues' | transloco }}
              </mat-slide-toggle>
              <app-hint-icon [text]="'admin.queryForm.saveOldValuesHint' | transloco"></app-hint-icon>
            </div>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'admin.queryForm.databaseUser' | transloco }}</mat-label>
              <mat-select formControlName="databaseUserId">
                <mat-option [value]="null">{{ 'admin.queryForm.defaultConnection' | transloco }}</mat-option>
                <mat-option *ngFor="let du of availableDbUsers" [value]="du.id">
                  {{ du.name }}
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!lookupLoadFailed.dbUsers">{{ 'admin.queryForm.databaseUserHint' | transloco }}</mat-hint>
              <mat-hint *ngIf="lookupLoadFailed.dbUsers" class="load-failed-hint">
                {{ 'admin.queryForm.dbUsersLoadFailed' | transloco }}
              </mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>{{ 'admin.queries.group' | transloco }}</mat-label>
              <mat-select formControlName="queryGroupId">
                <mat-option [value]="null">{{ 'admin.queryForm.ungrouped' | transloco }}</mat-option>
                <mat-option *ngFor="let g of availableGroups" [value]="g.id">
                  {{ g.name }}
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!lookupLoadFailed.groups">{{ 'admin.queryForm.groupHint' | transloco }}</mat-hint>
              <mat-hint *ngIf="lookupLoadFailed.groups" class="load-failed-hint">
                {{ 'admin.queryForm.groupsLoadFailed' | transloco }}
              </mat-hint>
            </mat-form-field>

            <div class="export-section">
              <h3>
                {{ 'admin.queryForm.exportSection' | transloco }}
                <app-hint-icon [text]="'admin.queryForm.exportHint' | transloco"></app-hint-icon>
              </h3>
              <div class="export-formats">
                <mat-checkbox *ngFor="let f of exportFormats"
                              [checked]="isExportFormatAllowed(f.name)"
                              (change)="toggleExportFormat(f.name, $event.checked)">
                  {{ f.labelKey | transloco }}
                </mat-checkbox>
              </div>
              <p class="field-hint export-off" *ngIf="allowedExportFormats.length === 0">
                {{ 'admin.queryForm.exportDisabled' | transloco }}
              </p>
            </div>

            <mat-slide-toggle *ngIf="isEdit" formControlName="isEnabled" class="toggle">
              {{ 'admin.queryForm.enabled' | transloco }}
            </mat-slide-toggle>

            <div *ngIf="isEdit" class="template-section">
              <h3>
                {{ 'admin.queryForm.wordTemplate' | transloco }}
                <app-hint-icon
                  [text]="'admin.queryForm.wordTemplateHint' | transloco: templateTokens"></app-hint-icon>
              </h3>
              <div class="template-row">
                <input #tplInput type="file" accept=".docx" hidden (change)="onTemplateSelected($event)">
                <button mat-stroked-button type="button" (click)="tplInput.click()"
                        [disabled]="uploadingTemplate">
                  <mat-icon>upload_file</mat-icon>
                  {{ (uploadingTemplate ? 'admin.queryForm.templateUploading'
                       : (templateFileName ? 'admin.queryForm.templateReplace'
                                           : 'admin.queryForm.templateUpload')) | transloco }}
                </button>
                <ng-container *ngIf="templateFileName">
                  <span class="template-name">
                    <mat-icon>description</mat-icon> {{ templateFileName }}
                  </span>
                  <button mat-icon-button type="button" [matTooltip]="'admin.queryForm.downloadTemplate' | transloco" [attr.aria-label]="'admin.queryForm.downloadTemplate' | transloco"
                          (click)="downloadTemplate()">
                    <mat-icon>download</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" type="button" [matTooltip]="'admin.queryForm.removeTemplate' | transloco" [attr.aria-label]="'admin.queryForm.removeTemplate' | transloco"
                          (click)="removeTemplate()">
                    <mat-icon>delete</mat-icon>
                  </button>
                </ng-container>
                <span *ngIf="!templateFileName" class="template-name none">
                  {{ 'admin.queryForm.noTemplate' | transloco }}
                </span>
              </div>
            </div>

            <h3>{{ 'admin.queries.parameters' | transloco }}</h3>
            <div formArrayName="parameters">
              <mat-card *ngFor="let param of parameters.controls; let i = index"
                        [formGroupName]="i" class="param-card">
                <div class="param-row">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.queries.name' | transloco }}</mat-label>
                    <input matInput formControlName="name" placeholder="paramName">
                    <mat-error *ngIf="param.get('name')?.hasError('required')">{{ 'common.nameRequired' | transloco }}</mat-error>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.queryForm.displayName' | transloco }}</mat-label>
                    <input matInput formControlName="displayName" [attr.placeholder]="'admin.queryForm.parameterLabel' | transloco">
                    <mat-error *ngIf="param.get('displayName')?.hasError('required')">
                      {{ 'admin.queryForm.displayNameRequired' | transloco }}
                    </mat-error>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.queries.type' | transloco }}</mat-label>
                    <mat-select formControlName="parameterType">
                      <mat-option [value]="0">{{ 'admin.queryForm.typeString' | transloco }}</mat-option>
                      <mat-option [value]="1">{{ 'admin.queryForm.typeNumber' | transloco }}</mat-option>
                      <mat-option [value]="2">{{ 'admin.queryForm.typeDate' | transloco }}</mat-option>
                      <mat-option [value]="3">{{ 'admin.queryForm.typeBoolean' | transloco }}</mat-option>
                      <mat-option [value]="4">{{ 'admin.queryForm.typeDropdown' | transloco }}</mat-option>
                    </mat-select>
                  </mat-form-field>

                  <mat-slide-toggle formControlName="isRequired">{{ 'admin.queryForm.required' | transloco }}</mat-slide-toggle>

                  <mat-form-field appearance="outline"
                                  *ngIf="getParamType(i) !== ParameterType.Dropdown">
                    <mat-label>{{ 'admin.queryForm.defaultValue' | transloco }}</mat-label>
                    <input matInput formControlName="defaultValue">
                  </mat-form-field>

                  <button mat-icon-button color="warn" type="button" (click)="removeParameter(i)"
                          [matTooltip]="'admin.queryForm.removeParameter' | transloco" [attr.aria-label]="'admin.queryForm.removeParameter' | transloco">
                    <mat-icon>delete</mat-icon>
                  </button>
                </div>

                <!-- Multi-value toggle: enables IN-clause expansion for String and Dropdown params -->
                <div *ngIf="getParamType(i) === ParameterType.String
                         || getParamType(i) === ParameterType.Dropdown"
                     class="multi-value-block">
                  <div class="toggle-row">
                    <mat-slide-toggle formControlName="allowMultiple">
                      {{ 'admin.queryForm.allowMultiple' | transloco }}
                    </mat-slide-toggle>
                    <app-hint-icon *ngIf="isAllowMultiple(i) && getParamType(i) === ParameterType.Dropdown"
                      [text]="'admin.queryForm.multiDropdownHint' | transloco: multiValueTokens"></app-hint-icon>
                    <app-hint-icon *ngIf="isAllowMultiple(i) && getParamType(i) === ParameterType.String"
                      [text]="'admin.queryForm.multiStringHint' | transloco: multiValueTokens"></app-hint-icon>
                  </div>
                </div>

                <!-- Dropdown configuration section -->
                <div *ngIf="getParamType(i) === ParameterType.Dropdown" class="dropdown-config">
                  <h4>{{ 'admin.queryForm.dropdownConfig' | transloco }}</h4>

                  <mat-radio-group formControlName="dropdownSourceType" class="source-radio-group">
                    <span class="radio-with-hint">
                      <mat-radio-button [value]="DropdownSourceType.Static">
                        {{ 'admin.queryForm.dropdownStatic' | transloco }}
                      </mat-radio-button>
                      <app-hint-icon
                        [text]="'admin.queryForm.dropdownStaticHint' | transloco"></app-hint-icon>
                    </span>
                    <mat-radio-button [value]="DropdownSourceType.Query">
                      {{ 'admin.queryForm.dropdownFromQuery' | transloco }}
                    </mat-radio-button>
                  </mat-radio-group>

                  <!-- Static values editor -->
                  <div *ngIf="getDropdownSourceType(i) === DropdownSourceType.Static"
                       class="static-values-editor">
                    <div *ngFor="let opt of getStaticOptions(i); let j = index; trackBy: trackStaticOption"
                         class="static-option-row">
                      <mat-form-field appearance="outline" class="option-field">
                        <mat-label>{{ 'admin.queryForm.label' | transloco }}</mat-label>
                        <input matInput [value]="opt.label"
                               (input)="updateStaticOption(i, j, 'label', $event)">
                      </mat-form-field>
                      <mat-form-field appearance="outline" class="option-field">
                        <mat-label>{{ 'admin.queryForm.value' | transloco }}</mat-label>
                        <input matInput [value]="opt.value"
                               (input)="updateStaticOption(i, j, 'value', $event)">
                      </mat-form-field>
                      <button mat-icon-button color="warn" type="button"
                              (click)="removeStaticOption(i, j)"
                              [matTooltip]="'admin.queryForm.removeOption' | transloco" [attr.aria-label]="'admin.queryForm.removeOption' | transloco">
                        <mat-icon>remove_circle_outline</mat-icon>
                      </button>
                    </div>
                    <button mat-stroked-button type="button" (click)="addStaticOption(i)">
                      <mat-icon>add</mat-icon> {{ 'admin.queryForm.addOption' | transloco }}
                    </button>
                  </div>

                  <!-- Query-based values configuration -->
                  <div *ngIf="getDropdownSourceType(i) === DropdownSourceType.Query"
                       class="query-config">
                    <mat-form-field appearance="outline" class="full-width">
                      <mat-label>{{ 'admin.queryForm.lookupQuery' | transloco }}</mat-label>
                      <mat-select formControlName="dropdownQueryId">
                        <mat-option *ngFor="let q of availableQueries" [value]="q.id">
                          {{ q.name }}
                        </mat-option>
                      </mat-select>
                      <mat-hint *ngIf="!lookupLoadFailed.queries">{{ 'admin.queryForm.lookupQueryHint' | transloco }}</mat-hint>
                      <mat-hint *ngIf="lookupLoadFailed.queries" class="load-failed-hint">
                        {{ 'admin.queryForm.lookupLoadFailed' | transloco }}
                      </mat-hint>
                    </mat-form-field>

                    <div class="column-row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.queryForm.valueColumn' | transloco }}</mat-label>
                        <input matInput formControlName="dropdownQueryValueColumn"
                               [attr.placeholder]="'admin.queryForm.valueColumnPlaceholder' | transloco">
                        <mat-hint>{{ 'admin.queryForm.valueColumnHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.queryForm.labelColumn' | transloco }}</mat-label>
                        <input matInput formControlName="dropdownQueryLabelColumn"
                               [attr.placeholder]="'admin.queryForm.labelColumnPlaceholder' | transloco">
                        <mat-hint>{{ 'admin.queryForm.columnDisplayedHint' | transloco }}</mat-hint>
                      </mat-form-field>
                    </div>
                  </div>
                </div>
              </mat-card>
            </div>

            <button mat-stroked-button type="button" (click)="addParameter()" class="add-btn">
              <mat-icon>add</mat-icon> {{ 'admin.queryForm.addParameter' | transloco }}
            </button>

            <!-- Why the query was not saved. Save stays clickable while the form is invalid so this
                 can be shown: a greyed-out button gave no clue, and the offending field could be a
                 parameter far down the page. The form's own problems shrink as they are fixed; the
                 server's reasons stay until the next attempt. -->
            <div *ngIf="saveProblems.length" #problemsPanel class="save-problems" role="alert">
              <mat-icon class="save-problems-icon">error_outline</mat-icon>
              <div>
                <strong>{{ (problemsFromServer ? 'admin.queryForm.serverRejected'
                            : problemsScope === 'test' ? 'admin.queryForm.cannotTest' : 'admin.queryForm.cannotSave') | transloco }}</strong>
                <ul>
                  <li *ngFor="let problem of saveProblems">
                    {{ problem.text }}
                    <button *ngIf="problem.sqlIndex !== undefined" type="button" class="show-problem"
                            (click)="revealSqlProblem(problem.sqlIndex)">
                      {{ 'admin.queryForm.showProblem' | transloco }}
                    </button>
                  </li>
                </ul>
              </div>
            </div>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/queries">{{ 'common.cancel' | transloco }}</button>
              <button mat-stroked-button type="button" (click)="openTest()" [disabled]="saving"
                      [matTooltip]="'admin.queryForm.testHint' | transloco">
                <mat-icon>play_arrow</mat-icon> {{ 'admin.queryForm.test' | transloco }}
              </button>
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ (saving ? 'common.saving' : (isEdit ? 'common.update' : 'common.create')) | transloco }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    mat-form-field { margin-inline-end: 16px; }
    .param-card { margin-bottom: 12px; padding: 12px; }
    .param-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px; }
    .save-problems {
      display: flex; gap: 12px; align-items: flex-start;
      margin-top: 24px; padding: 12px 16px;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-inline-start: 4px solid var(--status-error);
      border-radius: 4px;
    }
    .save-problems-icon { color: var(--status-error); flex-shrink: 0; }
    .save-problems ul { margin: 6px 0 0; padding-inline-start: 20px; }
    .save-problems li { margin: 2px 0; }
    .show-problem {
      background: none; border: none; padding: 0 4px; cursor: pointer;
      color: var(--accent-primary); text-decoration: underline; font: inherit;
    }
    .add-btn { margin: 16px 0; }
    .toggle { margin: 16px 0 4px; display: block; }
    /* A setting and its hint icon read as one control, so they share a row. */
    .toggle-row { display: flex; align-items: center; gap: 8px; margin: 16px 0 4px; }
    .radio-with-hint { display: inline-flex; align-items: center; gap: 6px; }
    .field-hint { font-size: 12px; color: var(--text-secondary); margin: 0 0 16px; max-width: 640px; }

    .dropdown-config {
      margin-top: 12px;
      padding: 12px;
      border-top: 1px solid var(--border-color);
      background: var(--bg-surface);
      border-radius: 4px;
    }
    .dropdown-config h4 { margin: 0 0 8px; font-size: 14px; color: var(--text-secondary); }
    .source-radio-group { display: flex; gap: 24px; margin-bottom: 16px; }
    .multi-value-block .toggle-row { margin: 0 0 12px; }
    .multi-value-block { margin: 8px 0 12px; }
    .hint { font-size: 12px; color: var(--text-secondary); margin-bottom: 8px; }

    .static-values-editor { margin-top: 8px; }
    .static-option-row { display: flex; align-items: center; gap: 8px; margin-bottom: 4px; }
    .option-field { flex: 1; }

    .query-config { margin-top: 8px; }
    .column-row { display: flex; gap: 16px; flex-wrap: wrap; }
    .load-failed-hint { color: var(--status-error); }
    .full-width { width: 100%; }
    /* Laid out like an outlined Material field's label and subscript, with room below so the
       hint line does not run into the floating label of the Timeout field. */
    .sql-field { margin-bottom: 20px; }
    .sql-label-row { display: flex; align-items: center; justify-content: space-between; }
    .sql-label { display: block; font-size: 13px; color: var(--text-secondary); margin: 0 0 6px; }
    .expand-sql { margin-block-end: 2px; }
    .sql-invalid .sql-label { color: var(--status-error); }
    .sql-subscript {
      display: flex; gap: 16px; justify-content: space-between;
      font-size: 12px; padding: 4px 16px 0; color: var(--text-secondary);
    }
    .sql-error { color: var(--status-error); }
    .sql-error-line { display: block; }
    .sql-counter { margin-inline-start: auto; white-space: nowrap; }

    .template-section { margin: 16px 0; }
    .template-section h3 { margin-bottom: 4px; }
    .export-section { margin: 16px 0; }
    .export-section h3 { margin-bottom: 4px; }
    .export-formats { display: flex; flex-wrap: wrap; gap: 8px 24px; margin-top: 8px; }
    .export-off { color: var(--text-secondary); font-style: italic; }
    .template-row { display: flex; align-items: center; gap: 8px; }
    .template-name { display: inline-flex; align-items: center; gap: 4px; font-size: 13px; }
    .template-name.none { color: var(--text-secondary); }
  `]
})
export class QueryFormComponent implements OnInit {
  form!: FormGroup;
  isEdit = false;
  isCopy = false;
  queryId?: string;
  saving = false;
  templateFileName: string | null = null;
  uploadingTemplate = false;
  availableQueries: DynamicQuery[] = [];
  availableDbUsers: DatabaseUser[] = [];
  availableGroups: QueryGroup[] = [];
  /** Set when a lookup list fails to load, so the empty dropdown can explain itself. */
  lookupLoadFailed = { dbUsers: false, queries: false, groups: false };

  /**
   * The Word-template placeholders, passed into the hint as interpolation values.
   * They live here rather than in the translation because they are literal markers the
   * user types into a .docx — they must read identically in every language, and writing
   * them inline in the template would collide with Angular's own {{ }} delimiters.
   */
  readonly templateTokens = {
    results: '{{RESULTS}}',
    queryName: '{{QUERY_NAME}}',
    generatedAt: '{{GENERATED_AT}}',
    generatedBy: '{{@generated_by}}',
    rowCount: '{{ROW_COUNT}}',
    parameter: '{{@paramName}}',
    params: '{{PARAMS}}'
  };

  /**
   * Which formats this query may be exported as. Held outside the reactive form because it is a
   * set rather than a field, and empty is the meaningful default: a query nobody has opened
   * export on cannot be downloaded at all.
   */
  allowedExportFormats: string[] = [];
  readonly exportFormats = EXPORT_FORMATS;

  isExportFormatAllowed(name: string): boolean {
    return this.allowedExportFormats.includes(name);
  }

  toggleExportFormat(name: string, allowed: boolean): void {
    this.allowedExportFormats = allowed
      ? [...this.allowedExportFormats, name]
      : this.allowedExportFormats.filter(f => f !== name);
  }

  /** SQL fragments shown in the multi-value hints — code, so identical in every language. */
  readonly multiValueTokens = {
    expansion: '(@name_0, @name_1, ...)',
    inClause: 'WHERE col IN (@name)',
    example: 'value1, value2'
  };

  readonly ParameterType = ParameterType;
  readonly DropdownSourceType = DropdownSourceType;

  /**
   * The longest SQL the server will accept — an administrator's setting, so it is read rather
   * than compiled in. Starts at the server's own default until the settings arrive; the server
   * re-checks on save either way.
   */
  sqlMaxLength = 4000;

  /** What stopped the last save, one readable line each. Empty hides the panel. */
  saveProblems: SaveProblem[] = [];
  /** Passed to the SQL editor, which highlights every match and its line. */
  readonly forbiddenSqlPattern = FORBIDDEN_SQL;
  @ViewChild('sqlEditor') sqlEditor?: SqlEditorComponent;
  /** True when the problems are the server's refusal rather than the form's own checks. */
  problemsFromServer = false;
  /** Whether the panel answers Save (every field) or Test (only what a test runs with). */
  problemsScope: 'save' | 'test' = 'save';

  private readonly dialog = inject(MatDialog);

  @ViewChild('problemsPanel') problemsPanel?: ElementRef<HTMLElement>;

  /**
   * The forbidden-SQL message, shown as soon as the text is typed rather than on leaving the box:
   * a trailing ";" or a "--" comment is a habit, and the fix is in the line just written.
   */
  get sqlForbiddenMessages(): string[] {
    const problems = this.form?.get('sqlQuery')?.getError('forbiddenSql') as ForbiddenSql[] | undefined;
    return (problems ?? []).map(p => this.forbiddenSqlMessage(p));
  }

  /** "Line 3: Remove the semicolon…" — the line is the one highlighted in the editor. */
  private forbiddenSqlMessage({ found, line }: ForbiddenSql): string {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const reason =
      found === '--' ? t('admin.queryForm.sqlForbiddenComment') :
      found === ';' ? t('admin.queryForm.sqlForbiddenSemicolon') :
      t('admin.queryForm.sqlForbiddenSystemCall', { found: found.toUpperCase() });
    return `${t('admin.queryForm.sqlProblemLine', { line })} ${reason}`;
  }

  /**
   * Opens the SQL in a near-full-screen editor. It edits the same form control, so there is
   * nothing to apply; on close the value is pushed back into the box on the page, because two
   * editors on one control do not update each other's view (Angular only syncs model -> view).
   */
  expandSql(): void {
    const control = this.form.get('sqlQuery') as FormControl<string>;
    const data: SqlExpandDialogData = {
      control,
      problemPattern: this.forbiddenSqlPattern,
      maxLength: this.sqlMaxLength,
      problems: () => [
        ...this.sqlForbiddenMessages,
        ...(control.hasError('maxlength')
          ? [this.transloco.translate('admin.queryForm.sqlTooLong', { max: this.sqlMaxLength })] : [])
      ]
    };
    // The popup sits outside this component's view tree, so its edits do not mark the page for
    // an update; in this zoneless app the counter and messages behind it would freeze (and dev
    // mode reports NG0100). Refresh the page on every change while it is open.
    const changes = control.valueChanges.subscribe(() => this.cdr.markForCheck());
    this.dialog.open(SqlExpandDialogComponent, {
      data, width: '95vw', maxWidth: '95vw', height: '90vh', ariaModal: true, autoFocus: false,
      panelClass: 'sql-expand-panel'
    }).afterClosed().subscribe(() => {
      changes.unsubscribe();
      control.setValue(control.value);
      control.markAsTouched();
      this.cdr.detectChanges();
    });
  }

  /** Scrolls the editor to that highlight and selects it. */
  revealSqlProblem(index = 0): void {
    this.sqlEditor?.revealProblem(index);
  }

  /** Mirrors the editor's own error border: invalid, and the user has left the box. */
  get sqlShowsError(): boolean {
    const control = this.form?.get('sqlQuery');
    return !!control && control.invalid && control.touched;
  }

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private transloco: TranslocoService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.required, Validators.maxLength(1000)]],
      sqlQuery: ['', [Validators.required, Validators.maxLength(this.sqlMaxLength), sqlSafetyValidator]],
      timeoutSeconds: [30, [Validators.min(0)]],
      isLongRunning: [false],
      allowRunWithoutConfirmation: [true],
      saveOldValues: [true],
      databaseUserId: [null],
      queryGroupId: [null],
      isEnabled: [true],
      parameters: this.fb.array([])
    });

    // While the panel lists the form's own problems, keep it in step: each one disappears as it
    // is fixed, and the panel goes once nothing is left. The server's reasons are left alone,
    // since only the server can say whether an edit answered them.
    this.form.statusChanges.subscribe(() => {
      if (this.saveProblems.length && !this.problemsFromServer) {
        const remaining = this.collectProblems(this.problemsScope);
        this.saveProblems = remaining.length && this.hasProblems(this.problemsScope) ? remaining : [];
        this.cdr.detectChanges();
      }
    });

    this.queryService.getSystemSettings().subscribe({
      next: (settings) => {
        // An API older than this setting omits it; keep the default rather than "undefined".
        this.sqlMaxLength = settings.querySqlMaxLength ?? this.sqlMaxLength;
        const sql = this.form.get('sqlQuery')!;
        sql.setValidators([Validators.required, Validators.maxLength(this.sqlMaxLength), sqlSafetyValidator]);
        sql.updateValueAndValidity({ emitEvent: false });
        this.cdr.detectChanges();
      },
      // Keeping the default is safe: the server enforces the real limit and says so on save.
      error: () => this.cdr.detectChanges()
    });

    // These three feed <mat-select> controls. Swallowing a failure leaves the
    // dropdown permanently empty with no way for the user to type a value in,
    // so each failure has to be surfaced.
    this.queryService.getAllDatabaseUsers().subscribe({
      next: (dbUsers) => { this.availableDbUsers = dbUsers; this.cdr.detectChanges(); },
      error: (err) => this.onLookupLoadFailed('dbUsers', err, 'Failed to load database users')
    });

    // Load all queries so admin can pick a lookup query
    this.queryService.getAllQueries().subscribe({
      next: (queries) => { this.availableQueries = queries; this.cdr.detectChanges(); },
      error: (err) => this.onLookupLoadFailed('queries', err, 'Failed to load lookup queries')
    });

    this.queryService.getAllQueryGroups().subscribe({
      next: (groups) => { this.availableGroups = groups; this.cdr.detectChanges(); },
      error: (err) => this.onLookupLoadFailed('groups', err, 'Failed to load query groups')
    });

    this.queryId = this.route.snapshot.params['id'];
    if (this.queryId) {
      this.isEdit = true;
      this.loadQuery(this.queryId);
    } else {
      const copyFromId = this.route.snapshot.queryParams['copyFrom'];
      if (copyFromId) {
        this.isCopy = true;
        this.loadQuery(copyFromId);
      }
    }
  }

  get parameters(): FormArray {
    return this.form.get('parameters') as FormArray;
  }

  getParamType(index: number): ParameterType {
    return this.parameters.at(index).get('parameterType')?.value;
  }

  isAllowMultiple(index: number): boolean {
    return !!this.parameters.at(index).get('allowMultiple')?.value;
  }

  getDropdownSourceType(index: number): DropdownSourceType | null {
    return this.parameters.at(index).get('dropdownSourceType')?.value ?? null;
  }

  // ---- Static options helpers ----

  getStaticOptions(index: number): DropdownOption[] {
    const raw = this.parameters.at(index).get('dropdownStaticValues')?.value as string;
    if (!raw) return [];
    try { return JSON.parse(raw); } catch { return []; }
  }

  addStaticOption(index: number): void {
    const opts = this.getStaticOptions(index);
    opts.push({ label: '', value: '' });
    this.parameters.at(index).get('dropdownStaticValues')?.setValue(JSON.stringify(opts));
  }

  removeStaticOption(paramIndex: number, optIndex: number): void {
    const opts = this.getStaticOptions(paramIndex);
    opts.splice(optIndex, 1);
    this.parameters.at(paramIndex).get('dropdownStaticValues')?.setValue(JSON.stringify(opts));
  }

  trackStaticOption(index: number): number {
    return index;
  }

  updateStaticOption(paramIndex: number, optIndex: number, field: 'label' | 'value', event: Event): void {
    const opts = this.getStaticOptions(paramIndex);
    opts[optIndex][field] = (event.target as HTMLInputElement).value;
    this.parameters.at(paramIndex).get('dropdownStaticValues')?.setValue(JSON.stringify(opts));
  }

  // ---- Parameter management ----

  addParameter(): void {
    this.parameters.push(this.fb.group({
      name: ['', PARAM_NAME_VALIDATORS],
      displayName: ['', PARAM_DISPLAY_NAME_VALIDATORS],
      parameterType: [ParameterType.String],
      isRequired: [true],
      defaultValue: [''],
      sortOrder: [this.parameters.length],
      allowMultiple: [false],
      dropdownSourceType: [DropdownSourceType.Static],
      dropdownStaticValues: ['[]'],
      dropdownQueryId: [null],
      dropdownQueryValueColumn: [''],
      dropdownQueryLabelColumn: ['']
    }));
  }

  removeParameter(index: number): void {
    this.parameters.removeAt(index);
  }

  // ---- Word export template ----

  onTemplateSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || !this.queryId) return;

    this.uploadingTemplate = true;
    this.queryService.uploadWordTemplate(this.queryId, file).subscribe({
      next: () => {
        this.uploadingTemplate = false;
        this.templateFileName = file.name;
        this.toast.success('admin.queryForm.templateUploaded');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.uploadingTemplate = false;
        this.toast.error(err, 'admin.queryForm.templateUploadFailed');
        this.cdr.detectChanges();
      }
    });
  }

  downloadTemplate(): void {
    if (!this.queryId) return;
    this.queryService.downloadWordTemplate(this.queryId).subscribe({
      next: (blob) => {
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = this.templateFileName || 'template.docx';
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: (err) => this.toast.error(err, 'admin.queryForm.templateDownloadFailed')
    });
  }

  removeTemplate(): void {
    if (!this.queryId) return;
    this.confirmService.askThen({
      titleKey: 'admin.queryForm.removeTemplateTitle',
      messageKey: 'admin.queryForm.removeTemplateMessage',
      params: { fileName: this.templateFileName },
      confirmText: this.transloco.translate('admin.queryForm.removeTemplateConfirm'),
      destructive: true
    }, () => this.doRemoveTemplate());
  }

  private doRemoveTemplate(): void {
    this.queryService.deleteWordTemplate(this.queryId!).subscribe({
      next: () => {
        this.templateFileName = null;
        this.toast.success('admin.queryForm.templateRemoved');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(err, 'admin.queryForm.templateRemoveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  loadQuery(id: string): void {
    this.queryService.getQueryById(id).pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: (query) => {
        this.form.patchValue({
          name: this.isCopy ? `Copy of ${query.name}` : query.name,
          description: query.description,
          sqlQuery: query.sqlQuery,
          timeoutSeconds: query.timeoutSeconds,
          isLongRunning: !!query.isLongRunning,
          allowRunWithoutConfirmation: query.allowRunWithoutConfirmation !== false,
          saveOldValues: query.saveOldValues !== false,
          databaseUserId: query.databaseUserId || null,
          queryGroupId: query.queryGroupId || null,
          isEnabled: this.isCopy ? true : query.isEnabled
        });

        // A copy keeps the original's export settings: it is the same data, so the same
        // formats are appropriate. (Templates are not copied — a copy starts without one.)
        this.allowedExportFormats = [...(query.allowedExportFormats ?? [])];
        this.templateFileName = this.isCopy ? null : (query.wordTemplateFileName || null);

        [...query.parameters].sort((a, b) => a.sortOrder - b.sortOrder).forEach(p => {
          this.parameters.push(this.fb.group({
            name: [p.name, PARAM_NAME_VALIDATORS],
            displayName: [p.displayName, PARAM_DISPLAY_NAME_VALIDATORS],
            parameterType: [p.parameterType],
            isRequired: [p.isRequired],
            defaultValue: [p.defaultValue || ''],
            sortOrder: [p.sortOrder],
            allowMultiple: [!!p.allowMultiple],
            dropdownSourceType: [p.dropdownSourceType ?? DropdownSourceType.Static],
            dropdownStaticValues: [p.dropdownStaticValues || '[]'],
            dropdownQueryId: [p.dropdownQueryId || null],
            dropdownQueryValueColumn: [p.dropdownQueryValueColumn || ''],
            dropdownQueryLabelColumn: [p.dropdownQueryLabelColumn || '']
          }));
        });
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(err, 'admin.queryForm.loadFailed');
      }
    });
  }

  onSubmit(): void {
    this.problemsScope = 'save';
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.showProblems(this.collectProblems('save'), false);
      return;
    }

    this.saveProblems = [];
    this.saving = true;
    const value = this.form.value;
    const cleanedParams = this.cleanedParameters();

    const payload = {
      ...value,
      parameters: cleanedParams,
      allowedExportFormats: this.allowedExportFormats
    };

    const request$ = this.isEdit
      ? this.queryService.updateQuery(this.queryId!, { ...payload, id: this.queryId })
      : this.queryService.createQuery(payload);

    request$.pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success(
          this.isEdit ? 'admin.queryForm.updated' : (this.isCopy ? 'admin.queryForm.copied' : 'admin.queryForm.created')
        );
        this.router.navigate(['/admin/queries']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'common.operationFailed');
        this.showProblems(this.serverProblems(err), true);
      }
    });
  }

  /**
   * Opens the test window with a copy of the SQL and parameters as edited — nothing is saved,
   * and the form is left exactly as it was. Only what a test runs with is checked first: a
   * missing name or description does not stop a test.
   */
  openTest(): void {
    this.problemsScope = 'test';
    if (this.hasProblems('test')) {
      this.form.get('sqlQuery')?.markAsTouched();
      this.form.get('timeoutSeconds')?.markAsTouched();
      this.parameters.markAllAsTouched();
      this.showProblems(this.collectProblems('test'), false);
      return;
    }
    this.saveProblems = [];
    const value = this.form.value;
    const data: QueryTestDialogData = {
      request: {
        sqlQuery: value.sqlQuery,
        timeoutSeconds: value.timeoutSeconds ?? 30,
        databaseUserId: value.databaseUserId || null,
        parameters: this.cleanedParameters()
      }
    };
    this.dialog.open(QueryTestDialogComponent, {
      data, width: '1000px', maxWidth: '95vw', ariaModal: true, autoFocus: 'dialog'
    });
  }

  /** Whether anything in scope is invalid: Test looks only at the SQL, timeout and parameters. */
  private hasProblems(scope: 'save' | 'test'): boolean {
    if (scope === 'save') return this.form.invalid;
    return !!this.form.get('sqlQuery')?.invalid || !!this.form.get('timeoutSeconds')?.invalid || this.parameters.invalid;
  }

  /**
   * The parameters as they are sent to the server. For non-dropdown parameters the dropdown-only
   * config is cleared; allowMultiple is kept for String (comma-separated IN-clause expansion) but
   * cleared for Number/Date/Boolean, where it has no meaning. sortOrder is resequenced to the
   * visible position so the stored order always matches what the admin sees here (and what the
   * execution page renders by sortOrder).
   */
  private cleanedParameters(): any[] {
    return this.form.value.parameters.map((p: any, i: number) => {
      const ordered = { ...p, sortOrder: i };
      if (ordered.parameterType !== ParameterType.Dropdown) {
        return {
          ...ordered,
          allowMultiple: ordered.parameterType === ParameterType.String ? !!ordered.allowMultiple : false,
          dropdownSourceType: null,
          dropdownStaticValues: null,
          dropdownQueryId: null,
          dropdownQueryValueColumn: null,
          dropdownQueryLabelColumn: null
        };
      }
      return ordered;
    });
  }

  private showProblems(problems: SaveProblem[], fromServer: boolean): void {
    this.saveProblems = problems;
    this.problemsFromServer = fromServer;
    this.cdr.detectChanges();
    this.problemsPanel?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'center' });
  }

  /** The form's own reasons for refusing to save, in page order, naming each field. */
  private collectProblems(scope: 'save' | 'test' = 'save'): SaveProblem[] {
    const t = (key: string, params?: Record<string, unknown>) => this.transloco.translate(key, params);
    const problems: SaveProblem[] = [];

    const describe = (control: AbstractControl | null, field: string, prefix = '') => {
      if (!control || control.valid) return;
      const e = control.errors ?? {};
      const line =
        e['required'] ? t('admin.queryForm.problemRequired', { field }) :
        e['maxlength'] ? t('admin.queryForm.problemTooLong', { field, max: e['maxlength'].requiredLength }) :
        e['min'] ? t('admin.queryForm.problemMin', { field, min: e['min'].min }) :
        e['pattern'] ? t('admin.queryForm.problemParamName') :
        t('admin.queryForm.problemInvalid', { field });
      problems.push({ text: prefix + line });
    };

    if (scope === 'save') {
      describe(this.form.get('name'), t('admin.queries.name'));
      describe(this.form.get('description'), t('admin.queries.description'));
    }
    const sql = this.form.get('sqlQuery');
    if (sql?.hasError('maxlength')) {
      problems.push({ text: t('admin.queryForm.sqlTooLong', { max: this.sqlMaxLength }) });
    } else if (sql?.hasError('forbiddenSql')) {
      for (const problem of sql.getError('forbiddenSql') as ForbiddenSql[]) {
        problems.push({ text: this.forbiddenSqlMessage(problem), sqlIndex: problem.index });
      }
    } else {
      describe(sql, t('admin.queryForm.sqlParameterized'));
    }
    describe(this.form.get('timeoutSeconds'), t('admin.queryForm.timeout'));

    this.parameters.controls.forEach((group, i) => {
      if (group.valid) return;
      const prefix = this.parameterPrefix(i, group.get('name')?.value) + ' ';
      describe(group.get('name'), t('admin.queries.name'), prefix);
      describe(group.get('displayName'), t('admin.queryForm.displayName'), prefix);
    });

    // Anything invalid without specific wording above still gets a line, so the panel can never
    // be empty while Save is refusing.
    return problems.length ? problems : [{ text: t('admin.queryForm.problemUnknown') }];
  }

  /**
   * The server's reasons, one line each. Field paths such as "Parameters[1].Name" become
   * "Parameter 2 (status):" so a rule broken in one parameter row points at that row.
   */
  private serverProblems(err: unknown): SaveProblem[] {
    const reasons = extractValidationErrors(err);
    if (!reasons.length) {
      return [{ text: extractApiError(err, this.transloco.translate('common.operationFailed')) }];
    }
    return reasons.map(({ field, message }) => {
      // A refusal about the SQL can point at the editor when the editor has something highlighted.
      if (/^sqlquery$/i.test(field)) {
        return { text: message, sqlIndex: this.form.get('sqlQuery')?.hasError('forbiddenSql') ? 0 : undefined };
      }
      const match = /^parameters\[(\d+)\]/i.exec(field);
      if (!match) return { text: message };
      const index = Number(match[1]);
      return { text: `${this.parameterPrefix(index, this.parameters.at(index)?.get('name')?.value)} ${message}` };
    });
  }

  private parameterPrefix(index: number, name: string | null | undefined): string {
    return name
      ? this.transloco.translate('admin.queryForm.problemParameterNamed', { n: index + 1, name })
      : this.transloco.translate('admin.queryForm.problemParameter', { n: index + 1 });
  }

  /**
   * Records that one of the lookup dropdowns could not be populated, so the
   * template can explain the empty <mat-select> instead of leaving the user
   * staring at a control with no options and no reason.
   */
  private onLookupLoadFailed(list: 'dbUsers' | 'queries' | 'groups', err: unknown, message: string): void {
    this.lookupLoadFailed[list] = true;
    this.toast.error(err, message);
    this.cdr.detectChanges();
  }
}
