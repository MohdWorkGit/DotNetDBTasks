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
  assignedUsers: DatabaseUserAccess[];
}

export interface DatabaseUserAccess {
  userId: string;
  username: string;
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
  userIds: string[];
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
  databaseUserId?: string;
  databaseUserName?: string;
  createdAt: string;
  parameters: QueryParameter[];
  assignedRoles: RoleAssignment[];
  assignedDepartments: DepartmentAssignment[];
  assignedUsers: UserAssignment[];
}

export interface CreateDynamicQueryRequest {
  name: string;
  description: string;
  sqlQuery: string;
  timeoutSeconds: number;
  databaseUserId?: string | null;
  parameters: QueryParameter[];
}

export interface UpdateDynamicQueryRequest extends CreateDynamicQueryRequest {
  id: string;
  isEnabled: boolean;
  databaseUserId?: string | null;
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
  /** True when this is a preview of a write query — the change was rolled back. Re-submit with confirmed=true to commit. */
  requiresConfirmation?: boolean;
  /** For UPDATE/DELETE previews: the current rows that match the WHERE clause and will be affected. */
  previewColumns?: string[];
  previewRows?: Record<string, any>[];
}

export interface ExecutionLog {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  userId: string;
  username: string;
  parameters: Record<string, string>;
  /** For UPDATE/DELETE queries: every affected row as it existed before the change. */
  oldValues?: Record<string, string>[] | null;
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
