import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, FormArray, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { DatabaseUser, DropdownOption, DropdownSourceType, DynamicQuery, ParameterType, QueryGroup } from '@core/models/dynamic-query.model';
import { EXPORT_FORMATS } from '@core/models/export-formats';
import { TranslocoService } from '@jsverse/transloco';

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

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.queryForm.sqlParameterized' | transloco }}</mat-label>
              <textarea matInput formControlName="sqlQuery" rows="5" dir="ltr" class="force-ltr"
                        [attr.placeholder]="'admin.queryForm.sqlPlaceholder' | transloco"></textarea>
              <mat-hint>{{ 'admin.queryForm.sqlHint' | transloco }}</mat-hint>
              <mat-error *ngIf="form.get('sqlQuery')?.hasError('required')">{{ 'admin.queryForm.sqlRequired' | transloco }}</mat-error>
            </mat-form-field>

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

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/queries">{{ 'common.cancel' | transloco }}</button>
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || saving">
                {{ saving ? 'Saving...' : (isEdit ? 'Update' : 'Create') }}
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
      sqlQuery: ['', [Validators.required, Validators.maxLength(4000)]],
      timeoutSeconds: [30, [Validators.min(0)]],
      isLongRunning: [false],
      allowRunWithoutConfirmation: [true],
      saveOldValues: [true],
      databaseUserId: [null],
      queryGroupId: [null],
      isEnabled: [true],
      parameters: this.fb.array([])
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
      name: ['', Validators.required],
      displayName: ['', Validators.required],
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
            name: [p.name, Validators.required],
            displayName: [p.displayName, Validators.required],
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
    if (this.form.invalid) return;

    this.saving = true;
    const value = this.form.value;

    // For non-dropdown parameters, clear dropdown-only config before sending. allowMultiple
    // is preserved for String (used to enable comma-separated IN-clause expansion) but cleared
    // for Number/Date/Boolean where it has no meaning.
    const cleanedParams = value.parameters.map((p: any, i: number) => {
      // Resequence sortOrder to the visible position so the stored order always matches
      // what the admin sees here (and what the execution page renders by sortOrder).
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
      }
    });
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
