export enum ParameterType {
  String = 0,
  Number = 1,
  Date = 2,
  Boolean = 3,
  Dropdown = 4
}

export enum DropdownSourceType {
  Static = 0,
  Query = 1
}

/** Mirrors the server-side QueryType enum. Derived from the SQL and stored on the query. */
export enum QueryType {
  Select = 0,
  Insert = 1,
  Update = 2,
  Delete = 3,
  /** Anything else — MERGE, DDL, a PL/SQL block. Not treated as a write. */
  Other = 4
}

/** Display labels for the type filters and columns. */
export const QUERY_TYPE_LABELS: Record<QueryType, string> = {
  [QueryType.Select]: 'SELECT',
  [QueryType.Insert]: 'INSERT',
  [QueryType.Update]: 'UPDATE',
  [QueryType.Delete]: 'DELETE',
  [QueryType.Other]: 'Other'
};

/** True for the statement types that modify data. Mirrors QueryTypeClassifier.IsWrite. */
export function isWriteQueryType(type: QueryType | undefined): boolean {
  return type === QueryType.Insert || type === QueryType.Update || type === QueryType.Delete;
}

export interface DropdownOption {
  label: string;
  value: string;
}

export interface QueryParameter {
  id?: string;
  name: string;
  displayName: string;
  parameterType: ParameterType;
  isRequired: boolean;
  defaultValue?: string;
  sortOrder: number;
  // Dropdown-specific fields
  allowMultiple?: boolean;
  dropdownSourceType?: DropdownSourceType;
  dropdownStaticValues?: string;
  dropdownQueryId?: string;
  dropdownQueryValueColumn?: string;
  dropdownQueryLabelColumn?: string;
}

export interface RoleAssignment {
  roleId: string;
  roleName: string;
}

/** A grant made to one of the application's own user groups. */
export interface UserGroupAssignment {
  userGroupId: string;
  userGroupName: string;
}

export interface UserAssignment {
  userId: string;
  username: string;
}

export enum DatabaseServerType {
  Oracle = 0,
  SqlServer = 1,
  PostgreSql = 2,
  MySql = 3
}

export interface DatabaseUser {
  id: string;
  name: string;
  description: string;
  serverType: DatabaseServerType;
  host: string;
  port: number;
  serviceName: string;
  databaseName: string;
  dbUsername: string;
  isActive: boolean;
  createdAt: string;
  assignedRoles: DatabaseUserRoleAccess[];
}

export interface DatabaseUserRoleAccess {
  roleId: string;
  roleName: string;
}

export interface DatabaseUserSummary {
  id: string;
  name: string;
}

export interface CreateDatabaseUserRequest {
  name: string;
  description: string;
  serverType: DatabaseServerType;
  host: string;
  port: number;
  serviceName: string;
  databaseName: string;
  dbUsername: string;
  password: string;
}

export interface UpdateDatabaseUserRequest {
  id: string;
  name: string;
  description: string;
  serverType: DatabaseServerType;
  host: string;
  port: number;
  serviceName: string;
  databaseName: string;
  dbUsername: string;
  isActive: boolean;
  password?: string;
}

export interface AssignDatabaseUserAccessRequest {
  roleIds: string[];
}

export interface TestConnectionResult {
  success: boolean;
  errorMessage?: string;
}

export interface DynamicQuery {
  id: string;
  name: string;
  description: string;
  sqlQuery: string;
  /** Derived from sqlQuery server-side on every save; read-only here. */
  queryType: QueryType;
  isEnabled: boolean;
  timeoutSeconds: number;
  isLongRunning: boolean;
  /** Admin setting: whether write runs may skip the preview/confirm step. */
  allowRunWithoutConfirmation: boolean;
  /** Admin setting: whether UPDATE/DELETE runs snapshot the pre-change rows into the audit log. */
  saveOldValues: boolean;
  databaseUserId?: string;
  databaseUserName?: string;
  queryGroupId?: string | null;
  queryGroupName?: string | null;
  /** File name of the uploaded Word export template; null/absent when none. */
  wordTemplateFileName?: string | null;
  createdAt: string;
  parameters: QueryParameter[];
  assignedRoles: RoleAssignment[];
  assignedUserGroups: UserGroupAssignment[];
  assignedUsers: UserAssignment[];
}

export interface QueryGroup {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  queryCount: number;
  assignedRoles: RoleAssignment[];
  assignedUserGroups: UserGroupAssignment[];
  assignedUsers: UserAssignment[];
}

/**
 * A group of users maintained in this application. Access is granted to groups, and
 * membership is set here — deliberately not read from Active Directory, so it also covers
 * local accounts and survives a directory reorganisation.
 */
