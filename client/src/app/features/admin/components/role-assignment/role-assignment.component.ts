import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { FormBuilder, FormGroup } from '@angular/forms';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QueryService } from '@core/services/query.service';
import { DynamicQuery, Role } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-role-assignment',
  template: `
    <div class="container">
      <h2>Assign Roles to Query</h2>

      <mat-card *ngIf="query">
        <mat-card-header>
          <mat-card-title>{{ query.name }}</mat-card-title>
          <mat-card-subtitle>{{ query.description }}</mat-card-subtitle>
        </mat-card-header>

        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Assigned Roles</mat-label>
              <mat-select formControlName="roleIds" multiple>
                <mat-option *ngFor="let role of roles" [value]="role.id">
                  {{ role.name }} - {{ role.description }}
                </mat-option>
              </mat-select>
            </mat-form-field>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
              <button mat-raised-button color="primary" type="submit" [disabled]="saving">
                {{ saving ? 'Saving...' : 'Save Assignment' }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px; }
  `]
})
export class RoleAssignmentComponent implements OnInit {
  form!: FormGroup;
  query?: DynamicQuery;
  roles: Role[] = [];
  saving = false;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {
    this.form = this.fb.group({
      roleIds: [[]]
    });
  }

  ngOnInit(): void {
    const queryId = this.route.snapshot.params['id'];
    this.queryService.getQueryById(queryId).subscribe(q => {
      this.query = q;
      this.form.patchValue({
        roleIds: q.assignedRoles.map(r => r.roleId)
      });
    });
    this.queryService.getRoles().subscribe(r => this.roles = r);
  }

  onSubmit(): void {
    this.saving = true;
    const queryId = this.route.snapshot.params['id'];
    this.queryService.assignRoles(queryId, { roleIds: this.form.value.roleIds }).subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open('Roles assigned successfully', 'Close', { duration: 3000 });
        this.router.navigate(['/admin/queries']);
      },
      error: () => {
        this.saving = false;
        this.snackBar.open('Failed to assign roles', 'Close', { duration: 5000 });
      }
    });
  }
}
