import { ImportedQueryResult, QueryImportResult } from './dynamic-query.model';

/**
 * Export and import of reports and dashboards. Mirrors Jadwal.Application/Features/Transfer.
 * One file can carry queries, reports and dashboards; one import endpoint takes all of them.
 */

export type TransferKind = 'report' | 'dashboard';

/** What an import did, per kind. A query-only import returns the narrower QueryImportResult. */
export interface TransferImportResult extends QueryImportResult {
  reports: ImportedQueryResult[];
  dashboards: ImportedQueryResult[];
}
