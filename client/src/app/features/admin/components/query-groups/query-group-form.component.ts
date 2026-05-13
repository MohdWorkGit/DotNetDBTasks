import { Component, OnInit, ChangeDetectorRef } from '@angular/core';
import { FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
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
              <mat-label>Name</mat-label>
              <input matInput formControlName="name" maxlength="200">
              <mat-error *ngIf="form.get('name')?.hasError('required')">Name is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Description</mat-label>
              <textarea matInput formControlName="description" rows="3" maxlength="1000"></textarea>
              <mat-error *ngIf="form.get('description')?.hasError('required')">Description is required</mat-error>
            </mat-form-field>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/query-groups">Cancel</button>
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
    private snackBar: MatSnackBar,
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
        error: () => {
          this.snackBar.open('Failed to load group', 'Close', { duration: 5000 });
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
        this.snackBar.open(
          `Group ${this.isEdit ? 'updated' : 'created'} successfully`,
          'Close', { duration: 3000 }
        );
        this.router.navigate(['/admin/query-groups']);
      },
      error: (err) => {
        this.saving = false;
        const msg = err.error?.message || 'Operation failed';
        this.snackBar.open(msg, 'Close', { duration: 5000 });
      }
    });
  }
}
