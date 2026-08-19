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
import { UserGroupsListComponent } from './components/user-groups/user-groups-list.component';
import { UserGroupFormComponent } from './components/user-groups/user-group-form.component';
import { DatabaseUsersComponent } from './components/database-users/database-users.component';
import { QueryGroupsListComponent } from './components/query-groups/query-groups-list.component';
import { QueryGroupFormComponent } from './components/query-groups/query-group-form.component';
import { QueryGroupAccessComponent } from './components/query-groups/query-group-access.component';
import { ScheduledTasksListComponent } from './components/scheduled-tasks/scheduled-tasks-list.component';
import { ScheduledTaskFormComponent } from './components/scheduled-tasks/scheduled-task-form.component';
import { ScheduledTaskRunsComponent } from './components/scheduled-tasks/scheduled-task-runs.component';
import { ScheduledTaskAccessComponent } from './components/scheduled-tasks/scheduled-task-access.component';
import { SystemAuditComponent } from './components/system-audit/system-audit.component';
import { SystemSettingsComponent } from './components/system-settings/system-settings.component';
import { PermissionsMatrixComponent } from './components/system-settings/permissions-matrix.component';
import { MatMenuModule } from '@angular/material/menu';
import { MatExpansionModule } from '@angular/material/expansion';
import { HintIconComponent } from '@shared/components/hint-icon.component';
import { authGuard } from '@core/guards/auth.guard';
import { PERM } from '@core/models/permissions';

@NgModule({
  declarations: [
    QueryListComponent,
    QueryFormComponent,
    RoleAssignmentComponent,
    ExecutionLogsComponent,
    AdUsersComponent,
    UserManagementComponent,
    UserGroupsListComponent,
    UserGroupFormComponent,
    DatabaseUsersComponent,
    QueryGroupsListComponent,
    QueryGroupFormComponent,
    QueryGroupAccessComponent,
    ScheduledTasksListComponent,
    ScheduledTaskFormComponent,
    ScheduledTaskRunsComponent,
    ScheduledTaskAccessComponent,
    SystemAuditComponent,
    SystemSettingsComponent,
    PermissionsMatrixComponent
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
    HintIconComponent,
    // Every route carries its own roles: the parent /admin guard only checks that the
    // user has *some* admin page, so without these an Auditor could type their way into
    // the query editor. These mirror the [Authorize] attributes on the API controllers —
    // the server is the real gate; this keeps the UI from offering a guaranteed 403.
    RouterModule.forChild([
      { path: 'queries', component: QueryListComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queriesView] } },
      { path: 'queries/create', component: QueryFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queriesManage] } },
      { path: 'queries/edit/:id', component: QueryFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queriesManage] } },
      { path: 'queries/:id/roles', component: RoleAssignmentComponent,
        canActivate: [authGuard], data: { permissions: [PERM.accessManageQuery] } },
      { path: 'query-groups', component: QueryGroupsListComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queriesView] } },
      { path: 'query-groups/create', component: QueryGroupFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queryGroupsManage] } },
      { path: 'query-groups/edit/:id', component: QueryGroupFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.queryGroupsManage] } },
      { path: 'query-groups/:id/access', component: QueryGroupAccessComponent,
        canActivate: [authGuard], data: { permissions: [PERM.accessManageGroup] } },
      { path: 'scheduled-tasks', component: ScheduledTasksListComponent,
        canActivate: [authGuard], data: { permissions: [PERM.scheduledTasksViewAll, PERM.scheduledTasksManage] } },
      { path: 'scheduled-tasks/create', component: ScheduledTaskFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.scheduledTasksManage] } },
      { path: 'scheduled-tasks/edit/:id', component: ScheduledTaskFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.scheduledTasksManage] } },
      { path: 'scheduled-tasks/:id/runs', component: ScheduledTaskRunsComponent,
        canActivate: [authGuard], data: { permissions: [PERM.scheduledTasksViewAll, PERM.scheduledTasksManage] } },
      { path: 'scheduled-tasks/:id/access', component: ScheduledTaskAccessComponent,
        canActivate: [authGuard], data: { permissions: [PERM.scheduledTasksManage] } },
      { path: 'logs', component: ExecutionLogsComponent,
        canActivate: [authGuard], data: { permissions: [PERM.logsView] } },
      { path: 'system-audit', component: SystemAuditComponent,
        canActivate: [authGuard], data: { permissions: [PERM.auditView] } },
      { path: 'settings', component: SystemSettingsComponent,
        canActivate: [authGuard], data: { permissions: [PERM.settingsManage] } },
      // Roles live under Users & Access rather than inside Settings: the question they
      // answer is who the system answers to, which belongs with the people pages.
      { path: 'roles', component: PermissionsMatrixComponent,
        canActivate: [authGuard], data: { permissions: [PERM.rolesManage] } },
      { path: 'ad-users', component: AdUsersComponent,
        canActivate: [authGuard], data: { permissions: [PERM.directoryManage] } },
      { path: 'users', component: UserManagementComponent,
        canActivate: [authGuard], data: { permissions: [PERM.usersView] } },
      { path: 'user-groups', component: UserGroupsListComponent,
        canActivate: [authGuard], data: { permissions: [PERM.userGroupsView] } },
      { path: 'user-groups/create', component: UserGroupFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.userGroupsManage] } },
      { path: 'user-groups/edit/:id', component: UserGroupFormComponent,
        canActivate: [authGuard], data: { permissions: [PERM.userGroupsManage] } },
      { path: 'database-users', component: DatabaseUsersComponent,
        canActivate: [authGuard], data: { permissions: [PERM.databaseUsersManage] } },
      { path: '', redirectTo: 'queries', pathMatch: 'full' }
    ])
  ]
})
export class AdminModule {}
