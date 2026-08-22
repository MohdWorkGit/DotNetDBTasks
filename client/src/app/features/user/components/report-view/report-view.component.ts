import { Component, OnInit, OnDestroy, ChangeDetectorRef, inject } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { ReportService, ReportExportFormat } from '@core/services/report.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { AuthService } from '@core/services/auth.service';
import { ParameterType } from '@core/models/dynamic-query.model';
import { ExportFormatOption } from '@core/models/export-formats';
import { Report, ReportRun, REPORT_EXPORT_FORMATS } from '@core/models/report.model';
import { ReportChartComponent } from '@shared/components/report-chart.component';

/**
 * Runs a report and shows it: one parameter form, then one tab per dataset.
 *
 * <p>The rows are never held here. A run files each section into the server's result cache and
 * returns a job id per section, which the section grids page against — so a report with several
 * large datasets costs one page of rows in the browser, not all of them.</p>
 */
@Component({
  selector: 'app-report-view',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1 dir="auto">{{ report?.name || ('user.reports.title' | transloco) }}</h1>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <div class="error-block" *ngIf="error">
        <span class="error-text">{{ error }}</span>
      </div>

      <mat-card *ngIf="report && !loading">
        <mat-card-content>
          <p dir="auto" *ngIf="report.description" class="muted">{{ report.description }}</p>

          <form [formGroup]="form" (ngSubmit)="run()">
            <div class="params" *ngIf="report.parameters.length > 0">
              <ng-container *ngFor="let param of report.parameters">
                <mat-form-field appearance="outline" *ngIf="param.parameterType === 0">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [formControlName]="param.name" dir="auto" />
                </mat-form-field>

                <mat-form-field appearance="outline" *ngIf="param.parameterType === 1">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput type="number" [formControlName]="param.name" />
                </mat-form-field>

                <mat-form-field appearance="outline" *ngIf="param.parameterType === 2">
                  <mat-label>{{ param.displayName }}</mat-label>
                  <input matInput [matDatepicker]="picker" [formControlName]="param.name" />
                  <mat-datepicker-toggle matSuffix [for]="picker"></mat-datepicker-toggle>
                  <mat-datepicker #picker></mat-datepicker>
                </mat-form-field>

                <div class="toggle-field" *ngIf="param.parameterType === 3">
                  <mat-slide-toggle [formControlName]="param.name">
                    {{ param.displayName }}
                  </mat-slide-toggle>
                </div>
              </ng-container>
            </div>

            <div class="actions">
              <button type="submit" mat-flat-button color="primary" [disabled]="running">
                <mat-icon>play_arrow</mat-icon> {{ 'user.reports.run' | transloco }}
              </button>

              <button type="button" mat-stroked-button *ngIf="run_ && availableFormats.length > 0"
                      [matMenuTriggerFor]="exportMenu" [disabled]="exporting">
                <mat-icon>download</mat-icon> {{ 'user.execute.export' | transloco }}
              </button>
              <mat-menu #exportMenu="matMenu">
                <button mat-menu-item *ngFor="let f of availableFormats"
                        (click)="exportRun(f.apiValue)">
                  <mat-icon>{{ f.icon }}</mat-icon> {{ f.labelKey | transloco }}
                </button>
              </mat-menu>
            </div>
          </form>

          <mat-progress-bar mode="indeterminate" *ngIf="running"></mat-progress-bar>
        </mat-card-content>
      </mat-card>

      <div class="warnings" *ngIf="run_ && run_.warnings.length > 0">
        <p class="warn" *ngFor="let warning of run_.warnings">
          <mat-icon>warning</mat-icon> {{ warning }}
        </p>
      </div>

      <mat-card *ngIf="run_ && run_.charts.length" class="results">
        <mat-card-content>
          <app-report-chart *ngFor="let c of run_.charts" [chart]="c"></app-report-chart>
        </mat-card-content>
      </mat-card>

      <mat-card *ngIf="run_" class="results">
        <mat-card-content>
          <p class="muted">
            {{ 'user.reports.ranIn' | transloco: { ms: run_.executionDurationMs } }}
          </p>

          <mat-tab-group>
            <mat-tab *ngFor="let section of visibleSections" [label]="section.title">
              <div class="section-body">
                <div class="error-block" *ngIf="section.error">
                  <span class="error-text">{{ section.error }}</span>
                </div>

                <app-report-section-grid
                  *ngIf="section.jobId"
                  [jobId]="section.jobId"
                  [columns]="section.columns">
                </app-report-section-grid>
              </div>
            </mat-tab>
          </mat-tab-group>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .params {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(250px, 1fr));
      gap: 16px;
    }
    .toggle-field { display: flex; align-items: center; min-block-size: 56px; }
    .actions { display: flex; gap: 8px; margin-block-start: 8px; }
    .results { margin-block-start: 16px; }
    .section-body { padding-block-start: 12px; }
    .muted { color: var(--text-secondary); }
    .warnings { margin-block-start: 12px; }
    .warn { display: flex; align-items: center; gap: 8px; color: var(--status-warning-text); }
  `]
})
export class ReportViewComponent implements OnInit, OnDestroy {
  private fb = inject(FormBuilder);
  private reports = inject(ReportService);
  private route = inject(ActivatedRoute);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private authService = inject(AuthService);
  private cdr = inject(ChangeDetectorRef);

  form: FormGroup = this.fb.group({});
  report: Report | null = null;
  /** Named with a trailing underscore because `run` is the submit handler. */
  run_: ReportRun | null = null;
  availableFormats: ExportFormatOption[] = [];

  loading = true;
  running = false;
  exporting = false;
  error = '';

  private reportId = '';

  get visibleSections() {
    return this.run_?.sections.filter(s => s.isVisibleInViewer) ?? [];
  }

  ngOnInit(): void {
    this.reportId = this.route.snapshot.paramMap.get('id') ?? '';
    this.reports.getMyReport(this.reportId).subscribe({
      next: report => {
        this.report = report;
        this.buildForm(report);
        // Both gates already applied: the server sends only the formats this report permits,
        // and this narrows again by what the signed-in user holds.
        this.availableFormats = REPORT_EXPORT_FORMATS.filter(f =>
          report.allowedExportFormats.includes(f.name) && this.authService.has(f.permission));
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.error = extractApiError(err, this.transloco.translate('user.reports.loadFailed'));
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  ngOnDestroy(): void {
    // Release the run's cached sections rather than leaving them to age out one at a time.
    if (this.run_) {
      this.reports.releaseRun(this.run_.runId).subscribe({ error: () => { /* best effort */ } });
    }
  }

  private buildForm(report: Report): void {
    for (const parameter of [...report.parameters].sort((a, b) => a.sortOrder - b.sortOrder)) {
      const validators = parameter.isRequired ? [Validators.required] : [];
      const initial = parameter.parameterType === ParameterType.Boolean
        ? parameter.defaultValue === 'true'
        : parameter.defaultValue ?? '';
      this.form.addControl(parameter.name, this.fb.control(initial, validators));
    }
  }

  run(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toast.error(null, 'user.reports.fillRequired');
      return;
    }

    // A previous run's sections are released before the new one replaces them, so re-running
    // does not leave the old result set occupying the cache.
    const previous = this.run_;
    this.running = true;

    this.reports.run(this.reportId, this.buildParameters()).subscribe({
      next: result => {
        this.run_ = result;
        this.running = false;
        if (previous) {
          this.reports.releaseRun(previous.runId).subscribe({ error: () => { /* best effort */ } });
        }
        this.cdr.detectChanges();
      },
      error: err => {
        this.running = false;
        this.toast.error(err, 'user.reports.runFailed');
        this.cdr.detectChanges();
      }
    });
  }

  /** The same wire format the query run form uses: every value as a string. */
  private buildParameters(): Record<string, string> {
    const parameters: Record<string, string> = {};
    for (const parameter of this.report?.parameters ?? []) {
      const value = this.form.get(parameter.name)?.value;
      if (value === null || value === undefined || value === '') continue;

      if (parameter.parameterType === ParameterType.Date && value instanceof Date) {
        parameters[parameter.name] = value.toISOString().split('T')[0];
      } else {
        parameters[parameter.name] = String(value);
      }
    }
    return parameters;
  }

  exportRun(format: ReportExportFormat): void {
    if (!this.run_) return;

    this.exporting = true;
    this.reports.exportRun(this.run_.runId, format).subscribe({
      next: blob => {
        this.exporting = false;
        const name = `${this.report?.name ?? 'report'}_${new Date().toISOString().split('T')[0]}.${format}`;
        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = name;
        anchor.click();
        window.URL.revokeObjectURL(url);
        this.cdr.detectChanges();
      },
      error: err => {
        this.exporting = false;
        this.toast.error(err, 'user.reports.exportFailed');
        this.cdr.detectChanges();
      }
    });
  }
}
