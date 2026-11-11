import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatListModule } from '@angular/material/list';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslocoModule, TranslocoService } from '@jsverse/transloco';
import { AiService } from '@core/services/ai.service';
import { extractApiError } from '@core/services/toast.service';
import { AiConnection, AiSchemaObject, AiWrittenSql } from '@core/models/ai.model';
import { BidiTextComponent, pageDirection, textDirection, TextDirection } from '@shared/components/bidi-text.component';

export interface AiWriteSqlDialogData {
  /** The connection the form has chosen, offered first when the AI may read it. */
  databaseUserId: string | null;
}

export interface AiWriteSqlDialogResult {
  written: AiWrittenSql;
  /** The connection the SQL was written for; the form switches to it. */
  databaseUserId: string | null;
}

/** The most tables one request takes (WriteSqlCommand.MaxObjects on the server). */
const MAX_OBJECTS = 10;
const DEFAULT_KEY = '__default__';

const PARAMETER_TYPE_KEYS = [
  'admin.queryForm.typeString', 'admin.queryForm.typeNumber', 'admin.queryForm.typeDate', 'admin.queryForm.typeBoolean'
];

/**
 * Asks the AI model to write a SELECT from a description: pick a connection the AI may read,
 * pick the tables, describe what the query should return, then review the SQL before it goes
 * into the editor. Nothing is saved or run here; the admin tests it there.
 */
