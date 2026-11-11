import { ChangeDetectorRef, Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatRadioModule } from '@angular/material/radio';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AiService } from '@core/services/ai.service';
import { extractApiError } from '@core/services/toast.service';
import { AiReportDraft } from '@core/models/ai.model';
import { DynamicQuery } from '@core/models/dynamic-query.model';
import { ReportChartType } from '@core/models/report.model';
import { BidiTextComponent, pageDirection, textDirection, TextDirection } from '@shared/components/bidi-text.component';
import { AiDraftMode } from '../dashboards/ai-dashboard-draft-dialog.component';

export interface AiReportDraftDialogData {
  /** Read queries only: a report section runs its query every time the report does. */
  queries: DynamicQuery[];
  /** The builder already has sections, so ask whether to add to them or replace them. */
  hasContent: boolean;
}

export interface AiReportDraftDialogResult {
  draft: AiReportDraft;
  mode: AiDraftMode;
}

/** The most queries one draft takes (DraftDashboardCommand.MaxQueries on the server). */
const MAX_QUERIES = 6;

const CHART_TYPES: Record<number, { icon: string; labelKey: string }> = {
  [ReportChartType.Column]: { icon: 'bar_chart', labelKey: 'admin.reports.typeColumn' },
  [ReportChartType.Bar]: { icon: 'align_horizontal_left', labelKey: 'admin.reports.typeBar' },
  [ReportChartType.Line]: { icon: 'show_chart', labelKey: 'admin.reports.typeLine' },
  [ReportChartType.Pie]: { icon: 'pie_chart', labelKey: 'admin.reports.typePie' },
  [ReportChartType.Area]: { icon: 'area_chart', labelKey: 'admin.reports.typeArea' },
  [ReportChartType.StackedColumn]: { icon: 'stacked_bar_chart', labelKey: 'admin.reports.typeStackedColumn' },
  [ReportChartType.StackedBar]: { icon: 'view_week', labelKey: 'admin.reports.typeStackedBar' },
  [ReportChartType.Doughnut]: { icon: 'donut_large', labelKey: 'admin.reports.typeDoughnut' }
};

/**
 * Asks the AI model to propose a report from saved queries — a section per query, charts on the
 * ones that are summaries, the parameters they need — and shows it for review before anything
 * reaches the form. Using it only fills the builder; it is saved, as any report is, with Save.
 */
