import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { MatTableModule } from '@angular/material/table';
import { MatPaginatorModule } from '@angular/material/paginator';
import { MatSortModule } from '@angular/material/sort';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatDialogModule } from '@angular/material/dialog';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatRadioModule } from '@angular/material/radio';

import { QueryListComponent } from './components/query-list/query-list.component';
import { QueryFormComponent } from './components/query-form/query-form.component';
import { RoleAssignmentComponent } from './components/role-assignment/role-assignment.component';
import { ExecutionLogsComponent } from './components/execution-logs/execution-logs.component';
import { AdUsersComponent } from './components/ad-users/ad-users.component';
import { UserManagementComponent } from './components/user-management/user-management.component';
import { DatabaseUsersComponent } from './components/database-users/database-users.component';
import { QueryGroupsListComponent } from './components/query-groups/query-groups-list.component';
import { QueryGroupFormComponent } from './components/query-groups/query-group-form.component';
import { QueryGroupAccessComponent } from './components/query-groups/query-group-access.component';
import { ScheduledTasksListComponent } from './components/scheduled-tasks/scheduled-tasks-list.component';
import { ScheduledTaskFormComponent } from './components/scheduled-tasks/scheduled-task-form.component';
import { ScheduledTaskRunsComponent } from './components/scheduled-tasks/scheduled-task-runs.component';
import { MatMenuModule } from '@angular/material/menu';
import { MatExpansionModule } from '@angular/material/expansion';
import { authGuard } from '@core/guards/auth.guard';
import { ADMIN, AUDITOR, ACCESS_MANAGER } from '@core/models/roles';

@NgModule({
  declarations: [
    QueryListComponent,
    QueryFormComponent,
    RoleAssignmentComponent,
    ExecutionLogsComponent,
    AdUsersComponent,
    UserManagementComponent,
    DatabaseUsersComponent,
    QueryGroupsListComponent,
    QueryGroupFormComponent,
    QueryGroupAccessComponent,
    ScheduledTasksListComponent,
    ScheduledTaskFormComponent,
    ScheduledTaskRunsComponent
  ],
  imports: [
    CommonModule,
    TranslocoModule,
    FormsModule,
    ReactiveFormsModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatChipsModule,
    MatDialogModule,
    MatTooltipModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatTabsModule,
    MatCheckboxModule,
    MatRadioModule,
    MatMenuModule,
    MatExpansionModule,
    // Every route carries its own roles: the parent /admin guard only checks that the
    // user has *some* admin page, so without these an Auditor could type their way into
    // the query editor. These mirror the [Authorize] attributes on the API controllers —
    // the server is the real gate; this keeps the UI from offering a guaranteed 403.
    RouterModule.forChild([
      { path: 'queries', component: QueryListComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, ACCESS_MANAGER] } },
      { path: 'queries/create', component: QueryFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'queries/edit/:id', component: QueryFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'queries/:id/roles', component: RoleAssignmentComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, ACCESS_MANAGER] } },
      { path: 'query-groups', component: QueryGroupsListComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, ACCESS_MANAGER] } },
      { path: 'query-groups/create', component: QueryGroupFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'query-groups/edit/:id', component: QueryGroupFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'query-groups/:id/access', component: QueryGroupAccessComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, ACCESS_MANAGER] } },
      { path: 'scheduled-tasks', component: ScheduledTasksListComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, AUDITOR] } },
      { path: 'scheduled-tasks/create', component: ScheduledTaskFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'scheduled-tasks/edit/:id', component: ScheduledTaskFormComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'scheduled-tasks/:id/runs', component: ScheduledTaskRunsComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, AUDITOR] } },
      { path: 'logs', component: ExecutionLogsComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, AUDITOR] } },
      { path: 'ad-users', component: AdUsersComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: 'users', component: UserManagementComponent,
        canActivate: [authGuard], data: { roles: [ADMIN, ACCESS_MANAGER] } },
      { path: 'database-users', component: DatabaseUsersComponent,
        canActivate: [authGuard], data: { roles: [ADMIN] } },
      { path: '', redirectTo: 'queries', pathMatch: 'full' }
    ])
  ]
})
export class AdminModule {}
