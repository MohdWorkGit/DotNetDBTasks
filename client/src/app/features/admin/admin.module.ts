import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule } from '@angular/forms';
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

import { QueryListComponent } from './components/query-list/query-list.component';
import { QueryFormComponent } from './components/query-form/query-form.component';
import { RoleAssignmentComponent } from './components/role-assignment/role-assignment.component';
import { ExecutionLogsComponent } from './components/execution-logs/execution-logs.component';

@NgModule({
  declarations: [
    QueryListComponent,
    QueryFormComponent,
    RoleAssignmentComponent,
    ExecutionLogsComponent
  ],
  imports: [
    CommonModule,
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
    RouterModule.forChild([
      { path: 'queries', component: QueryListComponent },
      { path: 'queries/create', component: QueryFormComponent },
      { path: 'queries/edit/:id', component: QueryFormComponent },
      { path: 'queries/:id/roles', component: RoleAssignmentComponent },
      { path: 'logs', component: ExecutionLogsComponent },
      { path: '', redirectTo: 'queries', pathMatch: 'full' }
    ])
  ]
})
export class AdminModule {}
