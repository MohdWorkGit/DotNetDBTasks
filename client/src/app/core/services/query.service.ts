import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import {
  AssignDatabaseUserAccessRequest,
  AssignDepartmentsRequest,
  AssignRolesRequest,
  AssignUsersRequest,
  ChangePasswordRequest,
  ChangeUsernameRequest,
  ChangeUserRolesRequest,
  CreateDatabaseUserRequest,
  CreateDynamicQueryRequest,
  CreateUserRequest,
  DatabaseUser,
  DropdownOption,
  DynamicQuery,
  ExecutionLog,
  ImportedLdapUser,
  LdapUser,
  QueryExecutionResult,
  ResetPasswordResult,
  Role,
  SystemUser,
  TestConnectionResult,
  ToggleUserActiveRequest,
  UpdateDatabaseUserRequest,
  UpdateDynamicQueryRequest
} from '../models/dynamic-query.model';

@Injectable({
  providedIn: 'root'
})
export class QueryService {
  private adminUrl = `${environment.apiUrl}/admin/dynamicqueries`;
  private userUrl = `${environment.apiUrl}/user/queries`;
  private rolesUrl = `${environment.apiUrl}/admin/roles`;
  private ldapUrl = `${environment.apiUrl}/admin/ldap`;
  private dbUsersUrl = `${environment.apiUrl}/admin/databaseusers`;

  constructor(private http: HttpClient) {}

  // Admin operations
  getAllQueries(): Observable<DynamicQuery[]> {
    return this.http.get<DynamicQuery[]>(this.adminUrl);
  }

  getQueryById(id: string): Observable<DynamicQuery> {
    return this.http.get<DynamicQuery>(`${this.adminUrl}/${id}`);
  }

  createQuery(request: CreateDynamicQueryRequest): Observable<DynamicQuery> {
    return this.http.post<DynamicQuery>(this.adminUrl, request);
  }

  updateQuery(id: string, request: UpdateDynamicQueryRequest): Observable<DynamicQuery> {
    return this.http.put<DynamicQuery>(`${this.adminUrl}/${id}`, request);
  }

