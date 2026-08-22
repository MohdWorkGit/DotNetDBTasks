import { PERM } from './permissions';
import { ExportFormatOption } from './export-formats';
import { ParameterType, DropdownSourceType } from './dynamic-query.model';

/**
 * A report composes several saved queries into one document, laid out by a Word template the
 * author uploads. Mirrors the DTOs in Bayan.Application/Features/Reports/Dtos.
 */

/** Mirrors ReportDatasetSourceType. */
export enum ReportDatasetSourceType {
  Query = 0,
  Join = 1,
  Detail = 2
}

/** Mirrors ReportChartType. */
export enum ReportChartType {
  Column = 0,
  Bar = 1,
  Line = 2,
  Pie = 3
}

/** Mirrors ReportJoinType. */
export enum ReportJoinType {
  Inner = 0,
  Left = 1
}

/** Mirrors ReportParameterSourceKind — where a dataset's query parameter gets its value. */
export enum ReportParameterSourceKind {
  ReportParameter = 0,
  Constant = 1,
  ParentColumn = 2
}

export interface ReportSummary {
  id: string;
  name: string;
  description: string;
  isEnabled: boolean;
  datasetCount: number;
  parameterCount: number;
  hasTemplate: boolean;
  templateFileName?: string | null;
  /** The query group this report is filed in — the same folders queries use. */
  queryGroupId?: string | null;
  queryGroupName?: string | null;
  /** Already narrowed by the server to what this user's roles permit. */
  allowedExportFormats: string[];
  createdAt: string;
  updatedAt?: string | null;
}

export interface Report extends ReportSummary {
  timeoutSeconds: number;
  maxDetailRows: number;
  maxTotalRows: number;
  datasets: ReportDataset[];
  parameters: ReportParameter[];
  charts: ReportChart[];
}

/** A chart the template places with a {{CHART:key}} marker. */
export interface ReportChart {
  id: string;
  chartKey: string;
  title?: string | null;
  chartType: ReportChartType;
  /** The dataset it draws from, by key. */
  datasetKey?: string | null;
  categoryColumn: string;
  seriesColumns: string[];
  maxCategories: number;
  sortOrder: number;
}

export interface ReportChartInput {
  chartKey: string;
  title?: string | null;
  chartType: ReportChartType;
  datasetKey: string;
  categoryColumn: string;
  seriesColumns: string[];
  maxCategories: number;
  sortOrder: number;
}

export interface ReportDataset {
  id: string;
  datasetKey: string;
  displayName: string;
  sourceType: ReportDatasetSourceType;
  sortOrder: number;
  isVisibleInViewer: boolean;
  dynamicQueryId?: string | null;
  dynamicQueryName?: string | null;
  /** For a Detail dataset, the key of the dataset it repeats under. */
  parentDatasetKey?: string | null;
  /** For a Join dataset, the two datasets combined and the columns matched on. */
  leftDatasetKey?: string | null;
  rightDatasetKey?: string | null;
  joinType?: ReportJoinType | null;
  leftColumn?: string | null;
  rightColumn?: string | null;
  parameterMaps: ReportParameterMap[];
}

export interface ReportParameterMap {
  id: string;
  targetParameterName: string;
  sourceKind: ReportParameterSourceKind;
  reportParameterId?: string | null;
  constantValue?: string | null;
  parentColumn?: string | null;
}

