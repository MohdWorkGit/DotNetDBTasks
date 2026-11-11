import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AiService } from '@core/services/ai.service';
import { extractApiError } from '@core/services/toast.service';
import { AiSqlExplanation, ExplainSqlRequest } from '@core/models/ai.model';
import { BidiTextComponent, pageDirection, textDirection, TextDirection } from '@shared/components/bidi-text.component';

/**
 * Asks the AI model what the SQL being edited does, and shows the answer in the page's
 * language. Read-only: nothing runs against the database, and the form is left as it was.
 */
@Component({
  standalone: true,
  selector: 'app-ai-explain-dialog',
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressSpinnerModule, TranslocoModule,
    BidiTextComponent
  ],
  template: `
    <h2 mat-dialog-title>{{ 'admin.ai.explainTitle' | transloco }}</h2>
    <mat-dialog-content>
      <div *ngIf="working" class="working" role="status">
        <mat-spinner diameter="32"></mat-spinner>
        <span>{{ 'admin.ai.working' | transloco }}</span>
      </div>

      <div *ngIf="error" class="ai-error" role="alert">
        <mat-icon>error_outline</mat-icon>
        <span>{{ error }}</span>
      </div>

      <ng-container *ngIf="answer && !working">
        <p class="summary" [attr.dir]="dirOf(answer.summary)"><app-bidi-text [text]="answer.summary"></app-bidi-text></p>

        <ng-container *ngIf="answer.steps.length">
          <h3>{{ 'admin.ai.steps' | transloco }}</h3>
          <ol><li *ngFor="let step of answer.steps" [attr.dir]="dirOf(step)"><app-bidi-text [text]="step"></app-bidi-text></li></ol>
        </ng-container>

        <ng-container *ngIf="answer.cautions.length">
          <h3>{{ 'admin.ai.cautions' | transloco }}</h3>
          <ul class="cautions">
            <li *ngFor="let caution of answer.cautions" [attr.dir]="dirOf(caution)">
              <mat-icon>warning_amber</mat-icon><app-bidi-text [text]="caution"></app-bidi-text>
            </li>
          </ul>
        </ng-container>

        <p class="disclaimer">
          <mat-icon>auto_awesome</mat-icon>{{ 'admin.ai.disclaimer' | transloco }}
        </p>
      </ng-container>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button *ngIf="error" mat-stroked-button type="button" (click)="ask()">
        <mat-icon>refresh</mat-icon> {{ 'admin.ai.tryAgain' | transloco }}
      </button>
      <button mat-button mat-dialog-close type="button">{{ 'common.close' | transloco }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Material's RTL dialog sets text-align: right outright, and an English line inherits it. */
    p[dir], li[dir] { text-align: start; }
    /* and a narrow paragraph sits on its own reading side, not the page's. */
    p[dir] { margin-inline-end: auto; }
    .working { display: flex; align-items: center; gap: 16px; padding: 16px 0; color: var(--text-secondary); }
    .ai-error {
      display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; margin: 8px 0;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-inline-start: 4px solid var(--status-error); border-radius: 4px;
    }
    .ai-error mat-icon { color: var(--status-error); flex-shrink: 0; }
    .summary { font-size: 15px; line-height: 1.6; margin: 0 0 12px; max-width: 70ch; color: var(--text-primary); }
    h3 { font-size: 14px; font-weight: 600; margin: 16px 0 6px; color: var(--text-primary); }
    ol, ul { margin: 0; padding-inline-start: 22px; line-height: 1.6; max-width: 70ch; color: var(--text-primary); }
    ul.cautions { list-style: none; padding-inline-start: 0; }
    ul.cautions li { display: flex; gap: 8px; align-items: flex-start; margin-bottom: 4px; }
    ul.cautions mat-icon { color: var(--status-warning); flex-shrink: 0; font-size: 20px; width: 20px; height: 20px; }
    .disclaimer {
      display: flex; align-items: center; gap: 6px; margin: 20px 0 0;
      color: var(--text-secondary); font-size: 12px;
    }
    .disclaimer mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `]
})
export class AiExplainDialogComponent implements OnInit {
  working = false;
  answer: AiSqlExplanation | null = null;
  error = '';

  private readonly data: ExplainSqlRequest = inject(MAT_DIALOG_DATA);
  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  private readonly cdr = inject(ChangeDetectorRef);

  ngOnInit(): void {
    this.ask();
  }

  /** The model mixes Arabic with SQL names; each line reads in the language that carries it. */
  dirOf(text: string): TextDirection {
    return textDirection(text, pageDirection(this.transloco));
  }

  ask(): void {
    this.working = true;
    this.error = '';
    this.answer = null;
    this.cdr.detectChanges();

    this.ai.explainSql(this.data).subscribe({
      next: (answer) => {
        this.answer = answer;
        this.working = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.error = err?.name === 'TimeoutError'
          ? this.transloco.translate('admin.ai.timedOut')
          : extractApiError(err, this.transloco.translate('admin.ai.failed'));
        this.working = false;
        this.cdr.detectChanges();
      }
    });
  }
}
