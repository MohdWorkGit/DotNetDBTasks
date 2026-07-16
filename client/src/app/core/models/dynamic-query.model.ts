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

export interface DepartmentAssignment {
  department: string;
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
  isEnabled: boolean;
  timeoutSeconds: number;
  isLongRunning: boolean;
  databaseUserId?: string;
  databaseUserName?: string;
  queryGroupId?: string | null;
  queryGroupName?: string | null;
  createdAt: string;
  parameters: QueryParameter[];
  assignedRoles: RoleAssignment[];
  assignedDepartments: DepartmentAssignment[];
  assignedUsers: UserAssignment[];
}

export interface QueryGroup {
  id: string;
  name: string;
  description: string;
  createdAt: string;
  queryCount: number;
  assignedRoles: RoleAssignment[];
  assignedDepartments: DepartmentAssignment[];
  assignedUsers: UserAssignment[];
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

export interface AssignRolesRequest {
  roleIds: string[];
}

export interface AssignDepartmentsRequest {
  departments: string[];
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
  department?: string;
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
