import { ParameterType, DropdownSourceType } from './dynamic-query.model';
import { ReportRunChart } from './report.model';

/**
 * A dashboard is a page of tiles, each fed by one saved query and refreshed on its own
 * interval. Mirrors the DTOs in Bayan.Application/Features/Dashboards/Dtos.
 */

/** Mirrors DashboardVisualType. */
export enum DashboardVisualType {
  Kpi = 0,
  Column = 1,
  Bar = 2,
  Line = 3,
  Pie = 4,
  Table = 5
}

/** Mirrors DashboardDrillAction — what clicking a category on a tile does. */
export enum DashboardDrillAction {
  None = 0,
  FilterDashboard = 1,
  OpenReport = 2,
  ShowRows = 3
}

/** Mirrors DashboardValueFormat. */
export enum DashboardValueFormat {
  Number = 0,
  Percent = 1,
  Currency = 2
}

/** Mirrors DashboardKpiAggregate — how a KPI reduces its rows to one number. */
export enum DashboardKpiAggregate {
  /** The rows are a series; the headline is the last row. */
  Last = 0,
  Sum = 1,
  Average = 2,
  Count = 3,
  Min = 4,
  Max = 5
}

export type ConditionalOperator = 'gt' | 'gte' | 'lt' | 'lte' | 'eq' | 'neq' | 'contains';
export type ConditionalTone = 'good' | 'warn' | 'bad';

/** A colour rule on a table cell or a KPI's value. The first matching rule wins. */
export interface DashboardConditionalRule {
  /** The column tested; empty on a KPI means the KPI's own value. */
  column?: string | null;
  operator: ConditionalOperator;
  value: string;
  tone: ConditionalTone;
}

/** Mirrors DashboardTileParameterSource. */
export enum DashboardTileParameterSource {
  Filter = 0,
  Constant = 1
}

export interface DashboardSummary {
  id: string;
  name: string;
  description: string;
  isEnabled: boolean;
  defaultRefreshSeconds: number;
  sortOrder: number;
  tileCount: number;
  filterCount: number;
  createdAt: string;
  updatedAt?: string | null;
}

export interface Dashboard extends DashboardSummary {
  tiles: DashboardTile[];
  filters: DashboardFilter[];
}

export interface DashboardTileParameterMap {
  targetParameterName: string;
  sourceKind: DashboardTileParameterSource;
  filterName?: string | null;
  constantValue?: string | null;
}

export interface DashboardTile {
  id: string;
  title: string;
  dynamicQueryId: string;
  dynamicQueryName?: string | null;
  sortOrder: number;
  /** Columns spanned on the 12-column grid: 3, 4, 6, 8 or 12. */
  width: number;
  /** Grid rows spanned, 1 to 3. */
  height: number;
  visualType: DashboardVisualType;
  categoryColumn?: string | null;
  seriesColumns: string[];
  maxCategories: number;
  valueColumn?: string | null;
  compareColumn?: string | null;
  kpiAggregate: DashboardKpiAggregate;
  valueFormat: DashboardValueFormat;
  higherIsBetter: boolean;
  targetValue?: number | null;
  /** Within this percent of the target, on the wrong side, counts as "near". */
  targetWarnPercent: number;
  conditionalRules: DashboardConditionalRule[];
  /** The tile's own interval; null inherits the dashboard's. */
  refreshSeconds?: number | null;
  /** What the viewer polls at. */
  effectiveRefreshSeconds: number;
  drillAction: DashboardDrillAction;
  drillFilterName?: string | null;
  drillReportId?: string | null;
  drillReportParameter?: string | null;
  parameterMaps: DashboardTileParameterMap[];
  /** The formats the tile's query may be exported as, before the caller's own permissions. */
  allowedExportFormats: string[];
}

/** Mirrors the report parameter shape, so the same controls render it. */
export interface DashboardFilter {
  id: string;
  name: string;
  displayName: string;
  parameterType: ParameterType;
  isRequired: boolean;
  defaultValue?: string | null;
  sortOrder: number;
  allowMultiple: boolean;
  dropdownSourceType?: DropdownSourceType | null;
  dropdownStaticValues?: string | null;
  dropdownQueryId?: string | null;
  dropdownQueryValueColumn?: string | null;
  dropdownQueryLabelColumn?: string | null;
}

export interface DashboardAccess {
  roleIds: string[];
  userGroupIds: string[];
  userIds: string[];
}

// ---------------------------------------------------------------- save

export type DashboardFilterInput = Omit<DashboardFilter, 'id'>;

export type DashboardTileInput =
  Omit<DashboardTile, 'id' | 'dynamicQueryName' | 'effectiveRefreshSeconds' | 'allowedExportFormats'>;

export interface DashboardInput {
  name: string;
  description?: string | null;
  isEnabled: boolean;
  defaultRefreshSeconds: number;
  sortOrder: number;
  tiles: DashboardTileInput[];
  filters: DashboardFilterInput[];
}

// ---------------------------------------------------------------- tile data

export interface DashboardKpiData {
  value: number | null;
  compare: number | null;
  deltaPercent: number | null;
  sparkline: (number | null)[];
  target?: number | null;
  targetStatus?: 'met' | 'near' | 'missed' | null;
}

export interface DashboardTableData {
  columns: string[];
  rows: Record<string, unknown>[];
  totalRows: number;
}

export interface DashboardTileData {
  tileId: string;
  /** When the query actually ran — older than now when served from the shared cache. */
  generatedAt: string;
  /** When a fresh result will be available; the next poll is scheduled from this. */
  nextRefreshAt: string;
  chart?: ReportRunChart | null;
  kpi?: DashboardKpiData | null;
  table?: DashboardTableData | null;
  isEmpty: boolean;
  error?: string | null;
}

export interface DashboardTileRows {
  jobId: string;
  title: string;
  columns: string[];
  totalRows: number;
}

/** Refresh bounds the server enforces. */
export const DASHBOARD_MIN_REFRESH_SECONDS = 30;
export const DASHBOARD_MAX_REFRESH_SECONDS = 3600;

/** The widths a tile may take on the 12-column grid. */
export const DASHBOARD_TILE_WIDTHS = [3, 4, 6, 8, 12] as const;

export interface DashboardColumnsProbe {
  columns: string[];
  error?: string | null;
}

/**
 * The first rule whose condition holds for this value, or null. Numbers compare numerically when
 * both sides are numbers, otherwise as text — so "status = Late" works as well as "total < 100".
 */
export function matchRule(
  rules: DashboardConditionalRule[], column: string | null, value: unknown
): DashboardConditionalRule | null {
  for (const rule of rules) {
    if ((rule.column ?? '') !== (column ?? '')) continue;
    if (value === null || value === undefined) continue;

    const text = String(value);
    const a = Number(text);
    const b = Number(rule.value);
    const numeric = text.trim() !== '' && rule.value.trim() !== '' && !isNaN(a) && !isNaN(b);
    const cmp = numeric ? Math.sign(a - b) : text.localeCompare(rule.value);

    const hit = rule.operator === 'contains'
      ? text.toLowerCase().includes(rule.value.toLowerCase())
      : rule.operator === 'gt' ? cmp > 0
      : rule.operator === 'gte' ? cmp >= 0
      : rule.operator === 'lt' ? cmp < 0
      : rule.operator === 'lte' ? cmp <= 0
      : rule.operator === 'eq' ? cmp === 0
      : cmp !== 0;

    if (hit) return rule;
  }
  return null;
}
