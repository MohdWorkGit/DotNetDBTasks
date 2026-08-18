import { PERM } from './permissions';

/**
 * The export formats a query's result can be downloaded as.
 *
 * <p>Mirrors <c>ExportFileFormat</c> and <c>ExportPermissions</c> on the server. Two gates decide
 * whether a format is offered: the query lists which formats it may be exported as at all, and
 * the signed-in user must hold the matching capability. Both are re-checked by the API — this
 * only keeps the menu from offering a download that would come back 403.</p>
 */
export interface ExportFormatOption {
  /** Matches the server's ExportFileFormat name, and what is stored on the query. */
  readonly name: string;
  /**
   * The value the download endpoint expects on its ?format= query string. Kept as the literal
   * union rather than string so a typo cannot reach exportResults(); it matches ExportFormat in
   * query.service structurally, without models having to depend on services.
   */
  readonly apiValue: 'xlsx' | 'csv' | 'json' | 'pdf' | 'docx';
  readonly permission: string;
  readonly icon: string;
  readonly labelKey: string;
}

export const EXPORT_FORMATS: readonly ExportFormatOption[] = [
  { name: 'Excel', apiValue: 'xlsx', permission: PERM.queriesExportExcel,
    icon: 'table_view', labelKey: 'user.execute.formatExcel' },
  { name: 'Csv', apiValue: 'csv', permission: PERM.queriesExportCsv,
    icon: 'description', labelKey: 'user.execute.formatCsv' },
  { name: 'Pdf', apiValue: 'pdf', permission: PERM.queriesExportPdf,
    icon: 'picture_as_pdf', labelKey: 'user.execute.formatPdf' },
  { name: 'Word', apiValue: 'docx', permission: PERM.queriesExportWord,
    icon: 'article', labelKey: 'user.execute.formatWord' },
  { name: 'Json', apiValue: 'json', permission: PERM.queriesExportJson,
    icon: 'data_object', labelKey: 'user.execute.formatJson' }
];
