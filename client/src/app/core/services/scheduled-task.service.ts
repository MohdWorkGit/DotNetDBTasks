import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '@env/environment';
import {
  SaveScheduledTaskRequest,
  ScheduledTask,
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

  create(request: SaveScheduledTaskRequest): Observable<ScheduledTask> {
    return this.http.post<ScheduledTask>(this.baseUrl, request);
  }

  update(id: string, request: SaveScheduledTaskRequest): Observable<ScheduledTask> {
    return this.http.put<ScheduledTask>(`${this.baseUrl}/${id}`, request);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Queues an immediate run; watch the task's run history for the outcome. */
  runNow(id: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/${id}/run`, {});
  }
}