export interface UserGroup {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  memberCount: number;
  members: UserGroupMember[];
}

export interface UserGroupMember {
  userId: string;
  username: string;
  firstName: string;
  lastName: string;
  isActive: boolean;
}

export interface CreateUserGroupRequest {
  name: string;
  description: string;
  memberUserIds: string[];
}

export interface UpdateUserGroupRequest {
  id: string;
  name: string;
  description: string;
}

export interface SetUserGroupMembersRequest {
  userIds: string[];
}

export interface CreateQueryGroupRequest {
  name: string;
  description: string;
}

export interface UpdateQueryGroupRequest {
  id: string;
  name: string;
  description: string;
}

/** A bucket of accessible queries surfaced on the "My Queries" page. id=null for the
 * synthetic "Ungrouped" bucket containing queries with no QueryGroupId. */
export interface MyQueryGroup {
  id: string | null;
  name: string;
  description: string;
  queries: DynamicQuery[];
}

export interface CreateDynamicQueryRequest {
  name: string;
  description: string;
  sqlQuery: string;
  timeoutSeconds: number;
  isLongRunning: boolean;
  allowRunWithoutConfirmation: boolean;
  saveOldValues: boolean;
  databaseUserId?: string | null;
  queryGroupId?: string | null;
  parameters: QueryParameter[];
}

export interface UpdateDynamicQueryRequest extends CreateDynamicQueryRequest {
  id: string;
  isEnabled: boolean;
  databaseUserId?: string | null;
  queryGroupId?: string | null;
}

/** One query's outcome from an import. */
export interface ImportedQueryResult {
  originalName: string;
  importedName: string;
  /** True when the name was already taken, so this came in as a copy. */
  wasRenamed: boolean;
}

/** Summary returned by the import endpoint. */
export interface QueryImportResult {
  queries: ImportedQueryResult[];
  /** Names that could not be resolved here — missing roles, users, DB connections. */
  warnings: string[];
  importedCount: number;
  renamedCount: number;
}

export interface AssignRolesRequest {
  roleIds: string[];
}

export interface AssignUserGroupsRequest {
  userGroupIds: string[];
}

export interface AssignUsersRequest {
  userIds: string[];
}

export interface QueryExecutionResult {
  columns: string[];
  rows: Record<string, any>[];
  totalRows: number;
  affectedRows: number;
  executionDurationMs: number;
  /** True when the result was capped at the server's max display row count; the on-screen
   * table is partial. Use the Excel export to get the complete result set. */
  isLimitReached?: boolean;
  /** True when this is a preview of a write query — the change was rolled back. Re-submit with confirmed=true to commit. */
  requiresConfirmation?: boolean;
  /** For UPDATE/DELETE previews: the current rows that match the WHERE clause and will be affected. */
  previewColumns?: string[];
  previewRows?: Record<string, any>[];
}

/**
 * Lightweight result of executing a query. Read results are cached server-side: `jobId` points at
 * the cached set and `rows` are fetched a page at a time via the rows endpoint (never inline here).
 * Write results carry `affectedRows` and, for previews, `requiresConfirmation` + `previewRows`.
 */
export interface ExecuteResult {
  /** Set for cached read results; used to page the grid and to export without re-running the query. */
  jobId?: string | null;
  requiresConfirmation?: boolean;
  columns: string[];
  totalRows: number;
  affectedRows: number;
  isLimitReached?: boolean;
  executionDurationMs: number;
  previewColumns?: string[];
  previewRows?: Record<string, any>[];
}

/** One page of a cached read result, returned by the rows endpoint. */
export interface JobRowsResponse {
  rows: Record<string, any>[];
  /** Total rows after the active column filters (drives the paginator length). */
  filteredTotal: number;
  /** Total rows in the unfiltered result set. */
  totalRows: number;
}

/** Status of an async query job, returned while polling after a query is submitted. */
export interface JobStatusResponse {
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Canceled';
  result?: ExecuteResult;
  error?: string;
}

export interface ExecutionLog {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  /** Statement type of the underlying query, used by the log list's Type filter. */
  queryType: QueryType;
  userId: string;
  username: string;
  parameters: Record<string, string>;
  /**
   * True when pre-change row snapshots were recorded for this log (UPDATE/DELETE).
   * The rows themselves are fetched on demand, one page at a time, via the
   * old-values endpoint — they are never part of list responses.
   */
  hasOldValues: boolean;
  /** True when the SQL query is a DML UPDATE statement. */
  isUpdateQuery: boolean;
  /** True when the SQL query is a DML DELETE statement. */
  isDeleteQuery?: boolean;
  executedAt: string;
  executionDurationMs: number;
  rowsReturned: number;
  isSuccess: boolean;
  errorMessage?: string;
}

