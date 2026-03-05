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

export interface DynamicQuery {
  id: string;
  name: string;
  description: string;
  sqlQuery: string;
  isEnabled: boolean;
  timeoutSeconds: number;
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
  parameters: QueryParameter[];
}

export interface UpdateDynamicQueryRequest extends CreateDynamicQueryRequest {
  id: string;
  isEnabled: boolean;
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
}

export interface ExecutionLog {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  userId: string;
  username: string;
  parameters: Record<string, string>;
  executedAt: string;
  executionDurationMs: number;
  rowsReturned: number;
  isSuccess: boolean;
  errorMessage?: string;
}

export interface ParameterChangeHistory {
  id: string;
  dynamicQueryId: string;
  queryName: string;
  parameterName: string;
  changeType: string;
  fieldName: string;
  oldValue?: string;
  newValue?: string;
  changedAt: string;
  changedByUserId?: string;
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
