import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { ToastService } from '@core/services/toast.service';
import { timeout, catchError } from 'rxjs/operators';
import { throwError } from 'rxjs';
import { QueryService } from '@core/services/query.service';

@Component({
  standalone: false,
  selector: 'app-query-group-form',
  template: `
    <div class="container">
      <h2>{{ isEdit ? 'Edit' : 'Create' }} Query Group</h2>

      <mat-card>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.groups.name' | transloco }}</mat-label>
              <input matInput formControlName="name" maxlength="200">
              <mat-error *ngIf="form.get('name')?.hasError('required')">Name is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>{{ 'admin.groups.description' | transloco }}</mat-label>
              <textarea matInput formControlName="description" rows="3" maxlength="1000"></textarea>
              <mat-error *ngIf="form.get('description')?.hasError('required')">Description is required</mat-error>
            </mat-form-field>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/query-groups">{{ 'common.cancel' | transloco }}</button>
              <button mat-raised-button color="primary" type="submit"
                      [disabled]="form.invalid || saving">
                {{ saving ? 'Saving...' : (isEdit ? 'Update' : 'Create') }}
              </button>
            </div>
          </form>
        </mat-card-content>
      </mat-card>
    </div>
  `,
  styles: [`
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 16px; }
    .full-width { width: 100%; }
  `]
})
export class QueryGroupFormComponent implements OnInit {
  form!: FormGroup;
  isEdit = false;
  groupId?: string;
  saving = false;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private toast: ToastService,
    private cdr: ChangeDetectorRef
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.required, Validators.maxLength(1000)]]
    });

    this.groupId = this.route.snapshot.params['id'];
    if (this.groupId) {
      this.isEdit = true;
      this.queryService.getQueryGroupById(this.groupId).subscribe({
        next: (g) => {
          this.form.patchValue({ name: g.name, description: g.description });
          this.cdr.detectChanges();
        },
        error: (err) => {
          this.toast.error(err, 'admin.groups.loadOneFailed');
        }
      });
    }
  }

  onSubmit(): void {
    if (this.form.invalid) return;
    this.saving = true;
    const value = this.form.value;

    const request$ = this.isEdit
      ? this.queryService.updateQueryGroup(this.groupId!, { ...value, id: this.groupId })
      : this.queryService.createQueryGroup(value);

    request$.pipe(
      timeout(30000),
      catchError(err => {
        if (err.name === 'TimeoutError') {
          return throwError(() => ({ error: { message: 'Request timed out.' } }));
        }
        return throwError(() => err);
      })
    ).subscribe({
      next: () => {
        this.saving = false;
        this.toast.success(this.isEdit ? 'admin.groups.updated' : 'admin.groups.created');
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        this.toast.error(err, 'common.operationFailed');
      }
    });
  }
}