  deleteQuery(id: string): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl}/${id}`);
  }

  assignRoles(queryId: string, request: AssignRolesRequest): Observable<void> {
    return this.http.post<void>(`${this.adminUrl}/${queryId}/roles`, request);
  }

  assignDepartments(queryId: string, request: AssignDepartmentsRequest): Observable<void> {
    return this.http.post<void>(`${this.adminUrl}/${queryId}/departments`, request);
  }

  assignUsers(queryId: string, request: AssignUsersRequest): Observable<void> {
    return this.http.post<void>(`${this.adminUrl}/${queryId}/users`, request);
  }

  getExecutionLogs(queryId?: string, userId?: string): Observable<ExecutionLog[]> {
    let params: any = {};
    if (queryId) params.queryId = queryId;
    if (userId) params.userId = userId;
    return this.http.get<ExecutionLog[]>(`${this.adminUrl}/logs`, { params });
  }

  getRoles(): Observable<Role[]> {
    return this.http.get<Role[]>(this.rolesUrl);
  }

  // Database user management (Admin)
  getAllDatabaseUsers(): Observable<DatabaseUser[]> {
    return this.http.get<DatabaseUser[]>(this.dbUsersUrl);
  }

  getDatabaseUserById(id: string): Observable<DatabaseUser> {
    return this.http.get<DatabaseUser>(`${this.dbUsersUrl}/${id}`);
  }

  createDatabaseUser(request: CreateDatabaseUserRequest): Observable<DatabaseUser> {
    return this.http.post<DatabaseUser>(this.dbUsersUrl, request);
  }

  updateDatabaseUser(id: string, request: UpdateDatabaseUserRequest): Observable<DatabaseUser> {
    return this.http.put<DatabaseUser>(`${this.dbUsersUrl}/${id}`, request);
  }

  deleteDatabaseUser(id: string): Observable<void> {
    return this.http.delete<void>(`${this.dbUsersUrl}/${id}`);
  }

  assignDatabaseUserAccess(dbUserId: string, request: AssignDatabaseUserAccessRequest): Observable<void> {
    return this.http.post<void>(`${this.dbUsersUrl}/${dbUserId}/access`, request);
  }

  testDatabaseConnection(dbUserId: string): Observable<TestConnectionResult> {
    return this.http.post<TestConnectionResult>(`${this.dbUsersUrl}/${dbUserId}/test-connection`, {});
  }

  // User operations
  getMyQueries(): Observable<DynamicQuery[]> {
    return this.http.get<DynamicQuery[]>(this.userUrl);
  }

  getMyQueryById(id: string): Observable<DynamicQuery> {
    return this.http.get<DynamicQuery>(`${this.userUrl}/${id}`);
  }

  executeQuery(queryId: string, parameters: Record<string, string>): Observable<QueryExecutionResult> {
    return this.http.post<QueryExecutionResult>(`${this.userUrl}/${queryId}/execute`, {
      parameters
    });
  }

  getDropdownOptions(queryId: string, parameterId: string): Observable<DropdownOption[]> {
    return this.http.get<DropdownOption[]>(
      `${this.userUrl}/${queryId}/parameters/${parameterId}/dropdown-options`
    );
  }

  getMyHistory(): Observable<ExecutionLog[]> {
    return this.http.get<ExecutionLog[]>(`${this.userUrl}/history`);
  }

  // LDAP / Active Directory operations
  searchLdapUsers(term: string): Observable<LdapUser[]> {
    return this.http.get<LdapUser[]>(`${this.ldapUrl}/search`, { params: { term } });
  }

  getLdapDepartments(): Observable<string[]> {
    return this.http.get<string[]>(`${this.ldapUrl}/departments`);
  }

  getLdapDepartmentUsers(department: string): Observable<LdapUser[]> {
    return this.http.get<LdapUser[]>(`${this.ldapUrl}/departments/${encodeURIComponent(department)}/users`);
  }

  importLdapUsers(usernames: string[]): Observable<{ imported: number }> {
    return this.http.post<{ imported: number }>(`${this.ldapUrl}/import/users`, { usernames });
  }

  importLdapDepartment(department: string): Observable<{ imported: number }> {
    return this.http.post<{ imported: number }>(`${this.ldapUrl}/import/department`, { department });
  }

  getImportedLdapUsers(): Observable<ImportedLdapUser[]> {
    return this.http.get<ImportedLdapUser[]>(`${this.ldapUrl}/imported`);
  }

  revokeLdapUser(username: string): Observable<void> {
    return this.http.post<void>(`${this.ldapUrl}/revoke/${encodeURIComponent(username)}`, {});
  }

  restoreLdapUser(username: string): Observable<void> {
    return this.http.post<void>(`${this.ldapUrl}/restore/${encodeURIComponent(username)}`, {});
  }

  syncLdapImportedUsers(): Observable<{ synced: number; notFound: number }> {
    return this.http.post<{ synced: number; notFound: number }>(`${this.ldapUrl}/sync`, {});
  }

  // User management operations (Admin)
  private usersUrl = `${environment.apiUrl}/admin/users`;

  getAllUsers(): Observable<SystemUser[]> {
    return this.http.get<SystemUser[]>(this.usersUrl);
  }

  getUserById(id: string): Observable<SystemUser> {
    return this.http.get<SystemUser>(`${this.usersUrl}/${id}`);
  }

  createUser(request: CreateUserRequest): Observable<SystemUser> {
    return this.http.post<SystemUser>(this.usersUrl, request);
  }

  changeUsername(userId: string, request: ChangeUsernameRequest): Observable<void> {
    return this.http.put<void>(`${this.usersUrl}/${userId}/username`, request);
  }

  changePassword(userId: string, request: ChangePasswordRequest): Observable<void> {
    return this.http.put<void>(`${this.usersUrl}/${userId}/password`, request);
  }

  resetPassword(userId: string): Observable<ResetPasswordResult> {
    return this.http.post<ResetPasswordResult>(`${this.usersUrl}/${userId}/reset-password`, {});
  }

  changeUserRoles(userId: string, request: ChangeUserRolesRequest): Observable<void> {
    return this.http.put<void>(`${this.usersUrl}/${userId}/roles`, request);
  }

  toggleUserActive(userId: string, request: ToggleUserActiveRequest): Observable<void> {
    return this.http.put<void>(`${this.usersUrl}/${userId}/active`, request);
  }
}
