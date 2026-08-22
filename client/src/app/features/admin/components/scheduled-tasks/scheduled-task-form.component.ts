import { ChangeDetectorRef, Component, OnInit } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { forkJoin } from 'rxjs';
import { QueryService } from '@core/services/query.service';
import { ScheduledTaskService } from '@core/services/scheduled-task.service';
import { ReportSummary } from '@core/models/report.model';
import { ReportService } from '@core/services/report.service';
import { DynamicQuery, isWriteQueryType } from '@core/models/dynamic-query.model';
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
        <h2>{{ (isEdit ? 'admin.tasks.editTitle' : 'admin.tasks.createTitle') | transloco }}</h2>
        <a mat-button routerLink="/admin/scheduled-tasks">
          <mat-icon class="rtl-flip">arrow_back</mat-icon> {{ 'common.back' | transloco }}
        </a>
      </div>

      <div *ngIf="loading" class="loading">
        <mat-spinner diameter="40"></mat-spinner>
      </div>

      <form [formGroup]="form" (ngSubmit)="save()" *ngIf="!loading">
        <mat-card class="section">
          <mat-card-title>{{ 'admin.tasks.taskSection' | transloco }}</mat-card-title>
          <mat-card-content>
            <div class="row">
              <mat-form-field appearance="outline" class="grow">
                <mat-label>{{ 'admin.tasks.name' | transloco }}</mat-label>
                <input matInput formControlName="name" maxlength="200" required>
                <mat-error *ngIf="form.get('name')?.hasError('required')">{{ 'common.nameRequired' | transloco }}</mat-error>
              </mat-form-field>
              <mat-slide-toggle formControlName="isEnabled" class="toggle">{{ 'admin.tasks.enabledToggle' | transloco }}</mat-slide-toggle>
            </div>

            <mat-form-field appearance="outline" class="full">
              <mat-label>{{ 'admin.tasks.description' | transloco }}</mat-label>
              <input matInput formControlName="description" maxlength="1000">
            </mat-form-field>

            <mat-form-field appearance="outline" class="full">
              <mat-label>{{ 'admin.tasks.outputFolder' | transloco }}</mat-label>
              <input matInput formControlName="outputFolder" maxlength="500"
                     placeholder="D:\\Exports\\Sales" required>
              <mat-hint>{{ 'admin.tasks.outputFolderHint' | transloco }}</mat-hint>
              <mat-error *ngIf="form.get('outputFolder')?.hasError('required')">
                {{ 'admin.tasks.outputFolderRequired' | transloco }}
              </mat-error>
            </mat-form-field>

            <mat-form-field appearance="outline" class="full">
              <mat-label>{{ 'admin.tasks.archiveFolder' | transloco }}</mat-label>
              <input matInput formControlName="archiveFolder" maxlength="500"
                     placeholder="D:\\Exports\\Archive">
              <mat-hint>{{ 'admin.tasks.archiveHint' | transloco }}</mat-hint>
            </mat-form-field>

            <div class="row output-options">
              <mat-slide-toggle formControlName="combineOutput"
                                [matTooltip]="'admin.tasks.combineTip' | transloco">
                {{ 'admin.tasks.combineOutput' | transloco }}
              </mat-slide-toggle>
              <mat-slide-toggle formControlName="includeHeaders"
                                [matTooltip]="'admin.tasks.headersTip' | transloco">
                {{ 'admin.tasks.includeHeaders' | transloco }}
              </mat-slide-toggle>
              <mat-form-field appearance="outline" class="timestamp-format">
                <mat-label>{{ 'admin.tasks.timestampFormat' | transloco }}</mat-label>
                <input matInput formControlName="timestampFormat" maxlength="50"
                       placeholder="_yyyyMMdd-HHmmss">
                <mat-hint>{{ 'admin.tasks.timestampFormatHint' | transloco }}</mat-hint>
              </mat-form-field>
            </div>

            <div class="row wrap" *ngIf="combineOutput">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.tasks.format' | transloco }}</mat-label>
                <mat-select formControlName="combinedFormat" required>
                  <mat-option *ngFor="let f of formats" [value]="f">{{ formatLabels[f] }}</mat-option>
                </mat-select>
              </mat-form-field>
              <mat-form-field appearance="outline" class="separator"
                              *ngIf="form.get('combinedFormat')?.value === Format.Csv">
                <mat-label>{{ 'admin.tasks.separator' | transloco }}</mat-label>
                <input matInput formControlName="combinedCsvSeparator" maxlength="8" placeholder=",">
                <mat-hint>{{ 'admin.tasks.separatorHint' | transloco }}</mat-hint>
              </mat-form-field>
              <p class="word-note" *ngIf="form.get('combinedFormat')?.value === Format.Word">
                <mat-icon inline>article</mat-icon>
                {{ 'admin.tasks.combinedWordNote' | transloco }}
              </p>
              <mat-form-field appearance="outline" class="grow">
                <mat-label>{{ 'admin.tasks.fileName' | transloco }}</mat-label>
                <input matInput formControlName="combinedFileName" maxlength="200"
                       [attr.placeholder]="'admin.tasks.fileNamePlaceholder' | transloco">
              </mat-form-field>
              <mat-slide-toggle formControlName="combinedAppendTimestamp" class="toggle"
                                [matTooltip]="'admin.tasks.timestampTip' | transloco">
                {{ 'admin.tasks.appendTimestamp' | transloco }}
              </mat-slide-toggle>
            </div>
            <p class="hint" *ngIf="combineOutput">
              {{ 'admin.tasks.headerRowNote' | transloco }}
            </p>
          </mat-card-content>
        </mat-card>

        <mat-card class="section">
          <mat-card-title>
            {{ 'admin.tasks.scheduleSection' | transloco }}
            <button mat-stroked-button type="button" color="primary" class="add-item" (click)="addTrigger()">
              <mat-icon>add</mat-icon> {{ 'admin.tasks.addTrigger' | transloco }}
            </button>
          </mat-card-title>
          <mat-card-content formArrayName="triggers">
            <p class="hint">
              {{ 'admin.tasks.triggersHint' | transloco }}
            </p>

            <div class="row" *ngFor="let trigger of triggers.controls; let i = index" [formGroupName]="i">
              <mat-form-field appearance="outline">
                <mat-label>{{ 'admin.tasks.frequency' | transloco }}</mat-label>
                <mat-select formControlName="frequency" required>
                  <mat-option [value]="Frequency.EveryNMinutes">{{ 'admin.tasks.freqEveryNMinutes' | transloco }}</mat-option>
                  <mat-option [value]="Frequency.Daily">{{ 'admin.tasks.freqDaily' | transloco }}</mat-option>
                  <mat-option [value]="Frequency.Weekly">{{ 'admin.tasks.freqWeekly' | transloco }}</mat-option>
                  <mat-option [value]="Frequency.Monthly">{{ 'admin.tasks.freqMonthly' | transloco }}</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.EveryNMinutes">
                <mat-label>{{ 'admin.tasks.intervalMinutes' | transloco }}</mat-label>
                <input matInput type="number" formControlName="intervalMinutes" min="1">
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.Weekly">
                <mat-label>{{ 'admin.tasks.dayOfWeek' | transloco }}</mat-label>
                <mat-select formControlName="dayOfWeek">
                  <mat-option *ngFor="let d of dayNames; let di = index" [value]="di">{{ d }}</mat-option>
                </mat-select>
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) === Frequency.Monthly">
                <mat-label>{{ 'admin.tasks.dayOfMonth' | transloco }}</mat-label>
                <input matInput type="number" formControlName="dayOfMonth" min="1" max="31">
              </mat-form-field>

              <mat-form-field appearance="outline" *ngIf="triggerFrequency(i) !== Frequency.EveryNMinutes">
                <mat-label>{{ 'admin.tasks.timeOfDay' | transloco }}</mat-label>
                <input matInput type="time" formControlName="timeOfDay">
              </mat-form-field>

              <button mat-icon-button type="button" color="warn" [matTooltip]="'admin.tasks.removeTrigger' | transloco" [attr.aria-label]="'admin.tasks.removeTrigger' | transloco"
                      class="remove-item" *ngIf="triggers.length > 1" (click)="removeTrigger(i)">
                <mat-icon>close</mat-icon>
              </button>
            </div>
          </mat-card-content>
        </mat-card>

        <mat-card class="section">
          <mat-card-title>
            {{ 'admin.tasks.queriesSection' | transloco }}
            <button mat-stroked-button type="button" color="primary" class="add-item" (click)="addItem()">
              <mat-icon>add</mat-icon> {{ 'admin.tasks.addQuery' | transloco }}
            </button>
          </mat-card-title>
          <mat-card-content formArrayName="items">
            <p class="hint" *ngIf="items.length === 0">
              {{ 'admin.tasks.noItemsHint' | transloco }}
            </p>

            <div class="item" *ngFor="let item of items.controls; let i = index" [formGroupName]="i">
              <div class="row">
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'admin.tasks.itemKind' | transloco }}</mat-label>
                  <mat-select formControlName="itemKind" (selectionChange)="onItemKindChange(i)">
                    <mat-option value="query">{{ 'admin.tasks.itemKindQuery' | transloco }}</mat-option>
                    <mat-option value="report">{{ 'admin.tasks.itemKindReport' | transloco }}</mat-option>
                  </mat-select>
                </mat-form-field>

                <mat-form-field appearance="outline" class="grow"
                                *ngIf="item.get('itemKind')?.value === 'query'">
                  <mat-label>{{ 'admin.tasks.query' | transloco }}</mat-label>
                  <mat-select formControlName="dynamicQueryId"
                              (selectionChange)="onQueryChange(i, $event.value)">
                    <mat-option *ngFor="let q of selectableQueries" [value]="q.id">
                      {{ q.name }}<span *ngIf="isWriteQuery(q)"> {{ 'admin.tasks.modifiesData' | transloco }}</span>
                    </mat-option>
                  </mat-select>
                  <mat-error *ngIf="item.get('dynamicQueryId')?.hasError('required')">
                    {{ 'admin.tasks.pickQuery' | transloco }}
                  </mat-error>
                </mat-form-field>

                <mat-form-field appearance="outline" class="grow"
                                *ngIf="item.get('itemKind')?.value === 'report'">
                  <mat-label>{{ 'admin.tasks.report' | transloco }}</mat-label>
                  <mat-select formControlName="reportId">
                    <mat-option *ngFor="let r of reports" [value]="r.id" dir="auto">{{ r.name }}</mat-option>
                  </mat-select>
                  <mat-error *ngIf="item.get('reportId')?.hasError('required')">
                    {{ 'admin.tasks.pickReport' | transloco }}
                  </mat-error>
                  <mat-hint>{{ 'admin.tasks.reportItemHint' | transloco }}</mat-hint>
                </mat-form-field>

                <mat-form-field appearance="outline"
                                *ngIf="item.get('itemKind')?.value === 'report'
                                       || (!itemMeta[i]?.isWrite && !combineOutput)">
                  <mat-label>{{ 'admin.tasks.format' | transloco }}</mat-label>
                  <mat-select formControlName="exportFormat" required>
                    <mat-option *ngFor="let f of formats" [value]="f">{{ formatLabels[f] }}</mat-option>
                  </mat-select>
                  <mat-error *ngIf="item.get('exportFormat')?.hasError('required')">
                    {{ 'admin.tasks.pickFormat' | transloco }}
                  </mat-error>
                </mat-form-field>

                <mat-form-field appearance="outline" class="separator"
                                *ngIf="!itemMeta[i]?.isWrite && !combineOutput && isCsv(i)">
                  <mat-label>{{ 'admin.tasks.separator' | transloco }}</mat-label>
                  <input matInput formControlName="csvSeparator" maxlength="8" placeholder=",">
                  <mat-hint>{{ 'admin.tasks.separatorHint' | transloco }}</mat-hint>
                </mat-form-field>

                <button mat-icon-button type="button" color="warn" [matTooltip]="'admin.tasks.removeQuery' | transloco" [attr.aria-label]="'admin.tasks.removeQuery' | transloco"
                        class="remove-item" (click)="removeItem(i)">
                  <mat-icon>close</mat-icon>
                </button>
              </div>

              <p class="write-note" *ngIf="itemMeta[i]?.isWrite">
                <mat-icon inline>edit_note</mat-icon>
                {{ 'admin.tasks.writeItemNote' | transloco }}
              </p>

              <p class="word-note" *ngIf="!itemMeta[i]?.isWrite && !combineOutput && isWord(i)">
                <mat-icon inline>article</mat-icon>
                <ng-container *ngIf="itemTemplateName(i); else defaultWordTpl">
                  {{ 'admin.tasks.wordTemplateUsed' | transloco }} <b>{{ itemTemplateName(i) }}</b>
                </ng-container>
                <ng-template #defaultWordTpl>
                  {{ 'admin.tasks.wordTemplateDefault' | transloco }}
                </ng-template>
              </p>

              <div class="row" *ngIf="!itemMeta[i]?.isWrite && !combineOutput">
                <mat-form-field appearance="outline" class="grow">
                  <mat-label>{{ 'admin.tasks.fileName' | transloco }}</mat-label>
                  <input matInput formControlName="fileNamePrefix" maxlength="200"
                         [placeholder]="queryName(i) || ('admin.tasks.fileNameQueryDefault' | transloco)">
                </mat-form-field>
                <mat-slide-toggle formControlName="appendTimestamp" class="toggle"
                                  [matTooltip]="'admin.tasks.timestampTip' | transloco">
                  {{ 'admin.tasks.appendTimestamp' | transloco }}
                </mat-slide-toggle>
              </div>

              <div class="checkpoint" *ngIf="!itemMeta[i]?.isWrite">
                <div class="params-title">
                  {{ 'admin.tasks.incrementalHint' | transloco }}
                </div>
                <div class="row wrap">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.tasks.keyColumn' | transloco }}</mat-label>
                    <input matInput formControlName="keyColumn" maxlength="128"
                           [attr.placeholder]="'admin.tasks.keyColumnPlaceholder' | transloco">
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.tasks.keyParameter' | transloco }}</mat-label>
                    <mat-select formControlName="keyParameter">
                      <mat-option [value]="''">—</mat-option>
                      <mat-option *ngFor="let p of parameterDefs[i]" [value]="p.name">{{ p.name }}</mat-option>
                    </mat-select>
                  </mat-form-field>
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.tasks.initialKey' | transloco }}</mat-label>
                    <input matInput formControlName="initialKey" maxlength="500" placeholder="e.g. 0">
                  </mat-form-field>
                </div>
                <div class="row" *ngIf="itemMeta[i]?.lastKeyValue">
                  <span class="saved-key">{{ 'admin.tasks.savedCheckpoint' | transloco }} <code>{{ itemMeta[i].lastKeyValue }}</code></span>
                  <mat-checkbox formControlName="resetKey">{{ 'admin.tasks.resetCheckpoint' | transloco }}</mat-checkbox>
                </div>
              </div>

              <div class="params" formGroupName="parameters" *ngIf="parameterDefs[i]?.length">
                <div class="params-title">{{ 'admin.tasks.parameterValues' | transloco }}</div>
                <div class="row wrap">
                  <mat-form-field appearance="outline" *ngFor="let p of parameterDefs[i]">
                    <mat-label>{{ p.displayName }}{{ p.isRequired ? ' *' : '' }}</mat-label>
                    <input matInput [formControlName]="p.name"
                           [placeholder]="p.allowMultiple ? 'value1, value2, value3' : ''">
                    <mat-hint *ngIf="p.allowMultiple">{{ 'admin.tasks.multiValueHint' | transloco }}</mat-hint>
                  </mat-form-field>
                </div>
              </div>
            </div>
          </mat-card-content>
        </mat-card>

        <div class="actions">
          <!-- Kept enabled when invalid: save() then explains what is wrong, instead
               of leaving a permanently dead button with no on-screen reason. -->
          <button mat-raised-button color="primary" type="submit" [disabled]="saving">
            <mat-icon>save</mat-icon> {{ saving ? 'Saving…' : 'Save' }}
          </button>
          <button mat-button type="button" routerLink="/admin/scheduled-tasks">{{ 'common.cancel' | transloco }}</button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    .section { margin-bottom: 16px; }
    .section mat-card-title {
      display: flex;
      justify-content: space-between;
      align-items: center;
      font-size: 16px;
      margin-bottom: 12px;
    }
    .row { display: flex; gap: 12px; align-items: baseline; flex-wrap: wrap; }
    .grow { flex: 1; }
    .full { width: 100%; }
    .toggle { margin-bottom: 20px; align-self: center; }
    .item {
      border: 1px solid var(--border-color);
      border-radius: 8px;
      padding: 12px 12px 0;
      margin-bottom: 12px;
    }
    .write-note {
      color: var(--status-warning);
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0 0 12px;
    }
    .word-note {
      color: var(--text-secondary);
      display: flex;
      align-items: center;
      gap: 6px;
      margin: 0 0 12px;
      font-size: 13px;
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
  formats = [ExportFileFormat.Excel, ExportFileFormat.Csv, ExportFileFormat.Json, ExportFileFormat.Pdf, ExportFileFormat.Word];
  formatLabels = EXPORT_FORMAT_LABELS;
  dayNames = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

  form!: FormGroup;
  isEdit = false;
  taskId: string | null = null;
  loading = true;
  saving = false;

  queries: DynamicQuery[] = [];
  /** Parameter definitions of the query selected in each item row, by row index. */
  parameterDefs: { name: string; displayName: string; isRequired: boolean; allowMultiple: boolean }[][] = [];
  /** Per-row info about the selected query: write vs read, and the saved checkpoint when editing. */
  reports: ReportSummary[] = [];
  itemMeta: { isWrite: boolean; lastKeyValue: string | null }[] = [];

  constructor(
    private fb: FormBuilder,
    private route: ActivatedRoute,
    private router: Router,
    private queryService: QueryService,
    private reportService: ReportService,
    private scheduledTaskService: ScheduledTaskService,
    private toast: ToastService,
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

  isWord(index: number): boolean {
    return this.items.at(index).get('exportFormat')?.value === ExportFileFormat.Word;
  }

  /** File name of the selected query's Word template, or null when it has none. */
  itemTemplateName(index: number): string | null {
    const id = this.items.at(index).get('dynamicQueryId')?.value;
    if (!id) return null;
    return this.queries.find(q => q.id === id)?.wordTemplateFileName || null;
  }

  isCsv(index: number): boolean {
    return this.items.at(index).get('exportFormat')?.value === ExportFileFormat.Csv;
  }

  /** Reads the type the server derived at save time rather than re-parsing the SQL here. */
  isWriteQuery(query: DynamicQuery | undefined): boolean {
    return isWriteQueryType(query?.queryType);
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
      items: this.fb.array([])
    });

    this.taskId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.taskId;

    forkJoin({
      queries: this.queryService.getAllQueries(),
      reports: this.reportService.getAll()
    }).subscribe({
      next: ({ queries, reports }) => {
        this.queries = queries.filter(q => q.isEnabled);
        this.reports = reports.filter(r => r.isEnabled);
        if (this.taskId) {
          this.loadTask(this.taskId);
        } else {
          this.addTrigger();
          this.addItem();
          this.loading = false;
          this.cdr.detectChanges();
        }
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.tasks.lookupsFailed');
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
          timestampFormat: task.timestampFormat || ''
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
            itemKind: item.reportId ? 'report' : 'query',
            dynamicQueryId: item.dynamicQueryId ?? '',
            reportId: item.reportId ?? '',
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
          // A report item has no per-query parameter controls: its parameters belong to the
          // report, and it carries no checkpoint to resume from.
          if (item.reportId) {
            this.onItemKindChange(index);
          } else {
            this.buildParameterControls(index, item.dynamicQueryId!, item.parameters);
          }

          this.itemMeta[index] = {
            isWrite: item.isWriteQuery,
            lastKeyValue: item.lastKeyValue || null
          };
        }
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loading = false;
        this.toast.error(err, 'admin.tasks.loadOneFailed');
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
      // Which of the two is required depends on itemKind, so neither is unconditionally so;
      // onItemKindChange moves the validator to whichever picker is showing.
      itemKind: ['query'],
      dynamicQueryId: ['', Validators.required],
      reportId: [''],
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
    this.itemMeta[index] = { isWrite: this.isWriteQuery(query), lastKeyValue: null };
    // A different query means any previous checkpoint config no longer applies.
    (this.items.at(index) as FormGroup).patchValue({ keyColumn: '', keyParameter: '', initialKey: '', resetKey: false });
  }

  /**
   * Switches a row between running a query and running a report. The required-validator moves
   * with the visible picker, and the other side is cleared so a hidden control cannot block the
   * save or be submitted alongside its opposite — the server rejects an item carrying both.
   */
  onItemKindChange(index: number): void {
    const item = this.items.at(index) as FormGroup;
    const isReport = item.get('itemKind')?.value === 'report';

    const query = item.get('dynamicQueryId')!;
    const report = item.get('reportId')!;

    if (isReport) {
      query.clearValidators();
      query.setValue('');
      report.setValidators([Validators.required]);
      // Checkpoints are a per-query notion; a report has no key column to resume from.
      item.patchValue({ keyColumn: '', keyParameter: '', initialKey: '', resetKey: false });
      this.itemMeta[index] = { isWrite: false, lastKeyValue: null };
      (item.get('parameters') as FormGroup | null)?.reset();
    } else {
      report.clearValidators();
      report.setValue('');
      query.setValidators([Validators.required]);
    }

    query.updateValueAndValidity();
    report.updateValueAndValidity();
    this.cdr.detectChanges();
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

  /** Human-readable name of the first invalid control, for the save-blocked message. */
  private firstInvalidLabel(): string | null {
    const labels: Record<string, string> = {
      name: 'Name',
      outputFolder: 'Output folder',
      archiveFolder: 'Archive folder',
      combinedFormat: 'Combined output format',
      timestampFormat: 'Timestamp format',
      triggers: 'Schedule',
      items: 'Queries'
    };
    for (const key of Object.keys(this.form.controls)) {
      if (this.form.get(key)?.invalid) return labels[key] ?? key;
    }
    return null;
  }

  save(): void {
    if (this.form.invalid) {
      // Some required controls live inside *ngIf branches (e.g. combinedFormat only
      // renders when combineOutput is on), so form.invalid could be true with nothing
      // visible to fix. Reveal the errors and name the first offender.
      this.form.markAllAsTouched();
      const first = this.firstInvalidLabel();
      this.toast.error(
        first ? `Please check "${first}" — some required fields are missing or invalid.`
              : 'Some required fields are missing or invalid.',
        'Some required fields are missing or invalid.',
        6000
      );
      this.cdr.detectChanges();
      return;
    }
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
        dynamicQueryId: item.itemKind === 'report' ? null : item.dynamicQueryId,
        reportId: item.itemKind === 'report' ? item.reportId : null,
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
      }))
    };

    const call = this.isEdit && this.taskId
      ? this.scheduledTaskService.update(this.taskId, request)
      : this.scheduledTaskService.create(request);

    call.subscribe({
      next: () => {
        this.saving = false;
        this.toast.success('admin.tasks.saved');
        this.router.navigate(['/admin/scheduled-tasks']);
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'admin.tasks.saveFailed', 6000);
        this.cdr.detectChanges();
      }
    });
  }
}
