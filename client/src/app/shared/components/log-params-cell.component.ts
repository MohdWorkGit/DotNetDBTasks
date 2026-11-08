import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { MatTooltipModule } from '@angular/material/tooltip';
import { LogParameter, parseLogParameters } from './log-parameters';

/**
 * The parameters of one execution log, compact enough for a table cell: the first few as
 * `name = value` chips, then "+N more". The full list is in the tooltip and in the log's
 * details dialog. Shown for every statement type — a DELETE's parameters are exactly what an
 * auditor needs to see which rows it targeted.
 */
@Component({
  standalone: true,
  selector: 'app-log-params-cell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslocoModule, MatTooltipModule],
  template: `
    <span *ngIf="items.length === 0" class="none">{{ 'admin.logs.noParameters' | transloco }}</span>
    <span *ngIf="items.length > 0" class="chips" [matTooltip]="tooltip"
          matTooltipClass="hint-tooltip">
      <span *ngFor="let p of shown" class="param-chip">
        <span class="param-name">{{ p.name }}</span>
        <span class="param-eq">=</span>
        <span class="param-value" [class.empty]="p.isEmpty">
          {{ p.isEmpty ? ('admin.logs.emptyValue' | transloco) : p.values.join(', ') }}
        </span>
      </span>
      <span *ngIf="hidden > 0" class="more">{{ 'admin.logs.moreParameters' | transloco: { count: hidden } }}</span>
    </span>
  `,
  styles: [`
    :host { display: block; max-width: 320px; }
    .none { color: var(--text-hint); font-size: 12px; }
    .chips { display: flex; flex-wrap: wrap; gap: 4px; cursor: default; }
    .param-chip {
      display: inline-flex;
      align-items: baseline;
      gap: 3px;
      max-width: 100%;
      padding: 1px 8px;
      border: 1px solid var(--border-color);
      border-radius: 10px;
      background: var(--bg-secondary);
      font-size: 12px;
      line-height: 18px;
      min-width: 0;
    }
    .param-name { color: var(--sql-param); font-weight: 600; white-space: nowrap; }
    .param-eq { color: var(--text-secondary); }
    .param-value {
      color: var(--text-primary);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      max-width: 160px;
    }
    .param-value.empty { color: var(--text-hint); font-style: italic; }
    .more { font-size: 12px; color: var(--text-secondary); align-self: center; }
  `]
})
export class LogParamsCellComponent {
  /** How many chips fit in a cell before the rest collapse into "+N more". */
  private static readonly MaxShown = 3;

  items: LogParameter[] = [];
  shown: LogParameter[] = [];
  hidden = 0;
  tooltip = '';

  @Input() set params(value: Record<string, string> | null | undefined) {
    this.items = parseLogParameters(value);
    this.shown = this.items.slice(0, LogParamsCellComponent.MaxShown);
    this.hidden = this.items.length - this.shown.length;
    this.tooltip = this.items
      .map(p => `${p.name} = ${p.isEmpty ? '—' : p.values.join(', ')}`)
      .join('\n');
  }
}
