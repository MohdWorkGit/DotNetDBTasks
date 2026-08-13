import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslocoModule } from '@jsverse/transloco';
import { FormsModule, ReactiveFormsModule } from '@angular/forms';
import { MatCheckboxModule } from '@angular/material/checkbox';
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
import { MatDatepickerModule } from '@angular/material/datepicker';
import { MatNativeDateModule } from '@angular/material/core';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBarModule } from '@angular/material/snack-bar';
import { MatChipsModule } from '@angular/material/chips';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatExpansionModule } from '@angular/material/expansion';
import { MatDialogModule } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';

import { MyQueriesComponent } from './components/my-queries/my-queries.component';
import { QueryExecuteComponent } from './components/query-execute/query-execute.component';
import { ExecutionHistoryComponent } from './components/execution-history/execution-history.component';
import { ScheduleStatusComponent } from './components/schedule-status/schedule-status.component';
import { queryAccessGuard } from '@core/guards/query-access.guard';

@NgModule({
  declarations: [
    MyQueriesComponent,
    QueryExecuteComponent,
    ExecutionHistoryComponent,
    ScheduleStatusComponent
  ],
  imports: [
    CommonModule,
    TranslocoModule,
    ReactiveFormsModule,
    FormsModule,
    MatCheckboxModule,
    MatTableModule,
    MatPaginatorModule,
    MatSortModule,
    MatCardModule,
    MatFormFieldModule,
    MatInputModule,
    MatButtonModule,
    MatIconModule,
    MatSelectModule,
    MatDatepickerModule,
    MatNativeDateModule,
    MatSlideToggleModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatChipsModule,
    MatTooltipModule,
    MatProgressBarModule,
    MatExpansionModule,
    MatDialogModule,
    MatMenuModule,
    // Schedules is deliberately ungated: being named a viewer on a scheduled task is a
    // per-task grant, not a query-running one, so an Access Manager can hold it. The other
    // three need a role that confers query access — see queryAccessGuard.
    RouterModule.forChild([
      { path: 'queries', component: MyQueriesComponent, canActivate: [queryAccessGuard] },
      { path: 'queries/:id/execute', component: QueryExecuteComponent, canActivate: [queryAccessGuard] },
      { path: 'history', component: ExecutionHistoryComponent, canActivate: [queryAccessGuard] },
      { path: 'schedules', component: ScheduleStatusComponent },
      { path: '', redirectTo: 'queries', pathMatch: 'full' }
    ])
  ]
})
export class UserModule {}