@Component({
  standalone: true,
  selector: 'app-ai-report-draft-dialog',
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatListModule, MatProgressSpinnerModule, MatRadioModule, TranslocoModule, BidiTextComponent
  ],
  template: `
    <h2 mat-dialog-title>{{ 'admin.ai.draftReportTitle' | transloco }}</h2>
    <mat-dialog-content>
      <!-- ------------------------------------------------------------ pick -->
      <ng-container *ngIf="step === 'pick'">
        <p class="intro">{{ 'admin.ai.draftReportIntro' | transloco }}</p>

        <mat-form-field appearance="outline" class="full-width">
          <mat-label>{{ 'admin.ai.draftSearch' | transloco }}</mat-label>
          <mat-icon matPrefix>search</mat-icon>
          <input matInput [value]="search" (input)="onSearch($event)" dir="auto">
        </mat-form-field>

        <p class="caption" role="status">
          {{ 'admin.ai.draftPicked' | transloco: { count: selected.size, max: maxQueries } }}
        </p>
        <mat-selection-list class="queries" [attr.aria-label]="'admin.ai.draftQueries' | transloco">
          <mat-list-option *ngFor="let q of visibleQueries" togglePosition="before"
                           [selected]="selected.has(q.id)"
                           [disabled]="!selected.has(q.id) && selected.size >= maxQueries"
                           (selectedChange)="toggle(q.id, $event)">
            <span matListItemTitle dir="auto">{{ q.name }}</span>
            <span matListItemLine *ngIf="q.description" dir="auto">{{ q.description }}</span>
          </mat-list-option>
        </mat-selection-list>
        <p *ngIf="!visibleQueries.length" class="caption">{{ 'admin.ai.draftNoQueries' | transloco }}</p>

        <mat-form-field appearance="outline" class="full-width goal">
          <mat-label>{{ 'admin.ai.draftReportGoal' | transloco }}</mat-label>
          <textarea matInput rows="2" maxlength="500" [value]="goal" (input)="onGoal($event)" dir="auto"></textarea>
          <mat-hint>{{ 'admin.ai.draftReportGoalHint' | transloco }}</mat-hint>
        </mat-form-field>

        <mat-radio-group *ngIf="data.hasContent" class="mode" [value]="mode" (change)="mode = $event.value">
          <mat-radio-button value="add">{{ 'admin.ai.draftReportModeAdd' | transloco }}</mat-radio-button>
          <mat-radio-button value="replace">{{ 'admin.ai.draftReportModeReplace' | transloco }}</mat-radio-button>
        </mat-radio-group>

        <div *ngIf="error" class="ai-error" role="alert">
          <mat-icon>error_outline</mat-icon><span [attr.dir]="dirOf(error)">{{ error }}</span>
        </div>
      </ng-container>

      <!-- ------------------------------------------------------------ working -->
      <div *ngIf="step === 'working'" class="working" role="status">
        <mat-spinner diameter="32"></mat-spinner>
        <span>{{ 'admin.ai.draftReportWorking' | transloco }}</span>
      </div>

      <!-- ------------------------------------------------------------ review -->
      <ng-container *ngIf="step === 'review' && result">
        <p class="proposed">
          {{ 'admin.ai.draftReportProposed' | transloco }}
          <strong><app-bidi-text [text]="result.draft.name"></app-bidi-text></strong>
        </p>
        <p *ngIf="result.draft.description" class="caption" [attr.dir]="dirOf(result.draft.description)">
          <app-bidi-text [text]="result.draft.description"></app-bidi-text>
        </p>

        <h3>{{ 'admin.ai.draftReportSections' | transloco }}</h3>
        <ul class="items">
          <li *ngFor="let d of result.draft.datasets">
            <mat-icon class="kind">table_rows</mat-icon>
            <div>
              <div class="item-title"><app-bidi-text [text]="d.displayName"></app-bidi-text></div>
              <div class="item-meta">
                <app-bidi-text [text]="queryName(d.dynamicQueryId)"></app-bidi-text> · <code dir="ltr">{{ d.datasetKey }}</code>
              </div>
            </div>
          </li>
        </ul>

        <ng-container *ngIf="result.draft.charts.length">
          <h3>{{ 'admin.ai.draftReportCharts' | transloco }}</h3>
          <ul class="items">
            <li *ngFor="let c of result.draft.charts; let i = index">
              <mat-icon class="kind" [attr.aria-label]="chartLabel(c.chartType) | transloco">{{ chartIcon(c.chartType) }}</mat-icon>
              <div>
                <div class="item-title"><app-bidi-text [text]="c.title || ''"></app-bidi-text></div>
                <div class="item-meta">
                  {{ chartLabel(c.chartType) | transloco }} · <code dir="ltr">{{ c.datasetKey }}</code>
                </div>
                <p *ngIf="result.chartReasons[i]" class="reason" [attr.dir]="dirOf(result.chartReasons[i])">
                  <app-bidi-text [text]="result.chartReasons[i]"></app-bidi-text>
                </p>
              </div>
            </li>
          </ul>
        </ng-container>

        <ng-container *ngIf="result.notes.length">
          <h3>{{ 'admin.ai.draftNotes' | transloco }}</h3>
          <ul class="notes">
            <li *ngFor="let note of result.notes" [attr.dir]="dirOf(note)">
              <mat-icon>warning_amber</mat-icon><app-bidi-text [text]="note"></app-bidi-text>
            </li>
          </ul>
        </ng-container>

        <p class="disclaimer"><mat-icon>auto_awesome</mat-icon>{{ 'admin.ai.disclaimer' | transloco }}</p>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button *ngIf="step === 'review'" mat-button type="button" (click)="step = 'pick'">
        {{ 'admin.ai.draftBack' | transloco }}
      </button>
      <button *ngIf="step === 'review'" mat-stroked-button type="button" (click)="draft()">
        <mat-icon>refresh</mat-icon> {{ 'admin.ai.tryAgain' | transloco }}
      </button>
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | transloco }}</button>
      <button *ngIf="step === 'pick'" mat-raised-button color="primary" type="button"
              [disabled]="!selected.size" (click)="draft()">
        <mat-icon>auto_awesome</mat-icon> {{ 'admin.ai.draftRun' | transloco }}
      </button>
      <button *ngIf="step === 'review'" mat-raised-button color="primary" type="button" (click)="use()">
        <mat-icon>check</mat-icon> {{ 'admin.ai.draftUse' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Material's RTL dialog sets text-align: right outright, and an English line inherits it. */
    p[dir], li[dir], span[dir] { text-align: start; }
    .intro, .caption { color: var(--text-secondary); margin: 0 0 12px; max-width: 70ch; }
    .caption { font-size: 13px; }
    .full-width { width: 100%; }
    .queries {
      max-height: 260px; overflow: auto; margin-bottom: 16px;
      border: 1px solid var(--border-color); border-radius: 4px;
    }
    .goal { margin-top: 4px; }
    .mode { display: flex; flex-wrap: wrap; gap: 8px 24px; margin: 8px 0; }
    .working { display: flex; align-items: center; gap: 16px; padding: 24px 0; color: var(--text-secondary); }
    .ai-error {
      display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; margin: 8px 0;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-inline-start: 4px solid var(--status-error); border-radius: 4px;
    }
    .ai-error mat-icon { color: var(--status-error); flex-shrink: 0; }
    .proposed { margin: 0 0 4px; color: var(--text-primary); }
    h3 { font-size: 14px; font-weight: 600; margin: 16px 0 6px; color: var(--text-primary); }
    ul { list-style: none; margin: 0; padding: 0; }
    .items li {
      display: flex; gap: 12px; align-items: flex-start; padding: 8px 0;
      border-bottom: 1px solid var(--border-color); color: var(--text-primary);
    }
    .items .kind { color: var(--accent-primary); flex-shrink: 0; }
    .item-title { font-weight: 600; }
    .item-meta { color: var(--text-secondary); font-size: 13px; }
    .item-meta code { font-family: 'Cascadia Mono', Consolas, 'Courier New', monospace; font-size: 12px; }
    .reason { margin: 4px 0 0; font-size: 13px; color: var(--text-primary); max-width: 70ch; }
    .notes li { display: flex; gap: 8px; align-items: flex-start; margin-bottom: 4px; color: var(--text-primary); }
    .notes mat-icon { color: var(--status-warning); flex-shrink: 0; font-size: 20px; width: 20px; height: 20px; }
    .disclaimer { display: flex; align-items: center; gap: 6px; margin: 16px 0 0; color: var(--text-secondary); font-size: 12px; }
    .disclaimer mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `]
})
export class AiReportDraftDialogComponent {
  readonly data: AiReportDraftDialogData = inject(MAT_DIALOG_DATA);
  readonly maxQueries = MAX_QUERIES;

