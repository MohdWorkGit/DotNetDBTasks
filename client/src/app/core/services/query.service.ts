import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, of, throwError, timer } from 'rxjs';
import { first, switchMap, tap } from 'rxjs/operators';
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
  CreateQueryGroupRequest,
  CreateUserRequest,
  DatabaseUser,
  DropdownOption,
  DynamicQuery,
  ExecutionLog,
  ImportedLdapUser,
  JobStatusResponse,
  LdapUser,
  MyQueryGroup,
  QueryExecutionResult,
  QueryGroup,
  ResetPasswordResult,
  Role,
  SystemUser,
  TestConnectionResult,
  ToggleUserActiveRequest,
  UpdateDatabaseUserRequest,
  UpdateDynamicQueryRequest,
  UpdateQueryGroupRequest
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
  private groupsUrl = `${environment.apiUrl}/admin/querygroups`;

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

  // Query groups (Admin)
  getAllQueryGroups(): Observable<QueryGroup[]> {
    return this.http.get<QueryGroup[]>(this.groupsUrl);
  }

  getQueryGroupById(id: string): Observable<QueryGroup> {
    return this.http.get<QueryGroup>(`${this.groupsUrl}/${id}`);
  }

  createQueryGroup(request: CreateQueryGroupRequest): Observable<QueryGroup> {
    return this.http.post<QueryGroup>(this.groupsUrl, request);
  }

  updateQueryGroup(id: string, request: UpdateQueryGroupRequest): Observable<QueryGroup> {
    return this.http.put<QueryGroup>(`${this.groupsUrl}/${id}`, request);
  }

  deleteQueryGroup(id: string): Observable<void> {
    return this.http.delete<void>(`${this.groupsUrl}/${id}`);
  }

  assignGroupRoles(groupId: string, request: AssignRolesRequest): Observable<void> {
    return this.http.post<void>(`${this.groupsUrl}/${groupId}/roles`, request);
  }

  assignGroupDepartments(groupId: string, request: AssignDepartmentsRequest): Observable<void> {
    return this.http.post<void>(`${this.groupsUrl}/${groupId}/departments`, request);
  }

  assignGroupUsers(groupId: string, request: AssignUsersRequest): Observable<void> {
    return this.http.post<void>(`${this.groupsUrl}/${groupId}/users`, request);
  }

  // User operations
  getMyQueries(): Observable<DynamicQuery[]> {
    return this.http.get<DynamicQuery[]>(this.userUrl);
  }

  getMyQueryGroups(): Observable<MyQueryGroup[]> {
    return this.http.get<MyQueryGroup[]>(`${this.userUrl}/groups`);
  }

  getMyQueryById(id: string): Observable<DynamicQuery> {
    return this.http.get<DynamicQuery>(`${this.userUrl}/${id}`);
  }

  /**
   * Executes a normal (quick) query synchronously and returns the result in one request.
   * Used for queries not flagged as long-running.
   */
  executeQuery(queryId: string, parameters: Record<string, string>, confirmed = false): Observable<QueryExecutionResult> {
    return this.http.post<QueryExecutionResult>(`${this.userUrl}/${queryId}/execute`, {
      parameters,
      confirmed
    });
  }

  /**
   * Downloads the full result set of a query as an Excel (.xlsx) file. Unlike on-screen
   * results, the export is not capped at the server's max display row count.
   *
   * Runs as a background job: submits the export, polls until it finishes, then downloads the
   * generated file. Each request stays short, so large exports are not cut off by proxy/edge
   * timeouts. The optional {@link onJobId} callback receives the job id as soon as it is created
   * so the caller can cancel the export in progress.
   */
  exportQuery(
    queryId: string,
    parameters: Record<string, string>,
    onJobId?: (jobId: string) => void
  ): Observable<Blob> {
    return this.http.post<{ jobId: string }>(`${this.userUrl}/${queryId}/export-async`, {
      parameters
    }).pipe(
      tap(({ jobId }) => onJobId?.(jobId)),
      switchMap(({ jobId }) =>
        this.pollJobUntilComplete(jobId).pipe(
          switchMap(() => this.http.get(`${this.userUrl}/jobs/${jobId}/export-file`, {
            responseType: 'blob'
          }))
        )
      )
    );
  }

  /**
   * Polls a submitted job every 2s until it reaches a terminal state. Completes when the job
   * succeeds, or errors with the server message if it failed or was canceled. Unlike
   * {@link pollJobResult} it does not emit the row result — used by exports, where the output
   * is downloaded as a file rather than read as JSON.
   */
  private pollJobUntilComplete(jobId: string): Observable<void> {
    return timer(0, 2000).pipe(
      switchMap(() => this.http.get<JobStatusResponse>(`${this.userUrl}/jobs/${jobId}`)),
      first(res => res.status === 'Succeeded' || res.status === 'Failed' || res.status === 'Canceled'),
      switchMap(res =>
        res.status === 'Succeeded'
          ? of(void 0)
          : throwError(() => ({ error: { message: res.error || 'Export was canceled' } }))
      )
    );
  }

  /**
   * Submits a long-running query for asynchronous execution. Returns the job id immediately;
   * the actual query runs on the server's background worker. Poll {@link pollJobResult} for
   * the result.
   */
  submitQuery(queryId: string, parameters: Record<string, string>, confirmed = false): Observable<{ jobId: string }> {
    return this.http.post<{ jobId: string }>(`${this.userUrl}/${queryId}/execute-async`, {
      parameters,
      confirmed
    });
  }

  /**
   * Polls a submitted job every 2s until it reaches a terminal state, then emits the result
   * (or errors with the server message). Each poll is a fast request, so long-running queries
   * are never cut off by proxy/edge timeouts. Unsubscribe to stop polling.
   */
  pollJobResult(jobId: string): Observable<QueryExecutionResult> {
    return timer(0, 2000).pipe(
      switchMap(() => this.http.get<JobStatusResponse>(`${this.userUrl}/jobs/${jobId}`)),
      first(res => res.status === 'Succeeded' || res.status === 'Failed' || res.status === 'Canceled'),
      switchMap(res =>
        res.status === 'Succeeded'
          ? of(res.result!)
          : throwError(() => ({ error: { message: res.error || 'Query was canceled' } }))
      )
    );
  }

  /** Cancels a running job, stopping the underlying database command server-side. */
  cancelJob(jobId: string): Observable<void> {
    return this.http.post<void>(`${this.userUrl}/jobs/${jobId}/cancel`, {});
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
