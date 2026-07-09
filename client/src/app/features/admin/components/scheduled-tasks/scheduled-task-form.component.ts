import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { forkJoin } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { DynamicQuery, SystemUser } from '@core/models/dynamic-query.model';
import {
  EXPORT_FORMAT_LABELS,
  ExportFileFormat,
  SaveScheduledTaskRequest,
  ScheduleFrequency,
  ScheduledTask
} from '@core/models/scheduled-task.model';

@Component({
  standalone: false,
  selector: 'app-scheduled-task-form',
  template: `
    <div class="container">
      <div class="header">
        <h2>{{ isEdit ? 'Edit' : 'Create' }} Scheduled Task</h2>
        <button mat-button routerLink="/admin/scheduled-tasks">
          <mat-icon>arrow_back</mat-icon> Back
        </button>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <form [formGroup]="form" (ngSubmit)="save()" *ngIf="!loading">
        <mat-card class="section">
          <mat-card-title>Task</mat-card-title>
          <mat-card-content>
            <div class="row">
              <mat-form-field appearance="outline" class="grow">
                <mat-label>Name</mat-label>
                <input matInput formControlName="name" maxlength="200" required>
              </mat-form-field>
              <mat-slide-toggle formControlName="isEnabled" class="toggle">Enabled</mat-slide-toggle>
            </div>

            <mat-form-field appearance="outline" class="full">
              <mat-label>Description</mat-label>
              <input matInput formControlName="description" maxlength="1000">
            </mat-form-field>

            <mat-form-field appearance="outline" class="full">
              <mat-label>Output folder (on the server)</mat-label>
              <input matInput formControlName="outputFolder" maxlength="500"
                     placeholder="D:\\Exports\\Sales" required>
              <mat-hint>Absolute path; it is created automatically if missing.</mat-hint>
            </mat-form-field>

            <mat-form-field appearance="outline" class="full">
              <mat-label>Archive folder (optional)</mat-label>
              <input matInput formControlName="archiveFolder" maxlength="500"
                     placeholder="D:\\Exports\\Archive">
              <mat-hint>A copy of every export file is also written here.</mat-hint>
            </mat-form-field>

            <div class="row output-options">
              <mat-slide-toggle formControlName="combineOutput"
                                matTooltip="All query results are appended into one file, in query order">
                Combine all results into one file
              </mat-slide-toggle>
              <mat-slide-toggle formControlName="includeHeaders"
                                matTooltip="Off: CSV/Excel files contain data rows only">
                Include header row
              </mat-slide-toggle>
              <mat-form-field appearance="outline" class="timestamp-format">
                <mat-label>Timestamp format (optional)</mat-label>
                <input matInput formControlName="timestampFormat" maxlength="50"
                       placeholder="_yyyyMMdd-HHmmss">
                <mat-hint>.NET date format for the file-name suffix, e.g. -yyyy-MM-dd</mat-hint>
              </mat-form-field>
            </div>

            <div class="row wrap" *ngIf="combineOutput">
              <mat-form-field appearance="outline">
                <mat-label>Format</mat-label>
                <mat-select formControlName="combinedFormat" required>
                  <mat-option *ngFor="let f of formats" [value]="f">{{ formatLabels[f] }}</mat-option>
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" class="separator"
                              *ngIf="form.get('combinedFormat')?.value === Format.Csv">
                <mat-label>Separator</mat-label>
                <input matInput formControlName="combinedCsvSeparator" maxlength="8" placeholder=",">
                <mat-hint>e.g. ; or ;; ("tab" = tab)</mat-hint>
              </mat-form-field>
              <mat-form-field appearance="outline" class="grow">
                <mat-label>File name (optional)</mat-label>
                <input matInput formControlName="combinedFileName" maxlength="200"
                       placeholder="Defaults to the task name">
              </mat-form-field>
              <mat-slide-toggle formControlName="combinedAppendTimestamp" class="toggle"
                                matTooltip="Off: the same file is overwritten on every run">
                Append timestamp
              </mat-slide-toggle>
            </div>
            <p class="hint" *ngIf="combineOutput">
              The header row (when enabled) comes from the first query, so the queries
              should return the same columns.
            </p>
          </mat-card-content>
        </mat-card>

        <mat-card class="section">
          <mat-card-title>
            Schedule
            <button mat-stroked-button type="button" color="primary" class="add-item" (click)="addTrigger()">
              <mat-icon>add</mat-icon> Add trigger
            </button>
          </mat-card-title>
          <mat-card-content formArrayName="triggers">
            <p class="hint">
              The task runs on every trigger below — e.g. daily at 03:00 plus monthly on
              day 14 plus daily at 14:00.
            </p>

            <div class="row" *ngFor="let trigger of triggers.controls; let i = index" [formGroupName]="i">
              <mat-form-field appearance="outline">
                <mat-label>Frequency</mat-label>
                <mat-select formControlName="frequency" required>
                  <mat-option [value]="Frequency.EveryNMinutes">Every N minutes</mat-option>
                  <mat-option [value]="Frequency.Daily">Daily</mat-option>
                  <mat-option [value]="Frequency.Weekly">Weekly</mat-option>
                  <mat-option [value]="Frequency.Monthly">Monthly</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.EveryNMinutes">
                <mat-label>Interval (minutes)</mat-label>
                <input matInput type="number" formControlName="intervalMinutes" min="1">
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.Weekly">
                <mat-label>Day of week</mat-label>
                <mat-select formControlName="dayOfWeek">
                  <mat-option *ngFor="let d of dayNames; let di = index" [value]="di">{{ d }}</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.Monthly">
                <mat-label>Day of month</mat-label>
                <input matInput type="number" formControlName="dayOfMonth" min="1" max="31">
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) !== Frequency.EveryNMinutes">
                <mat-label>Time of day</mat-label>
                <input matInput type="time" formControlName="timeOfDay">
              </mat-form-field>

              <button mat-icon-button type="button" color="warn" matTooltip="Remove trigger"
                      class="remove-item" *ngIf="triggers.length > 1" (click)="removeTrigger(i)">
                <mat-icon>close</mat-icon>
              </button>
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card class="section">
          <mat-card-title>
            Queries to export
            <button mat-stroked-button type="button" color="primary" class="add-item" (click)="addItem()">
              <mat-icon>add</mat-icon> Add query
            </button>
          </mat-card-title>
          <mat-card-content formArrayName="items">
            <p class="hint" *ngIf="items.length === 0">
              Add at least one read (SELECT) query. Each query is exported to its own file.
            </p>

            <div class="item" *ngFor="let item of items.controls; let i = index" [formGroupName]="i">
              <div class="row">
                <mat-form-field appearance="outline" class="grow">
                  <mat-label>Query</mat-label>
                  <mat-select formControlName="dynamicQueryId" required
                              (selectionChange)="onQueryChange(i, $event.value)">
                    <mat-option *ngFor="let q of selectableQueries" [value]="q.id">
                      {{ q.name }}<span *ngIf="isWriteSql(q.sqlQuery)"> (modifies data)</span>
                    </mat-option>
                  </mat-select>
                </mat-form-field>

                <mat-form-field appearance="outline" *ngIf="!itemMeta[i]?.isWrite && !combineOutput">
                  <mat-label>Format</mat-label>
                  <mat-select formControlName="exportFormat" required>
                    <mat-option *ngFor="let f of formats" [value]="f">{{ formatLabels[f] }}</mat-option>
                  </mat-select>
                </mat-form-field>

                <mat-form-field appearance="outline" class="separator"
                                *ngIf="!itemMeta[i]?.isWrite && !combineOutput && isCsv(i)">
                  <mat-label>Separator</mat-label>
                  <input matInput formControlName="csvSeparator" maxlength="8" placeholder=",">
                  <mat-hint>e.g. ; or ;; ("tab" = tab)</mat-hint>
                </mat-form-field>

                <button mat-icon-button type="button" color="warn" matTooltip="Remove query"
                        class="remove-item" (click)="removeItem(i)">
                  <mat-icon>close</mat-icon>
                </button>
              </div>

              <p class="write-note" *ngIf="itemMeta[i]?.isWrite">
                <mat-icon inline>edit_note</mat-icon>
                This query modifies data: each run executes and commits it — no output
                file, only the affected-row count in the run status.
              </p>

              <div class="row" *ngIf="!itemMeta[i]?.isWrite && !combineOutput">
                <mat-form-field appearance="outline" class="grow">
                  <mat-label>File name (optional)</mat-label>
                  <input matInput formControlName="fileNamePrefix" maxlength="200"
                         [placeholder]="queryName(i) || 'Defaults to the query name'">
                </mat-form-field>
                <mat-slide-toggle formControlName="appendTimestamp" class="toggle"
                                  matTooltip="Off: the same file is overwritten on every run">
                  Append timestamp
                </mat-slide-toggle>
              </div>

              <div class="checkpoint" *ngIf="!itemMeta[i]?.isWrite">
                <div class="params-title">
                  Incremental run (optional) — the saved key is passed into the selected
                  parameter; order the query by the key column ascending
                </div>
                <div class="row wrap">
                  <mat-form-field appearance="outline">
                    <mat-label>Key column</mat-label>
                    <input matInput formControlName="keyColumn" maxlength="128"
                           placeholder="e.g. Id">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Key parameter</mat-label>
                    <mat-select formControlName="keyParameter">
                      <mat-option [value]="''">—</mat-option>
                      <mat-option *ngFor="let p of parameterDefs[i]" [value]="p.name">{{ p.name }}</mat-option>
                    </mat-select>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>Initial key</mat-label>
                    <input matInput formControlName="initialKey" maxlength="500" placeholder="e.g. 0">
                  </mat-form-field>
                </div>
                <div class="row" *ngIf="itemMeta[i]?.lastKeyValue">
                  <span class="saved-key">Saved checkpoint: <code>{{ itemMeta[i].lastKeyValue }}</code></span>
                  <mat-checkbox formControlName="resetKey">Reset on save (restart from the initial key)</mat-checkbox>
                </div>
              </div>

              <div class="params" formGroupName="parameters" *ngIf="parameterDefs[i]?.length">
                <div class="params-title">Parameter values</div>
                <div class="row wrap">
                  <mat-form-field appearance="outline" *ngFor="let p of parameterDefs[i]">
                    <mat-label>{{ p.displayName }}{{ p.isRequired ? ' *' : '' }}</mat-label>
                    <input matInput [formControlName]="p.name"
                           [placeholder]="p.allowMultiple ? 'value1, value2, value3' : ''">
                    <mat-hint *ngIf="p.allowMultiple">Multiple values separated by commas</mat-hint>
                  </mat-form-field>
                </div>
              </div>
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card class="section">
          <mat-card-title>Status visibility</mat-card-title>
          <mat-card-content>
            <mat-form-field appearance="outline" class="full">
              <mat-label>Users who can view this task's status</mat-label>
              <mat-select formControlName="viewerUserIds" multiple>
                <mat-option *ngFor="let u of users" [value]="u.id">
                  {{ u.username }}<span *ngIf="u.firstName || u.lastName"> — {{ u.firstName }} {{ u.lastName }}</span>
                </mat-option>
              </mat-select>
              <mat-hint>Admins and Auditors always see every task.</mat-hint>
            </mat-form-field>
          </mat-card-content>
        </mat-card>

        <div class="actions">
          <button mat-raised-button color="primary" type="submit" [disabled]="saving || form.invalid">
            <mat-icon>save</mat-icon> {{ saving ? 'Saving…' : 'Save' }}
          </button>
          <button mat-button type="button" routerLink="/admin/scheduled-tasks">Cancel</button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    .header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 16px;
    }
    .loading { display: flex; justify-content: center; padding: 40px; }
    .section { margin-bottom: 16px; }
    .section mat-card-title {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 16px;
      margin-bottom: 12px;
    }
    .row { display: flex; gap: 12px; align-items: baseline; }
    .row.wrap { flex-wrap: wrap; }
    .grow { flex: 1; }
    .full { width: 100%; }
    .toggle { margin-bottom: 20px; align-self: center; }
    .item {
      border: 1px solid var(--border-color, rgba(128,128,128,.3));
      border-radius: 8px;
      padding: 12px 12px 0;
      margin-bottom: 12px;
    }
    .write-note {
      color: #ff9800;
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0 0 12px;
    }
    .checkpoint { margin-bottom: 12px; }
    /* Wide enough for its hint text; without this the hint runs under the next control. */
    .separator { width: 220px; }
    .output-options { gap: 24px; margin-bottom: 20px; align-items: center; flex-wrap: wrap; }
    .timestamp-format { width: 280px; }
    .saved-key { align-self: center; color: var(--text-secondary); }
    .saved-key code { font-weight: 600; }
    .remove-item { align-self: center; }
    .params-title { font-size: 13px; color: var(--text-secondary); margin-bottom: 8px; }
    .hint { color: var(--text-secondary); }
    .actions { display: flex; gap: 12px; margin: 8px 0 32px; }
  `]
})
export class ScheduledTaskFormComponent implements OnInit {
  Frequency = ScheduleFrequency;
  Format = ExportFileFormat;
  formats = [ExportFileFormat.Excel, ExportFileFormat.Csv, ExportFileFormat.Json];
  formatLabels = EXPORT_FORMAT_LABELS;
  dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