@Component({
  standalone: true,
  selector: 'app-ai-write-sql-dialog',
  imports: [
    CommonModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatIconModule, MatInputModule, MatListModule,
    MatProgressSpinnerModule, MatSelectModule, MatTooltipModule, TranslocoModule, BidiTextComponent
  ],
  template: `
    <h2 mat-dialog-title>{{ 'admin.ai.writeTitle' | transloco }}</h2>
    <mat-dialog-content>
      <div *ngIf="loadingConnections" class="working" role="status"><mat-spinner diameter="28"></mat-spinner></div>

      <div *ngIf="!loadingConnections && !connections.length" class="ai-note" role="status">
        <mat-icon>info</mat-icon><span>{{ 'admin.ai.writeNoConnections' | transloco }}</span>
      </div>

      <!-- ------------------------------------------------------------ pick -->
      <ng-container *ngIf="step === 'pick' && connections.length">
        <mat-form-field appearance="outline" class="full-width">
          <mat-label>{{ 'admin.ai.writeConnection' | transloco }}</mat-label>
          <mat-select [value]="connectionKey" (selectionChange)="onConnection($event.value)">
            <mat-option *ngFor="let c of connections" [value]="keyOf(c)">
              {{ c.isDefault ? ('admin.queryForm.defaultConnection' | transloco) : c.name }}
            </mat-option>
          </mat-select>
        </mat-form-field>

        <div class="tables-head">
          <mat-form-field appearance="outline" class="search">
            <mat-label>{{ 'admin.ai.writeSearch' | transloco }}</mat-label>
            <mat-icon matPrefix>search</mat-icon>
            <input matInput [value]="search" (input)="onSearch($event)" dir="ltr">
          </mat-form-field>
          <button mat-icon-button type="button" (click)="loadSchema(true)" [disabled]="loadingSchema"
                  [matTooltip]="'admin.ai.writeRefresh' | transloco" [attr.aria-label]="'admin.ai.writeRefresh' | transloco">
            <mat-icon>refresh</mat-icon>
          </button>
        </div>

        <p class="caption" role="status">
          {{ 'admin.ai.writePicked' | transloco: { count: selected.size, max: maxObjects } }}
        </p>
        <div *ngIf="loadingSchema" class="working"><mat-spinner diameter="24"></mat-spinner></div>
        <mat-selection-list *ngIf="!loadingSchema" class="tables" [attr.aria-label]="'admin.ai.writeTables' | transloco">
          <mat-list-option *ngFor="let o of visibleObjects" togglePosition="before"
                           [selected]="selected.has(o.qualifiedName)"
                           [disabled]="!selected.has(o.qualifiedName) && selected.size >= maxObjects"
                           (selectedChange)="toggle(o.qualifiedName, $event)">
            <span matListItemTitle class="object-name" dir="ltr">{{ o.qualifiedName }}</span>
            <span matListItemLine *ngIf="o.kind === 'view'">{{ 'admin.ai.writeView' | transloco }}</span>
          </mat-list-option>
        </mat-selection-list>
        <p *ngIf="!loadingSchema && !visibleObjects.length" class="caption">{{ 'admin.ai.writeNoTables' | transloco }}</p>

        <mat-form-field appearance="outline" class="full-width description">
          <mat-label>{{ 'admin.ai.writeDescription' | transloco }}</mat-label>
          <textarea matInput rows="3" maxlength="1000" [value]="description" (input)="onDescription($event)" dir="auto"></textarea>
          <mat-hint>{{ 'admin.ai.writeDescriptionHint' | transloco }}</mat-hint>
        </mat-form-field>

        <p class="caption sent">
          <mat-icon>{{ sampleValues ? 'visibility' : 'lock' }}</mat-icon>
          {{ (sampleValues ? 'admin.ai.writeSentSamples' : 'admin.ai.writeSentStructure') | transloco }}
        </p>

        <div *ngIf="error" class="ai-error" role="alert">
          <mat-icon>error_outline</mat-icon><span [attr.dir]="dirOf(error)">{{ error }}</span>
        </div>
      </ng-container>

      <!-- ------------------------------------------------------------ working -->
      <div *ngIf="step === 'working'" class="working" role="status">
        <mat-spinner diameter="32"></mat-spinner>
        <span>{{ 'admin.ai.writeWorking' | transloco }}</span>
      </div>

      <!-- ------------------------------------------------------------ review -->
      <ng-container *ngIf="step === 'review' && result">
        <p *ngIf="result.explanation" class="explanation" [attr.dir]="dirOf(result.explanation)">
          <app-bidi-text [text]="result.explanation"></app-bidi-text>
        </p>
        <pre class="sql" dir="ltr">{{ result.sql }}</pre>

        <ng-container *ngIf="result.parameters.length">
          <h3>{{ 'admin.ai.writeParameters' | transloco }}</h3>
          <ul class="parameters">
            <li *ngFor="let p of result.parameters">
              <code dir="ltr">&#64;{{ p.name }}</code>
              <span><app-bidi-text [text]="p.displayName"></app-bidi-text></span>
              <span class="type">{{ typeKey(p.parameterType) | transloco }}</span>
            </li>
          </ul>
        </ng-container>

        <p class="disclaimer"><mat-icon>auto_awesome</mat-icon>{{ 'admin.ai.writeTestFirst' | transloco }}</p>
      </ng-container>
    </mat-dialog-content>

    <mat-dialog-actions align="end">
      <button *ngIf="step === 'review'" mat-button type="button" (click)="step = 'pick'">
        {{ 'admin.ai.draftBack' | transloco }}
      </button>
      <button *ngIf="step === 'review'" mat-stroked-button type="button" (click)="write()">
        <mat-icon>refresh</mat-icon> {{ 'admin.ai.tryAgain' | transloco }}
      </button>
      <button mat-button mat-dialog-close type="button">{{ 'common.cancel' | transloco }}</button>
      <button *ngIf="step === 'pick' && connections.length" mat-raised-button color="primary" type="button"
              [disabled]="!selected.size || !description.trim()" (click)="write()">
        <mat-icon>auto_awesome</mat-icon> {{ 'admin.ai.writeRun' | transloco }}
      </button>
      <button *ngIf="step === 'review'" mat-raised-button color="primary" type="button" (click)="use()">
        <mat-icon>check</mat-icon> {{ 'admin.ai.useSql' | transloco }}
      </button>
    </mat-dialog-actions>
  `,
  styles: [`
    /* Material's RTL dialog sets text-align: right outright, and an English line inherits it. */
    p[dir], span[dir] { text-align: start; }
    p[dir] { margin-inline-end: auto; }
    .full-width { width: 100%; }
    .working { display: flex; align-items: center; gap: 16px; padding: 16px 0; color: var(--text-secondary); }
    .tables-head { display: flex; align-items: flex-start; gap: 8px; }
    .tables-head .search { flex: 1; }
    .tables-head button { margin-top: 8px; }
    .caption { color: var(--text-secondary); font-size: 13px; margin: 0 0 8px; max-width: 70ch; }
    .caption.sent { display: flex; align-items: center; gap: 6px; margin-top: 4px; }
    .caption.sent mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .tables {
      max-height: 240px; overflow: auto; margin-bottom: 16px;
      border: 1px solid var(--border-color); border-radius: 4px;
    }
    .object-name { font-family: 'Cascadia Mono', Consolas, 'Courier New', monospace; font-size: 13px; }
    .description { margin-top: 4px; }
    .ai-note, .ai-error {
      display: flex; gap: 12px; align-items: flex-start; padding: 12px 16px; margin: 8px 0;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-radius: 4px;
    }
    .ai-note { border-inline-start: 4px solid var(--status-info); }
    .ai-note mat-icon { color: var(--status-info); flex-shrink: 0; }
    .ai-error { border-inline-start: 4px solid var(--status-error); }
    .ai-error mat-icon { color: var(--status-error); flex-shrink: 0; }
    .explanation { margin: 0 0 12px; max-width: 70ch; color: var(--text-primary); line-height: 1.6; }
    .sql {
      margin: 0 0 12px; padding: 10px 12px; max-height: 280px; overflow: auto; white-space: pre-wrap;
      font-family: 'Cascadia Mono', Consolas, 'Courier New', monospace; font-size: 13px; text-align: left;
      background: var(--bg-surface); color: var(--text-primary);
      border: 1px solid var(--border-color); border-radius: 4px;
    }
    h3 { font-size: 14px; font-weight: 600; margin: 12px 0 6px; color: var(--text-primary); }
    .parameters { list-style: none; margin: 0; padding: 0; }
    .parameters li { display: flex; gap: 12px; align-items: baseline; padding: 4px 0; color: var(--text-primary); }
    .parameters code { font-family: 'Cascadia Mono', Consolas, 'Courier New', monospace; font-size: 13px; }
    .parameters .type { color: var(--text-secondary); font-size: 13px; }
    .disclaimer { display: flex; align-items: center; gap: 6px; margin: 16px 0 0; color: var(--text-secondary); font-size: 12px; }
    .disclaimer mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `]
})
export class AiWriteSqlDialogComponent implements OnInit {
  readonly maxObjects = MAX_OBJECTS;

