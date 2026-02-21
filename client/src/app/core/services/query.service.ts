import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import {
  AssignRolesRequest,
  CreateDynamicQueryRequest,
  DynamicQuery,
  ExecutionLog,
  ImportedLdapUser,
  LdapUser,
  QueryExecutionResult,
  Role,
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

  getExecutionLogs(queryId?: string, userId?: string): Observable<ExecutionLog[]> {
    let params: any = {};
    if (queryId) params.queryId = queryId;
    if (userId) params.userId = userId;
    return this.http.get<ExecutionLog[]>(`${this.adminUrl}/logs`, { params });
  }

  getRoles(): Observable<Role[]> {
    return this.http.get<Role[]>(this.rolesUrl);
  }

  // User operations
  getMyQueries(): Observable<DynamicQuery[]> {
    return this.http.get<DynamicQuery[]>(this.userUrl);
  }

  executeQuery(queryId: string, parameters: Record<string, string>): Observable<QueryExecutionResult> {
    return this.http.post<QueryExecutionResult>(`${this.userUrl}/${queryId}/execute`, parameters);
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
}
