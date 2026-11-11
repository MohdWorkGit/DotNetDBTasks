import { DashboardInput } from '@core/models/dashboard.model';

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

export interface DraftDashboardRequest {
  queryIds: string[];
  goal: string | null;
}

/** A proposed dashboard, in the shape a save takes. Nothing has been saved. */
export interface AiDashboardDraft {
  draft: DashboardInput;
  /** The model's reason for each tile, in tile order. */
  reasons: string[];
  /** What was left out and why. */
  notes: string[];
}

/** A connection the AI may read. The default connection has no id and is named by the page. */
export interface AiConnection {
  id: string | null;
  name: string;
  isDefault: boolean;
  serverType: number;
}

export interface AiSchemaObject {
  /** How SQL names it, and how it is sent back when picked. */
  qualifiedName: string;
  schema: string;
  name: string;
  kind: 'table' | 'view';
}

export interface AiSchema {
  objects: AiSchemaObject[];
  /** Example values go to the model with the structure (System Settings). */
  sampleValues: boolean;
}

export interface WriteSqlRequest {
  databaseUserId: string | null;
  description: string;
  objects: string[];
}

export interface AiSuggestedParameter {
  name: string;
  displayName: string;
  parameterType: number;
}

/** SQL written from a description. Only shown: it is tested and saved like any query. */
export interface AiWrittenSql {
  sql: string;
  explanation: string;
  parameters: AiSuggestedParameter[];
}
