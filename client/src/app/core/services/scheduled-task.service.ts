import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import {
  AssignScheduledTaskRolesRequest,
  AssignScheduledTaskUserGroupsRequest,
  AssignScheduledTaskUsersRequest,
  SaveScheduledTaskRequest,
  ScheduledTask,
  ScheduledTaskAccess,
  ScheduledTaskRun
} from '../models/scheduled-task.model';

@Injectable({
  providedIn: 'root'
})
export class ScheduledTaskService {
  private baseUrl = `${environment.apiUrl}/scheduledtasks`;

  constructor(private http: HttpClient) {}

  /** Admin/Auditor: all tasks. Other users: only tasks they may view. */
  getAll(): Observable<ScheduledTask[]> {
    return this.http.get<ScheduledTask[]>(this.baseUrl);
  }

  getById(id: string): Observable<ScheduledTask> {
    return this.http.get<ScheduledTask>(`${this.baseUrl}/${id}`);
  }

  getRuns(id: string, take = 50): Observable<ScheduledTaskRun[]> {
    const params = new HttpParams().set('take', take);
    return this.http.get<ScheduledTaskRun[]>(`${this.baseUrl}/${id}/runs`, { params });
  }

  /** Downloads an export file recorded in a run's item results (404 if it was since removed). */
  downloadRunFile(taskId: string, runId: string, fileName: string): Observable<Blob> {
    const params = new HttpParams().set('fileName', fileName);
    return this.http.get(`${this.baseUrl}/${taskId}/runs/${runId}/file`, {
      params,
      responseType: 'blob'
    });
  }

  create(request: SaveScheduledTaskRequest): Observable<ScheduledTask> {
    return this.http.post<ScheduledTask>(this.baseUrl, request);
  }

  update(id: string, request: SaveScheduledTaskRequest): Observable<ScheduledTask> {
    return this.http.put<ScheduledTask>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** The task's viewer grants, for the Manage Task Access page (admin only). */
  getAccess(id: string): Observable<ScheduledTaskAccess> {
    return this.http.get<ScheduledTaskAccess>(`${this.baseUrl}/${id}/access`);
  }

  /** Each of the three saves replaces that principal type's grants wholesale. */
  assignAccessRoles(id: string, request: AssignScheduledTaskRolesRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/access/roles`, request);
  }

  assignAccessUserGroups(id: string, request: AssignScheduledTaskUserGroupsRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/access/user-groups`, request);
  }

  assignAccessUsers(id: string, request: AssignScheduledTaskUsersRequest): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/${id}/access/users`, request);
  }

  /** Queues an immediate run; watch the task's run history for the outcome. */
  runNow(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/run`, {});
  }

  /** Cancels a run that is currently executing, aborting its running query (409 if not in progress). */
  cancelRun(taskId: string, runId: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${taskId}/runs/${runId}/cancel`, {});
  }
}