  step: 'pick' | 'working' | 'review' = 'pick';
  connections: AiConnection[] = [];
  connectionKey = DEFAULT_KEY;
  objects: AiSchemaObject[] = [];
  /** Kept outside the list, so filtering the list does not drop a choice made earlier. */
  readonly selected = new Set<string>();
  search = '';
  description = '';
  sampleValues = false;
  loadingConnections = true;
  loadingSchema = false;
  result: AiWrittenSql | null = null;
  error = '';

  private readonly data: AiWriteSqlDialogData = inject(MAT_DIALOG_DATA);
  private readonly ai = inject(AiService);
  private readonly transloco = inject(TranslocoService);
  private readonly dialogRef = inject(MatDialogRef<AiWriteSqlDialogComponent, AiWriteSqlDialogResult>);
  private readonly cdr = inject(ChangeDetectorRef);

  ngOnInit(): void {
    this.ai.getConnections().subscribe({
      next: (connections) => {
        this.connections = connections;
        this.loadingConnections = false;
        const wanted = connections.find(c => (c.id ?? null) === (this.data.databaseUserId ?? null)) ?? connections[0];
        if (wanted) {
          this.connectionKey = this.keyOf(wanted);
          this.loadSchema(false);
        }
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.loadingConnections = false;
        this.error = extractApiError(err, this.transloco.translate('admin.ai.failed'));
        this.cdr.detectChanges();
      }
    });
  }

  get visibleObjects(): AiSchemaObject[] {
    const term = this.search.trim().toLowerCase();
    return term ? this.objects.filter(o => o.qualifiedName.toLowerCase().includes(term)) : this.objects;
  }

  keyOf(connection: AiConnection): string {
    return connection.id ?? DEFAULT_KEY;
  }

  onConnection(key: string): void {
    this.connectionKey = key;
    this.selected.clear();
    this.loadSchema(false);
  }

  loadSchema(refresh: boolean): void {
    this.loadingSchema = true;
    this.error = '';
    this.cdr.detectChanges();
    this.ai.getSchema(this.connectionId, refresh).subscribe({
      next: (schema) => {
        this.objects = schema.objects;
        this.sampleValues = schema.sampleValues;
        // A refresh can drop a table; a choice of one that is gone would be refused.
        for (const name of [...this.selected]) {
          if (!schema.objects.some(o => o.qualifiedName === name)) this.selected.delete(name);
        }
        this.loadingSchema = false;
        this.cdr.detectChanges();
      },
      error: (err) => {
        this.objects = [];
        this.loadingSchema = false;
        this.error = extractApiError(err, this.transloco.translate('admin.ai.failed'));
        this.cdr.detectChanges();
      }
    });
  }

  onSearch(event: Event): void {
    this.search = (event.target as HTMLInputElement).value;
  }

  onDescription(event: Event): void {
    this.description = (event.target as HTMLTextAreaElement).value;
  }

  toggle(name: string, on: boolean): void {
    if (on && this.selected.size < MAX_OBJECTS) this.selected.add(name);
    else this.selected.delete(name);
  }

  write(): void {
    this.step = 'working';
    this.error = '';
    this.cdr.detectChanges();

    // In listing order, so the model sees the tables as the admin did.
    const objects = this.objects.map(o => o.qualifiedName).filter(n => this.selected.has(n));
    this.ai.writeSql({ databaseUserId: this.connectionId, description: this.description.trim(), objects }).subscribe({
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
    if (this.result) this.dialogRef.close({ written: this.result, databaseUserId: this.connectionId });
  }

  typeKey(type: number): string {
    return PARAMETER_TYPE_KEYS[type] ?? PARAMETER_TYPE_KEYS[0];
  }

  dirOf(text: string): TextDirection {
    return textDirection(text, pageDirection(this.transloco));
  }

  private get connectionId(): string | null {
    return this.connectionKey === DEFAULT_KEY ? null : this.connectionKey;
  }
}