  form!: FormGroup;
  isEdit = false;
  taskId: string | null = null;
  loading = true;
  saving = false;

  queries: DynamicQuery[] = [];
  users: SystemUser[] = [];
  /** Parameter definitions of the query selected in each item row, by row index. */
  parameterDefs: { name: string; displayName: string; isRequired: boolean; allowMultiple: boolean }[][] = [];
  /** Per-row info about the selected query: write vs read, and the saved checkpoint when editing. */
  itemMeta: { isWrite: boolean; lastKeyValue: string | null }[] = [];

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private queryService: QueryService,
    private scheduledTaskService: ScheduledTaskService,
    private snackBar: MatSnackBar,
    private cdr: ChangeDetectorRef
  ) {}

  get items(): FormArray {
    return this.form.get('items') as FormArray;
  }

  get triggers(): FormArray {
    return this.form.get('triggers') as FormArray;
  }

  triggerFrequency(index: number): ScheduleFrequency {
    return this.triggers.at(index).get('frequency')!.value;
  }

  get combineOutput(): boolean {
    return !!this.form.get('combineOutput')!.value;
  }

  /** All enabled queries can be scheduled: reads export a file, writes commit and report affected rows. */
  get selectableQueries(): DynamicQuery[] {
    return this.queries;
  }

  isCsv(index: number): boolean {
    return this.items.at(index).get('exportFormat')?.value === ExportFileFormat.Csv;
  }

  isWriteSql(sql: string | undefined): boolean {
    const trimmed = (sql || '').trimStart().toUpperCase();
    return trimmed.startsWith('INSERT') || trimmed.startsWith('UPDATE') || trimmed.startsWith('DELETE');
  }

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', Validators.required],
      description: [''],
      isEnabled: [true],
      outputFolder: ['', Validators.required],
      archiveFolder: [''],
      combineOutput: [false],
      includeHeaders: [true],
      combinedFileName: [''],
      combinedFormat: [ExportFileFormat.Csv],
      combinedCsvSeparator: [','],
      combinedAppendTimestamp: [true],
      timestampFormat: [''],
      triggers: this.fb.array([]),
      items: this.fb.array([]),
      viewerUserIds: [[] as string[]]
    });

    this.taskId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.taskId;

    forkJoin({
      queries: this.queryService.getAllQueries(),
      users: this.queryService.getAllUsers()
    }).subscribe({
      next: ({ queries, users }) => {
        this.queries = queries.filter(q => q.isEnabled);
        this.users = users.filter(u => u.isActive);
        if (this.taskId) {
          this.loadTask(this.taskId);
        } else {
          this.addTrigger();
          this.addItem();
          this.loading = false;
          this.cdr.detectChanges();
        }
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load queries/users', 'Close', { duration: 5000 });
        this.cdr.detectChanges();
      }
    });
  }

  private loadTask(id: string): void {
    this.scheduledTaskService.getById(id).subscribe({
      next: (task) => {
        this.form.patchValue({
          name: task.name,
          description: task.description,
          isEnabled: task.isEnabled,
          outputFolder: task.outputFolder,
          archiveFolder: task.archiveFolder || '',
          combineOutput: !!task.combineOutput,
          includeHeaders: task.includeHeaders !== false,
          combinedFileName: task.combinedFileName || '',
          combinedFormat: task.combinedFormat ?? ExportFileFormat.Csv,
          combinedCsvSeparator: task.combinedCsvSeparator === '\t' ? 'tab' : (task.combinedCsvSeparator || ','),
          combinedAppendTimestamp: task.combinedAppendTimestamp !== false,
          timestampFormat: task.timestampFormat || '',
          viewerUserIds: task.viewers.map(v => v.userId)
        });
        for (const trigger of (task.triggers?.length ? task.triggers : [null])) {
          this.addTrigger();
          if (trigger) {
            this.triggers.at(this.triggers.length - 1).patchValue({
              frequency: trigger.frequency,
              intervalMinutes: trigger.intervalMinutes ?? 60,
              timeOfDay: trigger.timeOfDay || '07:00',
              dayOfWeek: trigger.dayOfWeek ?? 1,
              dayOfMonth: trigger.dayOfMonth ?? 1
            });
          }
        }
        for (const item of task.items) {
          this.addItem();
          const index = this.items.length - 1;
          const group = this.items.at(index) as FormGroup;
          group.patchValue({
            dynamicQueryId: item.dynamicQueryId,
            exportFormat: item.exportFormat,
            // A stored tab character is shown as the word "tab" (the backend accepts both).
            csvSeparator: item.csvSeparator === '\t' ? 'tab' : (item.csvSeparator || ','),
            fileNamePrefix: item.fileNamePrefix || '',
            appendTimestamp: item.appendTimestamp,
            keyColumn: item.keyColumn || '',
            keyParameter: item.keyParameter || '',
            initialKey: item.initialKey || '',
            resetKey: false
          });
          this.buildParameterControls(index, item.dynamicQueryId, item.parameters);
          this.itemMeta[index] = {
            isWrite: item.isWriteQuery,
            lastKeyValue: item.lastKeyValue || null
          };
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: () => {
        this.loading = false;
        this.snackBar.open('Failed to load the scheduled task', 'Close', { duration: 5000 });
        this.router.navigate(['/admin/scheduled-tasks']);
        this.cdr.detectChanges();
      }
    });
  }

  addTrigger(): void {
    this.triggers.push(this.fb.group({
      frequency: [ScheduleFrequency.Daily, Validators.required],
      intervalMinutes: [60],
      timeOfDay: ['07:00'],
      dayOfWeek: [1],
      dayOfMonth: [1]
    }));
  }

  removeTrigger(index: number): void {
    this.triggers.removeAt(index);
  }

  addItem(): void {
    this.items.push(this.fb.group({
      dynamicQueryId: ['', Validators.required],
      exportFormat: [ExportFileFormat.Excel, Validators.required],
      csvSeparator: [','],
      fileNamePrefix: [''],
      appendTimestamp: [true],
      keyColumn: [''],
      keyParameter: [''],
      initialKey: [''],
      resetKey: [false],
      parameters: this.fb.group({})
    }));
    this.parameterDefs.push([]);
    this.itemMeta.push({ isWrite: false, lastKeyValue: null });
  }

  removeItem(index: number): void {
    this.items.removeAt(index);
    this.parameterDefs.splice(index, 1);
    this.itemMeta.splice(index, 1);
  }

  onQueryChange(index: number, queryId: string): void {
    this.buildParameterControls(index, queryId, {});
    const query = this.queries.find(q => q.id === queryId);
    this.itemMeta[index] = { isWrite: this.isWriteSql(query?.sqlQuery), lastKeyValue: null };
    // A different query means any previous checkpoint config no longer applies.
    (this.items.at(index) as FormGroup).patchValue({ keyColumn: '', keyParameter: '', initialKey: '', resetKey: false });
  }

  queryName(index: number): string {
    const id = this.items.at(index).get('dynamicQueryId')?.value;
    return this.queries.find(q => q.id === id)?.name || '';
  }

  /** Rebuilds the per-parameter inputs for a row from the selected query's definitions. */
  private buildParameterControls(index: number, queryId: string, values: Record<string, string>): void {
    const query = this.queries.find(q => q.id === queryId);
    const defs = (query?.parameters || [])
      .slice()
      .sort((a, b) => a.sortOrder - b.sortOrder)
      .map(p => ({
        name: p.name,
        displayName: p.displayName,
        isRequired: p.isRequired,
        allowMultiple: !!p.allowMultiple
      }));
    this.parameterDefs[index] = defs;

    const group = this.fb.group({});
    for (const def of defs) {
      // Multi-value params are stored in the JSON-array wire format; show them
      // back to the admin as a comma-separated list.
      let value = values[def.name] ?? '';
      if (def.allowMultiple) {
        value = this.jsonArrayToCommaList(value);
      }
      group.addControl(
        def.name,
        this.fb.control(value, def.isRequired ? Validators.required : [])
      );
    }
    (this.items.at(index) as FormGroup).setControl('parameters', group);
  }

  private jsonArrayToCommaList(value: string): string {
    if (!value) return '';
    try {
      const parsed = JSON.parse(value);
      return Array.isArray(parsed) ? parsed.join(', ') : value;
    } catch {
      return value;
    }
  }

  /**
   * Converts the row's raw form values into the backend wire format: multi-value
   * params ("a, b, c") become a JSON-array string, matching what the query-execute
   * page sends and what the server's multi-select parser expects.
   */
  private toWireParameters(index: number, raw: Record<string, string>): Record<string, string> {
    const wire: Record<string, string> = {};
    for (const def of this.parameterDefs[index] || []) {
      let value = String(raw[def.name] ?? '');
      if (def.allowMultiple) {
        const items = value.split(',').map(v => v.trim()).filter(v => v.length > 0);
        value = JSON.stringify(items);
      }
      wire[def.name] = value;
    }
    return wire;
  }

  save(): void {
    if (this.form.invalid) return;
    this.saving = true;

    const value = this.form.value;
    const request: SaveScheduledTaskRequest = {
      name: value.name,
      description: value.description || '',
      isEnabled: value.isEnabled,
      outputFolder: value.outputFolder,
      archiveFolder: value.archiveFolder || null,
      combineOutput: !!value.combineOutput,
      includeHeaders: !!value.includeHeaders,
      combinedFileName: value.combineOutput ? (value.combinedFileName || null) : null,
      combinedFormat: value.combinedFormat ?? ExportFileFormat.Csv,
      combinedCsvSeparator: value.combineOutput && value.combinedFormat === ExportFileFormat.Csv
        ? (value.combinedCsvSeparator || null)
        : null,
      combinedAppendTimestamp: !!value.combinedAppendTimestamp,
      timestampFormat: value.timestampFormat?.trim() || null,
      triggers: (value.triggers as any[]).map((trigger, i) => ({
        frequency: trigger.frequency,
        intervalMinutes: trigger.frequency === ScheduleFrequency.EveryNMinutes ? trigger.intervalMinutes : null,
        timeOfDay: trigger.frequency === ScheduleFrequency.EveryNMinutes ? null : trigger.timeOfDay,
        dayOfWeek: trigger.frequency === ScheduleFrequency.Weekly ? trigger.dayOfWeek : null,
        dayOfMonth: trigger.frequency === ScheduleFrequency.Monthly ? trigger.dayOfMonth : null,
        sortOrder: i
      })),
      items: (value.items as any[]).map((item, i) => ({
        dynamicQueryId: item.dynamicQueryId,
        parameters: this.toWireParameters(i, item.parameters || {}),
        exportFormat: item.exportFormat,
        csvSeparator: item.exportFormat === ExportFileFormat.Csv ? (item.csvSeparator || null) : null,
        fileNamePrefix: item.fileNamePrefix || null,
        appendTimestamp: item.appendTimestamp,
        sortOrder: i,
        keyColumn: this.itemMeta[i]?.isWrite ? null : (item.keyColumn || null),
        keyParameter: this.itemMeta[i]?.isWrite ? null : (item.keyParameter || null),
        initialKey: this.itemMeta[i]?.isWrite ? null : (item.initialKey || null),
        resetKey: !!item.resetKey
      })),
      viewerUserIds: value.viewerUserIds || []
    };

    const call = this.isEdit && this.taskId
      ? this.scheduledTaskService.update(this.taskId, request)
      : this.scheduledTaskService.create(request);

    call.subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Scheduled task saved', 'Close', { duration: 3000 });
        this.router.navigate(['/admin/scheduled-tasks']);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.snackBar.open(err?.error?.message || 'Failed to save the scheduled task', 'Close', { duration: 6000 });
        this.cdr.detectChanges();
      }
    });
  }
}
