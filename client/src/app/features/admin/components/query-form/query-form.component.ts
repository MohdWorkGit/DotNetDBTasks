import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, FormArray, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { DatabaseUser, DropdownOption, DropdownSourceType, DynamicQuery, ParameterType, QueryGroup } from '@core/models/dynamic-query.model';

@Component({
  standalone: false,
  selector: 'app-query-form',
  template: `
    <div class="container">
      <h2>{{ isEdit ? 'Edit' : (isCopy ? 'Copy' : 'Create') }} Dynamic Query</h2>

      <mat-card>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Name</mat-label>
              <input matInput formControlName="name">
              <mat-error *ngIf="form.get('name')?.hasError('required')">Name is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Description</mat-label>
              <textarea matInput formControlName="description" rows="3"></textarea>
              <mat-error *ngIf="form.get('description')?.hasError('required')">Description is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>SQL Query (parameterized)</mat-label>
              <textarea matInput formControlName="sqlQuery" rows="5"
                        placeholder="SELECT * FROM Users WHERE CreatedAt >= &#64;StartDate"></textarea>
              <mat-hint>Use &#64;paramName syntax for parameters. All query types supported (SELECT, INSERT, UPDATE, DELETE, etc.).</mat-hint>
              <mat-error *ngIf="form.get('sqlQuery')?.hasError('required')">SQL query is required</mat-error>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Timeout (seconds)</mat-label>
              <input matInput type="number" min="0" formControlName="timeoutSeconds">
              <mat-hint>Set to 0 for no timeout (query can run indefinitely).</mat-hint>
            </mat-form-field>

            <mat-slide-toggle formControlName="isLongRunning" class="toggle">
              Long-running query
            </mat-slide-toggle>
            <p class="field-hint">
              Enable for slow queries. They run as a background job the page polls for, so they
              are not cut off by proxy/gateway timeouts. Normal queries run instantly and should
              leave this off.
            </p>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Database User</mat-label>
              <mat-select formControlName="databaseUserId">
                <mat-option [value]="null">Default (system connection)</mat-option>
                <mat-option *ngFor="let du of availableDbUsers" [value]="du.id">
                  {{ du.name }}
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!lookupLoadFailed.dbUsers">Select which database credentials to use when executing this query</mat-hint>
              <mat-hint *ngIf="lookupLoadFailed.dbUsers" class="load-failed-hint">
                Could not load database users — reload the page to try again.
              </mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline" class="full-width">
              <mat-label>Group</mat-label>
              <mat-select formControlName="queryGroupId">
                <mat-option [value]="null">Ungrouped</mat-option>
                <mat-option *ngFor="let g of availableGroups" [value]="g.id">
                  {{ g.name }}
                </mat-option>
              </mat-select>
              <mat-hint *ngIf="!lookupLoadFailed.groups">Pick a folder to organise this query on the My Queries page</mat-hint>
              <mat-hint *ngIf="lookupLoadFailed.groups" class="load-failed-hint">
                Could not load query groups — reload the page to try again.
              </mat-hint>
            </mat-form-field>

            <mat-slide-toggle *ngIf="isEdit" formControlName="isEnabled" class="toggle">
              Enabled
            </mat-slide-toggle>

            <div *ngIf="isEdit" class="template-section">
              <h3>Word Export Template</h3>
              <p class="field-hint">
                Optional .docx used when this query's results are exported as Word. Put
                {{ '{{RESULTS}}' }} where the result table should go; {{ '{{QUERY_NAME}}' }},
                {{ '{{GENERATED_AT}}' }} and {{ '{{ROW_COUNT}}' }} are also replaced (including
                in headers/footers). Each parameter's value is available as
                {{ '{{@paramName}}' }} — the same &#64;name you use in the SQL — and
                {{ '{{PARAMS}}' }} prints every parameter as "Display Name: value", one per line.
                To style the result table, put {{ '{{RESULTS}}' }} inside a
                table: its first row styles the header, the marker's row styles the data rows,
                and an optional row below it styles alternating rows. Without a template, the
                system default Word template is used (managed on the Dynamic Queries page).
              </p>
              <div class="template-row">
                <input #tplInput type="file" accept=".docx" hidden (change)="onTemplateSelected($event)">
                <button mat-stroked-button type="button" (click)="tplInput.click()"
                        [disabled]="uploadingTemplate">
                  <mat-icon>upload_file</mat-icon>
                  {{ uploadingTemplate ? 'Uploading…' : (templateFileName ? 'Replace Template' : 'Upload Template') }}
                </button>
                <ng-container *ngIf="templateFileName">
                  <span class="template-name">
                    <mat-icon>description</mat-icon> {{ templateFileName }}
                  </span>
                  <button mat-icon-button type="button" matTooltip="Download template" aria-label="Download template"
                          (click)="downloadTemplate()">
                    <mat-icon>download</mat-icon>
                  </button>
                  <button mat-icon-button color="warn" type="button" matTooltip="Remove template" aria-label="Remove template"
                          (click)="removeTemplate()">
                    <mat-icon>delete</mat-icon>
                  </button>
                </ng-container>
                <span *ngIf="!templateFileName" class="template-name none">
                  No template — the default layout is used
                </span>
              </div>
            </div>

            <h3>Parameters</h3>
            <div formArrayName="parameters">
              <mat-card *ngFor="let param of parameters.controls; let i = index"
                        [formGroupName]="i" class="param-card">
                <div class="param-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Name</mat-label>
                    <input matInput formControlName="name" placeholder="paramName">
                    <mat-error *ngIf="param.get('name')?.hasError('required')">Name is required</mat-error>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Display Name</mat-label>
                    <input matInput formControlName="displayName" placeholder="Parameter Label">
                    <mat-error *ngIf="param.get('displayName')?.hasError('required')">
                      Display name is required
                    </mat-error>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Type</mat-label>
                    <mat-select formControlName="parameterType">
                      <mat-option [value]="0">String</mat-option>
                      <mat-option [value]="1">Number</mat-option>
                      <mat-option [value]="2">Date</mat-option>
                      <mat-option [value]="3">Boolean</mat-option>
                      <mat-option [value]="4">Dropdown</mat-option>
                    </mat-select>
                  </mat-form-field>

                  <mat-slide-toggle formControlName="isRequired">Required</mat-slide-toggle>

                  <mat-form-field appearance="outline"
                                  *ngIf="getParamType(i) !== ParameterType.Dropdown">
                    <mat-label>Default Value</mat-label>
                    <input matInput formControlName="defaultValue">
                  </mat-form-field>

                  <button mat-icon-button color="warn" type="button" (click)="removeParameter(i)"
                          matTooltip="Remove parameter" aria-label="Remove parameter">
                    <mat-icon>delete</mat-icon>
                  </button>
                </div>

                <!-- Multi-value toggle: enables IN-clause expansion for String and Dropdown params -->
                <div *ngIf="getParamType(i) === ParameterType.String
                         || getParamType(i) === ParameterType.Dropdown"
                     class="multi-value-block">
                  <mat-slide-toggle formControlName="allowMultiple" class="allow-multiple-toggle">
                    Allow multiple values
                  </mat-slide-toggle>
                  <p *ngIf="isAllowMultiple(i) && getParamType(i) === ParameterType.Dropdown" class="hint">
                    Selected values are expanded into <code>(&#64;name_0, &#64;name_1, ...)</code> at
                    execution time. Use <code>WHERE col IN (&#64;name)</code> in your SQL.
                  </p>
                  <p *ngIf="isAllowMultiple(i) && getParamType(i) === ParameterType.String" class="hint">
                    User enters comma-separated values (e.g. <code>value1, value2</code>); each is bound
                    as a separate parameter and expanded into
                    <code>(&#64;name_0, &#64;name_1, ...)</code>. Use <code>WHERE col IN (&#64;name)</code>
                    in your SQL.
                  </p>
                </div>

                <!-- Dropdown configuration section -->
                <div *ngIf="getParamType(i) === ParameterType.Dropdown" class="dropdown-config">
                  <h4>Dropdown Configuration</h4>

                  <mat-radio-group formControlName="dropdownSourceType" class="source-radio-group">
                    <mat-radio-button [value]="DropdownSourceType.Static">
                      Static list (defined manually)
                    </mat-radio-button>
                    <mat-radio-button [value]="DropdownSourceType.Query">
                      From database query
                    </mat-radio-button>
                  </mat-radio-group>

                  <!-- Static values editor -->
                  <div *ngIf="getDropdownSourceType(i) === DropdownSourceType.Static"
                       class="static-values-editor">
                    <p class="hint">Add label/value pairs. The "value" is what gets passed to the SQL query.</p>
                    <div *ngFor="let opt of getStaticOptions(i); let j = index; trackBy: trackStaticOption"
                         class="static-option-row">
                      <mat-form-field appearance="outline" class="option-field">
                        <mat-label>Label</mat-label>
                        <input matInput [value]="opt.label"
                               (input)="updateStaticOption(i, j, 'label', $event)">
                      </mat-form-field>
                      <mat-form-field appearance="outline" class="option-field">
                        <mat-label>Value</mat-label>
                        <input matInput [value]="opt.value"
                               (input)="updateStaticOption(i, j, 'value', $event)">
                      </mat-form-field>
                      <button mat-icon-button color="warn" type="button"
                              (click)="removeStaticOption(i, j)"
                              matTooltip="Remove option" aria-label="Remove option">
                        <mat-icon>remove_circle_outline</mat-icon>
                      </button>
                    </div>
                    <button mat-stroked-button type="button" (click)="addStaticOption(i)">
                      <mat-icon>add</mat-icon> Add Option
                    </button>
                  </div>

                  <!-- Query-based values configuration -->
                  <div *ngIf="getDropdownSourceType(i) === DropdownSourceType.Query"
                       class="query-config">
                    <mat-form-field appearance="outline" class="full-width">
                      <mat-label>Lookup Query</mat-label>
                      <mat-select formControlName="dropdownQueryId">
                        <mat-option *ngFor="let q of availableQueries" [value]="q.id">
                          {{ q.name }}
                        </mat-option>
                      </mat-select>
                      <mat-hint *ngIf="!lookupLoadFailed.queries">Select the query that returns the dropdown options</mat-hint>
                      <mat-hint *ngIf="lookupLoadFailed.queries" class="load-failed-hint">
                        Could not load queries — reload the page to try again.
                      </mat-hint>
                    </mat-form-field>

                    <div class="column-row">
                      <mat-form-field appearance="outline">
                        <mat-label>Value Column</mat-label>
                        <input matInput formControlName="dropdownQueryValueColumn"
                               placeholder="e.g. ID">
                        <mat-hint>Column used as the stored value</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>Label Column</mat-label>
                        <input matInput formControlName="dropdownQueryLabelColumn"
                               placeholder="e.g. NAME">
                        <mat-hint>Column displayed to the user</mat-hint>
                      </mat-form-field>
                    </div>
                  </div>
                </div>
              </mat-card>
            </div>

            <button mat-stroked-button type="button" (click)="addParameter()" class="add-btn">
              <mat-icon>add</mat-icon> Add Parameter
            </button>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
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
    mat-form-field { margin-right: 16px; }
    .param-card { margin-bottom: 12px; padding: 12px; }
    .param-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px; }
    .add-btn { margin: 16px 0; }
    .toggle { margin: 16px 0 4px; display: block; }
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
    .allow-multiple-toggle { display: block; margin-bottom: 12px; }
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

  readonly ParameterType = ParameterType;
  readonly DropdownSourceType = DropdownSourceType;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    private confirmService: ConfirmService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.required, Validators.maxLength(1000)]],
      sqlQuery: ['', [Validators.required, Validators.maxLength(4000)]],
      timeoutSeconds: [30, [Validators.min(0)]],
      isLongRunning: [false],
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
        this.toast.success('Template uploaded');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.uploadingTemplate = false;
        this.toast.error(err, 'Failed to upload template');
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
      error: (err) => this.toast.error(err, 'Failed to download template')
    });
  }

  removeTemplate(): void {
    if (!this.queryId) return;
    this.confirmService.askThen({
      title: 'Remove Word template?',
      message: `"${this.templateFileName}" will be deleted from the server and this query's Word `
        + 'exports will fall back to the default layout. This cannot be undone.',
      confirmText: 'Remove template',
      destructive: true
    }, () => this.doRemoveTemplate());
  }

  private doRemoveTemplate(): void {
    this.queryService.deleteWordTemplate(this.queryId!).subscribe({
      next: () => {
        this.templateFileName = null;
        this.toast.success('Template removed');
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.toast.error(err, 'Failed to remove template');
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
          databaseUserId: query.databaseUserId || null,
          queryGroupId: query.queryGroupId || null,
          isEnabled: this.isCopy ? true : query.isEnabled
        });

        // Templates are not copied — a copy starts without one.
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
        this.toast.error(err, 'Failed to load query');
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

    const payload = { ...value, parameters: cleanedParams };

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
          `Query ${this.isEdit ? 'updated' : (this.isCopy ? 'copied' : 'created')} successfully`
        );
        this.router.navigate(['/admin/queries']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'Operation failed');
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
