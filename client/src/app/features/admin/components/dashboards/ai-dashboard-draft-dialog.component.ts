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
import { AiDashboardDraft } from '@core/models/ai.model';
import { DynamicQuery } from '@core/models/dynamic-query.model';
import { DashboardVisualType } from '@core/models/dashboard.model';
import { BidiTextComponent, pageDirection, textDirection, TextDirection } from '@shared/components/bidi-text.component';

export interface AiDashboardDraftDialogData {
  /** The read queries the builder offers for tiles. */
  queries: DynamicQuery[];
  /** The builder already has tiles, so ask whether to add to them or replace them. */
  hasTiles: boolean;
  /** The builder's visual-type list, for the icon and name of each proposed tile. */
  visualTypes: { value: DashboardVisualType; icon: string; labelKey: string }[];
}

export type AiDraftMode = 'add' | 'replace';

export interface AiDashboardDraftDialogResult {
  draft: AiDashboardDraft;
  mode: AiDraftMode;
}

/** The most queries one draft takes (DraftDashboardCommand.MaxQueries on the server). */
const MAX_QUERIES = 6;

/**
 * Asks the AI model to propose a dashboard from saved queries: pick the queries and say what the
 * dashboard is for, wait, then review the proposed tiles before anything reaches the form. Using
 * the draft only fills the builder; it is saved, as any dashboard is, with Save.
 */
@Component({
  standalone: true,
  selector: 'app-ai-dashboard-draft-dialog',
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule,
    MatListModule, MatProgressSpinnerModule, MatRadioModule, TranslocoModule, BidiTextComponent
  ],
  template: `
    <h2 mat-dialog-title>{{ 'admin.ai.draftTitle' | transloco }}</h2>
    <mat-dialog-content>
      <!-- ------------------------------------------------------------ pick -->
      <ng-container *ngIf="step === 'pick'">
        <p class="intro">{{ 'admin.ai.draftIntro' | transloco }}</p>

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
          <mat-label>{{ 'admin.ai.draftGoal' | transloco }}</mat-label>
          <textarea matInput rows="2" maxlength="500" [value]="goal" (input)="onGoal($event)" dir="auto"></textarea>
          <mat-hint>{{ 'admin.ai.draftGoalHint' | transloco }}</mat-hint>
        </mat-form-field>

        <mat-radio-group *ngIf="data.hasTiles" class="mode" [value]="mode" (change)="mode = $event.value">
          <mat-radio-button value="add">{{ 'admin.ai.draftModeAdd' | transloco }}</mat-radio-button>
          <mat-radio-button value="replace">{{ 'admin.ai.draftModeReplace' | transloco }}</mat-radio-button>
        </mat-radio-group>

        <div *ngIf="error" class="ai-error" role="alert">
          <mat-icon>error_outline</mat-icon><span>{{ error }}</span>
        </div>
      </ng-container>

      <!-- ------------------------------------------------------------ working -->
      <div *ngIf="step === 'working'" class="working" role="status">
        <mat-spinner diameter="32"></mat-spinner>
        <span>{{ 'admin.ai.draftWorking' | transloco }}</span>
      </div>

      <!-- ------------------------------------------------------------ review -->
      <ng-container *ngIf="step === 'review' && result">
        <p class="proposed">
          {{ 'admin.ai.draftProposed' | transloco }}
          <strong><app-bidi-text [text]="result.draft.name"></app-bidi-text></strong>
        </p>
        <p *ngIf="result.draft.description" class="caption" [attr.dir]="dirOf(result.draft.description)">
          <app-bidi-text [text]="result.draft.description"></app-bidi-text>
        </p>

        <ul class="tiles">
          <li *ngFor="let t of result.draft.tiles; let i = index">
            <mat-icon class="visual" [attr.aria-label]="visualLabel(t.visualType) | transloco">{{ visualIcon(t.visualType) }}</mat-icon>
            <div>
              <div class="tile-title"><app-bidi-text [text]="t.title"></app-bidi-text></div>
              <div class="tile-meta">
                {{ visualLabel(t.visualType) | transloco }} ·
                <app-bidi-text [text]="queryName(t.dynamicQueryId)"></app-bidi-text> ·
                {{ 'admin.ai.draftWidth' | transloco: { width: t.width } }}
              </div>
              <p *ngIf="result.reasons[i]" class="reason" [attr.dir]="dirOf(result.reasons[i])">
                <app-bidi-text [text]="result.reasons[i]"></app-bidi-text>
              </p>
            </div>
          </li>
        </ul>

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
    p[dir], li[dir] { text-align: start; }
    .intro, .caption { color: var(--text-secondary); margin: 0 0 12px; max-width: 70ch; }
    .caption { font-size: 13px; }
    .full-width { width: 100%; }
    .queries {
      max-height: 280px; overflow: auto; margin-bottom: 16px;
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
    ul { list-style: none; margin: 0; padding: 0; }
    .tiles li {
      display: flex; gap: 12px; align-items: flex-start; padding: 10px 0;
      border-bottom: 1px solid var(--border-color); color: var(--text-primary);
    }
    .tiles .visual { color: var(--accent-primary); flex-shrink: 0; }
    .tile-title { font-weight: 600; }
    .tile-meta { color: var(--text-secondary); font-size: 13px; }
    .reason { margin: 4px 0 0; font-size: 13px; color: var(--text-primary); max-width: 70ch; }
    h3 { font-size: 14px; font-weight: 600; margin: 16px 0 6px; color: var(--text-primary); }
    .notes li { display: flex; gap: 8px; align-items: flex-start; margin-bottom: 4px; color: var(--text-primary); }
    .notes mat-icon { color: var(--status-warning); flex-shrink: 0; font-size: 20px; width: 20px; height: 20px; }
    .disclaimer { display: flex; align-items: center; gap: 6px; margin: 16px 0 0; color: var(--text-secondary); font-size: 12px; }
    .disclaimer mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `]
})
export class AiDashboardDraftDialogComponent {
  readonly data: AiDashboardDraftDialogData = inject(MAT_DIALOG_DATA);
  readonly maxQueries = MAX_QUERIES;

  step: 'pick' | 'working' | 'review' = 'pick';
  search = '';
  goal = '';
  mode: AiDraftMode = 'add';
  /** Kept outside the list, so filtering the list does not drop a choice made earlier. */
  readonly selected = new Set<string>();
  result: AiDashboardDraft | null = null;
  error = '';

  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialogRef = inject(MatDialogRef<AiDashboardDraftDialogComponent, AiDashboardDraftDialogResult>);
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

    // In the order the queries are listed, so the model numbers them as the admin sees them.
    const queryIds = this.data.queries.map(q => q.id).filter(id => this.selected.has(id));
    this.ai.draftDashboard({ queryIds, goal: this.goal.trim() || null }).subscribe({
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

  queryName(id: string): string {
    return this.data.queries.find(q => q.id === id)?.name ?? '';
  }

  visualIcon(type: DashboardVisualType): string {
    return this.data.visualTypes.find(v => v.value === type)?.icon ?? 'widgets';
  }

  visualLabel(type: DashboardVisualType): string {
    return this.data.visualTypes.find(v => v.value === type)?.labelKey ?? '';
  }

  dirOf(text: string): TextDirection {
    return textDirection(text, pageDirection(this.transloco));
  }
}
