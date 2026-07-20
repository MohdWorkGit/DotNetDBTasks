// Models for scheduled export tasks (admin-managed recurring query exports).

export enum ScheduleFrequency {
  EveryNMinutes = 0,
  Daily = 1,
  Weekly = 2,
  Monthly = 3
}

export enum ExportFileFormat {
  Excel = 0,
  Csv = 1,
  Json = 2,
  Pdf = 3,
  Word = 4
}

export const EXPORT_FORMAT_LABELS: Record<ExportFileFormat, string> = {
  [ExportFileFormat.Excel]: 'Excel (.xlsx)',
  [ExportFileFormat.Csv]: 'CSV (.csv)',
  [ExportFileFormat.Json]: 'JSON (.json)',
  [ExportFileFormat.Pdf]: 'PDF (.pdf)',
  [ExportFileFormat.Word]: 'Word (.docx)'
};

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

/** One recurrence rule of a task; the task fires on the earliest upcoming occurrence across all of its triggers. */
export interface ScheduleTrigger {
  frequency: ScheduleFrequency;
  intervalMinutes?: number | null;
  timeOfDay?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  sortOrder: number;
}

export interface ScheduledTaskItem {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  parameters: Record<string, string>;
  exportFormat: ExportFileFormat;
  /** CSV only: field separator text (null = comma), e.g. ";" or ";;". */
  csvSeparator?: string | null;
  fileNamePrefix?: string | null;
  appendTimestamp: boolean;
  sortOrder: number;
  /** True when the query modifies data — it commits on run and exports no file. */
  isWriteQuery: boolean;
  keyColumn?: string | null;
  keyParameter?: string | null;
  initialKey?: string | null;
  /** Saved checkpoint of the last successful run (read-only). */
  lastKeyValue?: string | null;
}

export interface ScheduledTaskViewer {
  userId: string;
  username: string;
  /** When true the viewer may also download the run's export files. */
  canDownloadFiles: boolean;
}

export interface ScheduledTaskRunItem {
  queryName: string;
  /** Null for write queries — they produce no file. */
  fileName?: string | null;
  success: boolean;
  /** Rows exported (read query) or rows affected (write query). */
  rowCount: number;
  isWrite?: boolean;
  /** New checkpoint saved by this run, for incremental items. */
  lastKey?: string | null;
  error?: string | null;
  durationMs: number;
}

export interface ScheduledTaskRun {
  id: string;
  startedAt: string;
  completedAt?: string | null;
  status: 'Running' | 'Succeeded' | 'PartiallySucceeded' | 'Failed';
  triggeredByUsername?: string | null;
  error?: string | null;
  items: ScheduledTaskRunItem[];
}

export interface ScheduledTask {
  id: string;
  name: string;
  description: string;
  isEnabled: boolean;
  outputFolder: string;
  /** Optional second folder that receives a copy of every export file. */
  archiveFolder?: string | null;
  /** When true, all read-query results are appended into one output file in item order. */
  combineOutput: boolean;
  /** When false, CSV/Excel exports contain data rows only (no header row). */
  includeHeaders: boolean;
  combinedFileName?: string | null;
  combinedFormat: ExportFileFormat;
  combinedCsvSeparator?: string | null;
  combinedAppendTimestamp: boolean;
  /** .NET date format for the file-name timestamp suffix (null = "_yyyyMMdd-HHmmss"). */
  timestampFormat?: string | null;
  triggers: ScheduleTrigger[];
  nextRunAt?: string | null;
  createdAt: string;
  items: ScheduledTaskItem[];
  viewers: ScheduledTaskViewer[];
  lastRun?: ScheduledTaskRun | null;
  /** Whether the CURRENT user may download this task's export files. */
  canDownloadFiles?: boolean;
}

export interface ScheduledTaskItemInput {
  dynamicQueryId: string;
  parameters: Record<string, string>;
  exportFormat: ExportFileFormat;
  /** CSV only: field separator text (null = comma), e.g. ";" or ";;". */
  csvSeparator?: string | null;
  fileNamePrefix?: string | null;
  appendTimestamp: boolean;
  sortOrder: number;
  keyColumn?: string | null;
  keyParameter?: string | null;
  initialKey?: string | null;
  /** When true (edit only), clears the saved checkpoint so the next run starts from initialKey. */
  resetKey?: boolean;
}

export interface SaveScheduledTaskRequest {
  name: string;
  description: string;
  isEnabled: boolean;
  outputFolder: string;
  /** Optional second folder that receives a copy of every export file. */
  archiveFolder?: string | null;
  /** When true, all read-query results are appended into one output file in item order. */
  combineOutput: boolean;
  /** When false, CSV/Excel exports contain data rows only (no header row). */
  includeHeaders: boolean;
  combinedFileName?: string | null;
  combinedFormat: ExportFileFormat;
  combinedCsvSeparator?: string | null;
  combinedAppendTimestamp: boolean;
  /** .NET date format for the file-name timestamp suffix (null = "_yyyyMMdd-HHmmss"). */
  timestampFormat?: string | null;
  triggers: ScheduleTrigger[];
  items: ScheduledTaskItemInput[];
  viewerUserIds: string[];
  /** Viewers who may also download the run's export files (subset of viewerUserIds). */
  downloadUserIds: string[];
}

/**
 * Server timestamps for scheduled tasks are UTC but may serialize without a "Z"
 * suffix after an Oracle round-trip; parse them explicitly as UTC either way.
 */
export function utcDate(value: string): Date {
  return new Date(/[zZ]$|[+-]\d{2}:\d{2}$/.test(value) ? value : value + 'Z');
}

/** Human-readable one-liner for a single trigger, e.g. "Weekly on Monday at 07:00". */
export function describeSchedule(trigger: {
  frequency: ScheduleFrequency;
  intervalMinutes?: number | null;
  timeOfDay?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
}): string {
  switch (trigger.frequency) {
    case ScheduleFrequency.EveryNMinutes:
      return `Every ${trigger.intervalMinutes ?? '?'} minute(s)`;
    case ScheduleFrequency.Daily:
      return `Daily at ${trigger.timeOfDay || '00:00'}`;
    case ScheduleFrequency.Weekly:
      return `Weekly on ${DAY_NAMES[trigger.dayOfWeek ?? 1] || '?'} at ${trigger.timeOfDay || '00:00'}`;
    case ScheduleFrequency.Monthly:
      return `Monthly on day ${trigger.dayOfMonth ?? '?'} at ${trigger.timeOfDay || '00:00'}`;
    default:
      return '';
  }
}

/** All of a task's triggers as one line, e.g. "Daily at 03:00 · Monthly on day 14 at 09:00". */
export function describeTriggers(triggers: ScheduleTrigger[] | undefined): string {
  return (triggers || []).map(describeSchedule).join(' · ');
}
