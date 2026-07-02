import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, of, throwError, timer } from 'rxjs';
import { first, switchMap } from 'rxjs/operators';
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
  ExecuteResult,
  ExecutionLog,
  ImportedLdapUser,
  JobRowsResponse,
  JobStatusResponse,
  LdapUser,
  MyQueryGroup,
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

/** Grid paging/sort/filter parameters sent to the cached-rows endpoint. */
export interface JobRowsQuery {
  pageIndex: number;
  pageSize: number;
  sortColumn?: string;
  sortDir?: string;
  /** Column -> substring; empty entries are ignored server-side. */
  filters?: Record<string, string>;
}

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
   * Executes a normal (quick) query synchronously and returns the result metadata in one request.
   * For read queries the full result is cached server-side (see {@link ExecuteResult.jobId}); rows
   * are fetched a page at a time via {@link getJobRows} and export reuses the same cached result.
   */
  executeQuery(queryId: string, parameters: Record<string, string>, confirmed = false): Observable<ExecuteResult> {
    return this.http.post<ExecuteResult>(`${this.userUrl}/${queryId}/execute`, {
      parameters,
      confirmed
    });
  }

  /**
   * Fetches a single page of a cached read result, applying server-side column filtering and
   * sorting over the full set. Called on every page/sort/filter change so only one page crosses
   * the wire while the underlying query ran only once.
   */
  getJobRows(jobId: string, query: JobRowsQuery): Observable<JobRowsResponse> {
    let params = new HttpParams()
      .set('pageIndex', query.pageIndex)
      .set('pageSize', query.pageSize);

    if (query.sortColumn && query.sortDir) {
      params = params.set('sortColumn', query.sortColumn).set('sortDir', query.sortDir);
    }

    const activeFilters = Object.fromEntries(
      Object.entries(query.filters ?? {}).filter(([, v]) => !!v)
    );
    if (Object.keys(activeFilters).length > 0) {
      params = params.set('filters', JSON.stringify(activeFilters));
    }

    return this.http.get<JobRowsResponse>(`${this.userUrl}/jobs/${jobId}/rows`, { params });
  }

  /**
   * Downloads the complete result set of a cached read job as an Excel (.xlsx) file. No second
   * query run — the workbook is built from the result cached during execution.
   */
  exportJob(jobId: string): Observable<Blob> {
    return this.http.get(`${this.userUrl}/jobs/${jobId}/export-file`, { responseType: 'blob' });
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
   * metadata (or errors with the server message). Each poll is a fast request, so long-running
   * queries are never cut off by proxy/edge timeouts. Unsubscribe to stop polling.
   */
  pollJobResult(jobId: string): Observable<ExecuteResult> {
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

  /**
   * Releases a cached result immediately (frees its server memory). Called when leaving the
   * results page; the result would otherwise expire on its own after the retention window.
   */
  releaseJob(jobId: string): Observable<void> {
    return this.http.delete<void>(`${this.userUrl}/jobs/${jobId}`);
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