  step: 'pick' | 'working' | 'review' = 'pick';
  search = '';
  goal = '';
  mode: AiDraftMode = 'add';
  /** Kept outside the list, so filtering the list does not drop a choice made earlier. */
  readonly selected = new Set<string>();
  result: AiReportDraft | null = null;
  error = '';

  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialogRef = inject(MatDialogRef<AiReportDraftDialogComponent, AiReportDraftDialogResult>);
  private readonly cdr = inject(ChangeDetectorRef);

  get visibleQueries(): DynamicQuery[] {
    const term = this.search.trim().toLowerCase();
    if (!term) return this.data.queries;
    return this.data.queries.filter(q =>
      q.name.toLowerCase().includes(term) || (q.description ?? '').toLowerCase().includes(term));
  }

  onSearch(event: Event): void {
    this.search = (event.target as HTMLInputElement).value;
  }

  onGoal(event: Event): void {
    this.goal = (event.target as HTMLTextAreaElement).value;
  }

  toggle(id: string, on: boolean): void {
    if (on && this.selected.size < MAX_QUERIES) this.selected.add(id);
    else this.selected.delete(id);
  }

  draft(): void {
    this.step = 'working';
    this.error = '';
    this.cdr.detectChanges();

    // In listing order, so the sections come out in the order the admin sees the queries.
    const queryIds = this.data.queries.map(q => q.id).filter(id => this.selected.has(id));
    this.ai.draftReport({ queryIds, goal: this.goal.trim() || null }).subscribe({
      next: (result) => {
        this.result = result;
        this.step = 'review';
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = err?.name === 'TimeoutError'
          ? this.transloco.translate('admin.ai.timedOut')
          : extractApiError(err, this.transloco.translate('admin.ai.failed'));
        this.step = 'pick';
        this.cdr.detectChanges();
      }
    });
  }

  use(): void {
    if (this.result) this.dialogRef.close({ draft: this.result, mode: this.mode });
  }

  queryName(id: string | null | undefined): string {
    return this.data.queries.find(q => q.id === id)?.name ?? '';
  }

  chartIcon(type: ReportChartType): string {
    return CHART_TYPES[type]?.icon ?? 'insert_chart';
  }

  chartLabel(type: ReportChartType): string {
    return CHART_TYPES[type]?.labelKey ?? 'admin.reports.typeColumn';
  }

  dirOf(text: string): TextDirection {
    return textDirection(text, pageDirection(this.transloco));
  }
}