/** Same shape as QueryParameter, so the run form reuses the query parameter controls. */
export interface ReportParameter {
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

// ---------------------------------------------------------------- save payloads

export interface ReportInput {
  name: string;
  description?: string | null;
  isEnabled: boolean;
  queryGroupId?: string | null;
  timeoutSeconds: number;
  maxDetailRows: number;
  maxTotalRows: number;
  allowedExportFormats: string[];
  datasets: ReportDatasetInput[];
  parameters: ReportParameterInput[];
  charts: ReportChartInput[];
}

export interface ReportDatasetInput {
  datasetKey: string;
  displayName: string;
  sourceType: ReportDatasetSourceType;
  sortOrder: number;
  isVisibleInViewer: boolean;
  dynamicQueryId?: string | null;
  parentDatasetKey?: string | null;
  leftDatasetKey?: string | null;
  rightDatasetKey?: string | null;
  joinType?: ReportJoinType | null;
  leftColumn?: string | null;
  rightColumn?: string | null;
  parameterMaps: ReportParameterMapInput[];
}

export interface ReportParameterMapInput {
  targetParameterName: string;
  sourceKind: ReportParameterSourceKind;
  /** The report parameter's NAME — a parameter can be created and mapped in the same save. */
  reportParameterName?: string | null;
  constantValue?: string | null;
  parentColumn?: string | null;
}

export interface ReportParameterInput {
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

export interface ReportAccess {
  roleIds: string[];
  userGroupIds: string[];
  userIds: string[];
}

// ---------------------------------------------------------------- template

/**
 * What the uploaded template actually references, checked against the report's datasets. The
 * builder renders this so a mistyped key is caught while the author still has the file open,
 * rather than surfacing as a silently empty section later.
 */
export interface ReportTemplateInspection {
  templateFileName?: string | null;
  matchedDatasetKeys: string[];
  unknownDatasetKeys: string[];
  datasetsWithoutMarker: string[];
  duplicateKeysInOneTable: string[];
  hasUnkeyedResultsMarker: boolean;
  hasPlaceholderInsideTextBox: boolean;
}

// ---------------------------------------------------------------- run

export interface ReportRunSection {
  key: string;
  title: string;
  /** The cached-result job this section's rows are paged from; null when the section failed. */
  jobId?: string | null;
  columns: string[];
  totalRows: number;
  error?: string | null;
  isVisibleInViewer: boolean;
}

export interface ReportRun {
  runId: string;
  reportId: string;
  reportName: string;
  executionDurationMs: number;
  /** Non-failures the reader should still know about, e.g. a row budget reached. */
  warnings: string[];
  sections: ReportRunSection[];
  /**
   * Charts already reduced to categories and series by the server — the same numbers the
   * exported document draws, so screen and file cannot disagree.
   */
  charts: ReportRunChart[];
}

export interface ReportRunChartSeries {
  name: string;
  /** One value per category; null is a gap, not a zero. */
  values: (number | null)[];
}

export interface ReportRunChart {
  key: string;
  title: string;
  type: ReportChartType;
  categories: string[];
  series: ReportRunChartSeries[];
}

/**
 * The formats a whole report can be downloaded as, and the capability each needs.
 *
 * <p>Deliberately not the query list: exporting an assembled multi-section document is a
 * separate decision from exporting one query's rows, so it has its own permissions. CSV and
 * JSON are offered because a report still resolves to keyed result sets the exporters can
 * write, though Word and PDF are what a templated report is for.</p>
 */
export const REPORT_EXPORT_FORMATS: readonly ExportFormatOption[] = [
  { name: 'Word', apiValue: 'docx', permission: PERM.reportsExportWord,
    icon: 'article', labelKey: 'user.execute.formatWord' },
  { name: 'Pdf', apiValue: 'pdf', permission: PERM.reportsExportPdf,
    icon: 'picture_as_pdf', labelKey: 'user.execute.formatPdf' },
  { name: 'Excel', apiValue: 'xlsx', permission: PERM.reportsExportExcel,
    icon: 'table_view', labelKey: 'user.execute.formatExcel' },
  { name: 'Csv', apiValue: 'csv', permission: PERM.reportsExportCsv,
    icon: 'description', labelKey: 'user.execute.formatCsv' },
  { name: 'Json', apiValue: 'json', permission: PERM.reportsExportJson,
    icon: 'data_object', labelKey: 'user.execute.formatJson' }
];
