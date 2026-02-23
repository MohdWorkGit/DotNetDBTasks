import { Component, OnInit } from '@angular/core';
import { FormBuilder, FormGroup, FormArray, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { MatSnackBar } from '@angular/material/snack-bar';
import { QueryService } from '@core/services/query.service';
import { ParameterType } from '@core/models/dynamic-query.model';

@Component({
  selector: 'app-query-form',
  template: `
    <div class="container">
      <h2>{{ isEdit ? 'Edit' : 'Create' }} Dynamic Query</h2>

      <mat-card>
        <mat-card-content>
          <form [formGroup]="form" (ngSubmit)="onSubmit()">
            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Name</mat-label>
              <input matInput formControlName="name">
              <mat-error *ngIf="form.get('name')?.hasError('required')">Name is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>Description</mat-label>
              <textarea matInput formControlName="description" rows="3"></textarea>
              <mat-error *ngIf="form.get('description')?.hasError('required')">Description is required</mat-error>
            </mat-form-field>

            <mat-form-field class="full-width" appearance="outline">
              <mat-label>SQL Query (parameterized)</mat-label>
              <textarea matInput formControlName="sqlQuery" rows="5"
                        placeholder="SELECT * FROM Users WHERE CreatedAt >= &#64;StartDate"></textarea>
              <mat-hint>Use &#64;paramName syntax for parameters. Supports SELECT, INSERT, UPDATE, DELETE, and MERGE queries.</mat-hint>
              <mat-error *ngIf="form.get('sqlQuery')?.hasError('required')">SQL query is required</mat-error>
            </mat-form-field>

            <mat-form-field appearance="outline">
              <mat-label>Timeout (seconds)</mat-label>
              <input matInput type="number" formControlName="timeoutSeconds">
            </mat-form-field>

            <mat-slide-toggle *ngIf="isEdit" formControlName="isEnabled" class="toggle">
              Enabled
            </mat-slide-toggle>

            <h3>Parameters</h3>
            <div formArrayName="parameters">
              <mat-card *ngFor="let param of parameters.controls; let i = index"
                        [formGroupName]="i" class="param-card">
                <div class="param-row">
                  <mat-form-field appearance="outline">
                    <mat-label>Name</mat-label>
                    <input matInput formControlName="name" placeholder="paramName">
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Display Name</mat-label>
                    <input matInput formControlName="displayName" placeholder="Parameter Label">
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>Type</mat-label>
                    <mat-select formControlName="parameterType">
                      <mat-option [value]="0">String</mat-option>
                      <mat-option [value]="1">Number</mat-option>
                      <mat-option [value]="2">Date</mat-option>
                      <mat-option [value]="3">Boolean</mat-option>
                    </mat-select>
                  </mat-form-field>

                  <mat-slide-toggle formControlName="isRequired">Required</mat-slide-toggle>

                  <mat-form-field appearance="outline">
                    <mat-label>Default Value</mat-label>
                    <input matInput formControlName="defaultValue">
                  </mat-form-field>

                  <button mat-icon-button color="warn" type="button" (click)="removeParameter(i)">
                    <mat-icon>delete</mat-icon>
                  </button>
                </div>
              </mat-card>
            </div>

            <button mat-stroked-button type="button" (click)="addParameter()" class="add-btn">
              <mat-icon>add</mat-icon> Add Parameter
            </button>

            <div class="actions">
              <button mat-button type="button" routerLink="/admin/queries">Cancel</button>
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
    mat-form-field { margin-right: 16px; }
    .param-card { margin-bottom: 12px; padding: 12px; }
    .param-row { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-top: 24px; }
    .add-btn { margin: 16px 0; }
    .toggle { margin: 16px 0; display: block; }
  `]
})
export class QueryFormComponent implements OnInit {
  form!: FormGroup;
  isEdit = false;
  queryId?: string;
  saving = false;

  constructor(
    private fb: FormBuilder,
    private queryService: QueryService,
    private route: ActivatedRoute,
    private router: Router,
    private snackBar: MatSnackBar
  ) {}

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: ['', [Validators.required, Validators.maxLength(1000)]],
      sqlQuery: ['', [Validators.required, Validators.maxLength(4000)]],
      timeoutSeconds: [30, [Validators.min(1), Validators.max(120)]],
      isEnabled: [true],
      parameters: this.fb.array([])
    });

    this.queryId = this.route.snapshot.params['id'];
    if (this.queryId) {
      this.isEdit = true;
      this.loadQuery(this.queryId);
    }
  }

  get parameters(): FormArray {
    return this.form.get('parameters') as FormArray;
  }

  addParameter(): void {
    this.parameters.push(this.fb.group({
      name: ['', Validators.required],
      displayName: ['', Validators.required],
      parameterType: [ParameterType.String],
      isRequired: [true],
      defaultValue: [''],
      sortOrder: [this.parameters.length]
    }));
  }

  removeParameter(index: number): void {
    this.parameters.removeAt(index);
  }

  loadQuery(id: string): void {
    this.queryService.getQueryById(id).subscribe({
      next: (query) => {
        this.form.patchValue({
          name: query.name,
          description: query.description,
          sqlQuery: query.sqlQuery,
          timeoutSeconds: query.timeoutSeconds,
          isEnabled: query.isEnabled
        });

        query.parameters.forEach(p => {
          this.parameters.push(this.fb.group({
            name: [p.name, Validators.required],
            displayName: [p.displayName, Validators.required],
            parameterType: [p.parameterType],
            isRequired: [p.isRequired],
            defaultValue: [p.defaultValue || ''],
            sortOrder: [p.sortOrder]
          }));
        });
      },
      error: () => {
        this.snackBar.open('Failed to load query', 'Close', { duration: 5000 });
      }
    });
  }

  onSubmit(): void {
    if (this.form.invalid) return;

    this.saving = true;
    const value = this.form.value;

    const request$ = this.isEdit
      ? this.queryService.updateQuery(this.queryId!, { ...value, id: this.queryId })
      : this.queryService.createQuery(value);

    request$.subscribe({
      next: () => {
        this.saving = false;
        this.snackBar.open(
          `Query ${this.isEdit ? 'updated' : 'created'} successfully`,
          'Close', { duration: 3000 }
        );
        this.router.navigate(['/admin/queries']);
      },
      error: (err) => {
        this.saving = false;
        const msg = err.error?.message || err.error?.errors
          ? Object.values(err.error.errors).flat().join(', ')
          : 'Operation failed';
        this.snackBar.open(msg, 'Close', { duration: 5000 });
      }
    });
  }
}
