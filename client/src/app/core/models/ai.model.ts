/** Mirrors Jadwal.Application.Features.Ai.AiDtos. */

export type AiState = 'disabled' | 'ready' | 'unreachable' | 'modelMissing';

export interface AiStatus {
  /** True when both the server's configuration and System Settings allow AI. */
  enabled: boolean;
  state: AiState;
  /** False when the server's appsettings has AI off, whatever System Settings say. */
  configuredOnServer: boolean;
  model: string;
  /** How long one request may take on the server. */
  timeoutSeconds: number;
}

export interface ExplainSqlRequest {
  sqlQuery: string;
  databaseUserId: string | null;
}

export interface AiSqlExplanation {
  summary: string;
  steps: string[];
  cautions: string[];
}

export interface SuggestSqlFixRequest {
  sqlQuery: string;
  errorMessage: string;
  databaseUserId: string | null;
}

export interface AiSqlFix {
  explanation: string;
  fixedSql: string;
  /** False when the model could not fix it from the SQL alone; the explanation says what to check. */
  changed: boolean;
}
