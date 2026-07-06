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
  Json = 2
}

export const EXPORT_FORMAT_LABELS: Record<ExportFileFormat, string> = {
  [ExportFileFormat.Excel]: 'Excel (.xlsx)',
  [ExportFileFormat.Csv]: 'CSV (.csv)',
  [ExportFileFormat.Json]: 'JSON (.json)'
};

const DAY_NAMES = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];

export interface ScheduledTaskItem {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  parameters: Record<string, string>;
  exportFormat: ExportFileFormat;
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
  frequency: ScheduleFrequency;
  intervalMinutes?: number | null;
  timeOfDay?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  nextRunAt?: string | null;
  createdAt: string;
  items: ScheduledTaskItem[];
  viewers: ScheduledTaskViewer[];
  lastRun?: ScheduledTaskRun | null;
}

export interface ScheduledTaskItemInput {
  dynamicQueryId: string;
  parameters: Record<string, string>;
  exportFormat: ExportFileFormat;
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
  frequency: ScheduleFrequency;
  intervalMinutes?: number | null;
  timeOfDay?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
  items: ScheduledTaskItemInput[];
  viewerUserIds: string[];
}

/**
 * Server timestamps for scheduled tasks are UTC but may serialize without a "Z"
 * suffix after an Oracle round-trip; parse them explicitly as UTC either way.
 */
export function utcDate(value: string): Date {
  return new Date(/[zZ]$|[+-]\d{2}:\d{2}$/.test(value) ? value : value + 'Z');
}

/** Human-readable one-liner for a task's recurrence, e.g. "Weekly on Monday at 07:00". */
export function describeSchedule(task: {
  frequency: ScheduleFrequency;
  intervalMinutes?: number | null;
  timeOfDay?: string | null;
  dayOfWeek?: number | null;
  dayOfMonth?: number | null;
}): string {
  switch (task.frequency) {
    case ScheduleFrequency.EveryNMinutes:
      return `Every ${task.intervalMinutes ?? '?'} minute(s)`;
    case ScheduleFrequency.Daily:
      return `Daily at ${task.timeOfDay || '00:00'}`;
    case ScheduleFrequency.Weekly:
      return `Weekly on ${DAY_NAMES[task.dayOfWeek ?? 1] || '?'} at ${task.timeOfDay || '00:00'}`;
    case ScheduleFrequency.Monthly:
      return `Monthly on day ${task.dayOfMonth ?? '?'} at ${task.timeOfDay || '00:00'}`;
    default:
      return '';
  }
}