/** Generic server-side pagination envelope (mirrors PaginatedList<T> on the API). */
export interface PagedResult<T> {
  items: T[];
  pageNumber: number;
  totalPages: number;
  totalCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

/** Paging/sort/filter parameters for the execution-log list endpoints. */
export interface ExecutionLogListRequest {
  queryId?: string;
  userId?: string;
  isSuccess?: boolean;
  queryType?: QueryType;
  search?: string;
  sortBy?: string;
  sortDescending?: boolean;
  pageNumber?: number;
  pageSize?: number;
}

/** One page of pre-change row snapshots for a single execution log. */
export interface OldValuesPage {
  totalRows: number;
  pageNumber: number;
  pageSize: number;
  columns: string[];
  rows: Record<string, string>[];
}

export interface Role {
  id: string;
  name: string;
  description: string;
}

/** One row of the permission matrix: a role and what it may do. */
export interface RolePermissions {
  id: string;
  name: string;
  description?: string | null;
  /** True for the four roles the system ships; they can be re-permissioned but not renamed. */
  isSeeded: boolean;
  /** True for Admin, which holds everything and cannot be edited. */
  isPinned: boolean;
  permissions: string[];
}

/** The whole matrix: every capability the server defines, and every role's holdings. */
export interface PermissionMatrix {
  permissions: string[];
  roles: RolePermissions[];
}

export interface SaveRoleRequest {
  name: string;
  description?: string | null;
  permissions: string[];
}

export interface LdapUser {
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  department?: string;
  isImported: boolean;
}

export interface ImportedLdapUser {
  id: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  department?: string;
  isActive: boolean;
  createdAt: string;
}

export interface SystemUser {
  id: string;
  username: string;
  email: string;
  firstName: string;
  lastName: string;
  isActive: boolean;
  authSource: string;
  department?: string;
  createdAt: string;
  updatedAt?: string;
  roles: string[];
}

export interface CreateUserRequest {
  username: string;
  email: string;
  password: string;
  firstName: string;
  lastName: string;
  roleIds: string[];
}

export interface ChangeUsernameRequest {
  newUsername: string;
}

export interface ChangePasswordRequest {
  newPassword: string;
}

export interface ChangeUserRolesRequest {
  roleIds: string[];
}

export interface ToggleUserActiveRequest {
  isActive: boolean;
}

export interface ResetPasswordResult {
  temporaryPassword: string;
}

export interface LdapImportSkip {
  username: string;
  reason: string;
}

/**
 * Outcome of an Active Directory import. The API reports per-user reasons rather than only a
 * count, so a partially-successful run can say exactly which accounts were left out and why.
 * Distinct from QueryImportResult, which is about importing query definitions.
 */
export interface LdapImportResult {
  imported: number;
  notFound: string[];
  alreadyImported: string[];
  skipped: LdapImportSkip[];
  /** One-line message assembled server-side, ready to show as-is. */
  summary: string;
}

/**
 * One administrative action from the system audit trail.
 *
 * `action` is a stable machine code (`users.create`), not a sentence — the UI translates it,
 * which is what lets the log read in Arabic as well as English.
 */
export interface SystemAuditLog {
  id: string;
  occurredAt: string;
  username: string;
  action: string;
  category: string;
  entityName?: string | null;
  detailsJson?: string | null;
  isSuccess: boolean;
  errorMessage?: string | null;
  ipAddress?: string | null;
}

export interface SystemAuditLogListRequest {
  category?: string;
  action?: string;
  userId?: string;
  isSuccess?: boolean;
  search?: string;
  pageNumber?: number;
  pageSize?: number;
}

/** Filter options advertised by the server, so the dropdowns cannot drift from what it emits. */
export interface AuditActionCatalog {
  categories: string[];
  actions: string[];
}

/** Runtime settings an administrator can change without a restart. */
export interface SystemSettings {
  /** Access-token lifetime in minutes. */
  sessionAccessTokenMinutes: number;
  /** Refresh-token lifetime in days. */
  sessionRefreshTokenDays: number;
  /** Rows a query may return to the grid before the result is capped and flagged. */
  queryMaxRows: number;
  /** When false, the Active Directory pages are switched off. */
  directoryEnabled: boolean;
}

/**
 * The bounds the API enforces on the numeric settings. Mirrored here so the form can say no
 * before a round trip — the server check in SystemSettingsController is the real one.
 */
export const SETTING_LIMITS = {
  accessTokenMinutes: { min: 5, max: 1440 },
  refreshTokenDays: { min: 1, max: 90 },
  maxRows: { min: 100, max: 1000000 }
} as const;
