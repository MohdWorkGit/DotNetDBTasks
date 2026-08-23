import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { ReportService } from '@core/services/report.service';
import { QueryService } from '@core/services/query.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import { ConfirmService } from '@core/services/confirm.service';
import { DynamicQuery, ParameterType, QueryGroup } from '@core/models/dynamic-query.model';
import {
  Report,
  ReportChartType,
  ReportDatasetSourceType,
  ReportInput,
  ReportJoinType,
  ReportParameterSourceKind,
  ReportTemplateInspection,
  REPORT_EXPORT_FORMATS
} from '@core/models/report.model';

/**
 * The report builder.
 *
 * <p>Four tabs, in the order a report is actually designed: what it is, which queries feed it,
 * what the user is asked once, and the Word document that lays it out. The template tab is last
 * because the starter it offers is generated from the dataset keys defined on the second tab —
 * downloading it before those exist would hand the author an empty document.</p>
 */
@Component({
  selector: 'app-report-form',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1>{{ (isEdit ? 'admin.reports.editTitle' : 'admin.reports.createTitle') | transloco }}</h1>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <form [formGroup]="form" (ngSubmit)="save()" *ngIf="!loading">
        <mat-tab-group>
          <!-- ---------------------------------------------------------- details -->
          <mat-tab [label]="'admin.reports.tabDetails' | transloco">
            <mat-card class="tab-card">
              <mat-card-content>
                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>{{ 'admin.reports.name' | transloco }}</mat-label>
                  <input matInput formControlName="name" dir="auto" />
                </mat-form-field>

                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>{{ 'admin.reports.description' | transloco }}</mat-label>
                  <textarea matInput formControlName="description" rows="2" dir="auto"></textarea>
                </mat-form-field>

                <mat-form-field appearance="outline" class="full-width">
                  <mat-label>{{ 'admin.reports.queryGroup' | transloco }}</mat-label>
                  <mat-select formControlName="queryGroupId">
                    <mat-option [value]="null">{{ 'admin.reports.noGroup' | transloco }}</mat-option>
                    <mat-option *ngFor="let g of queryGroups" [value]="g.id">{{ g.name }}</mat-option>
                  </mat-select>
                  <mat-hint>{{ 'admin.reports.queryGroupHint' | transloco }}</mat-hint>
                </mat-form-field>

                <div class="row">
                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.reports.timeoutSeconds' | transloco }}</mat-label>
                    <input matInput type="number" formControlName="timeoutSeconds" min="1" />
                    <mat-hint>{{ 'admin.reports.timeoutHint' | transloco }}</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.reports.maxTotalRows' | transloco }}</mat-label>
                    <input matInput type="number" formControlName="maxTotalRows" min="0" />
                    <mat-hint>{{ 'admin.reports.maxTotalRowsHint' | transloco }}</mat-hint>
                  </mat-form-field>

                  <mat-form-field appearance="outline">
                    <mat-label>{{ 'admin.reports.maxDetailRows' | transloco }}</mat-label>
                    <input matInput type="number" formControlName="maxDetailRows" min="0" />
                    <mat-hint>{{ 'admin.reports.maxDetailRowsHint' | transloco }}</mat-hint>
                  </mat-form-field>
                </div>

                <mat-slide-toggle formControlName="isEnabled">
                  {{ 'admin.reports.enabled' | transloco }}
                </mat-slide-toggle>

                <h3>
                  {{ 'admin.reports.exportFormats' | transloco }}
                  <app-hint-icon [text]="'admin.reports.exportFormatsHint' | transloco"></app-hint-icon>
                </h3>
                <div class="chips">
                  <mat-checkbox *ngFor="let f of exportFormats"
                                [checked]="isFormatSelected(f.name)"
                                (change)="toggleFormat(f.name, $event.checked)">
                    {{ f.labelKey | transloco }}
                  </mat-checkbox>
                </div>
              </mat-card-content>
            </mat-card>
          </mat-tab>

          <!-- ---------------------------------------------------------- datasets -->
          <mat-tab [label]="'admin.reports.tabDatasets' | transloco">
            <mat-card class="tab-card">
              <mat-card-content>
                <p class="muted">{{ 'admin.reports.datasetsIntro' | transloco }}</p>

                <div formArrayName="datasets">
                  <mat-expansion-panel *ngFor="let ds of datasets.controls; let i = index"
                                       [formGroupName]="i" class="panel">
                    <mat-expansion-panel-header>
                      <mat-panel-title>
                        <span class="force-ltr">{{ '{{RESULTS:' + ds.get('datasetKey')?.value + '}}' }}</span>
                      </mat-panel-title>
                      <mat-panel-description dir="auto">
                        {{ ds.get('displayName')?.value }}
                      </mat-panel-description>
                    </mat-expansion-panel-header>

                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.datasetKey' | transloco }}</mat-label>
                        <input matInput formControlName="datasetKey" class="force-ltr" />
                        <mat-hint>{{ 'admin.reports.datasetKeyHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.datasetTitle' | transloco }}</mat-label>
                        <input matInput formControlName="displayName" dir="auto" />
                      </mat-form-field>
                    </div>

                    <mat-form-field appearance="outline" class="full-width"
                                    *ngIf="ds.get('sourceType')?.value !== sourceTypes.Join">
                      <mat-label>{{ 'admin.reports.datasetQuery' | transloco }}</mat-label>
                      <mat-select formControlName="dynamicQueryId"
                                  (selectionChange)="onQueryChange(i, $event.value)">
                        <mat-option *ngFor="let q of queries" [value]="q.id">{{ q.name }}</mat-option>
                      </mat-select>
                    </mat-form-field>

                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.datasetKind' | transloco }}</mat-label>
                        <mat-select formControlName="sourceType"
                                    (selectionChange)="onSourceTypeChange(i)">
                          <mat-option [value]="sourceTypes.Query">
                            {{ 'admin.reports.kindStandalone' | transloco }}
                          </mat-option>
                          <mat-option [value]="sourceTypes.Detail">
                            {{ 'admin.reports.kindDetail' | transloco }}
                          </mat-option>
                          <mat-option [value]="sourceTypes.Join">
                            {{ 'admin.reports.kindJoin' | transloco }}
                          </mat-option>
                        </mat-select>
                        <mat-hint>{{ 'admin.reports.datasetKindHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline"
                                      *ngIf="ds.get('sourceType')?.value === sourceTypes.Detail">
                        <mat-label>{{ 'admin.reports.parentDataset' | transloco }}</mat-label>
                        <mat-select formControlName="parentDatasetKey">
                          <mat-option *ngFor="let key of parentCandidates(i)" [value]="key">
                            {{ key }}
                          </mat-option>
                        </mat-select>
                      </mat-form-field>
                    </div>

                    <p class="warn-note" *ngIf="ds.get('sourceType')?.value === sourceTypes.Detail">
                      <mat-icon>warning</mat-icon>
                      <ng-container *ngIf="!detailRowsUnlimited">
                        {{ 'admin.reports.detailCostHint' | transloco: { count: maxDetailRows } }}
                      </ng-container>
                      <ng-container *ngIf="detailRowsUnlimited">
                        {{ 'admin.reports.detailCostHintUnlimited' | transloco }}
                      </ng-container>
                    </p>

                    <ng-container *ngIf="ds.get('sourceType')?.value === sourceTypes.Join">
                      <p class="muted">{{ 'admin.reports.joinIntro' | transloco }}</p>
                      <div class="row">
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.joinLeft' | transloco }}</mat-label>
                          <mat-select formControlName="leftDatasetKey">
                            <mat-option *ngFor="let key of joinCandidates(i)" [value]="key">{{ key }}</mat-option>
                          </mat-select>
                        </mat-form-field>

                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.joinLeftColumn' | transloco }}</mat-label>
                          <input matInput formControlName="leftColumn" class="force-ltr" />
                        </mat-form-field>

                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.joinKind' | transloco }}</mat-label>
                          <mat-select formControlName="joinType">
                            <mat-option [value]="joinTypes.Inner">
                              {{ 'admin.reports.joinInner' | transloco }}
                            </mat-option>
                            <mat-option [value]="joinTypes.Left">
                              {{ 'admin.reports.joinLeftOuter' | transloco }}
                            </mat-option>
                          </mat-select>
                        </mat-form-field>
                      </div>

                      <div class="row">
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.joinRight' | transloco }}</mat-label>
                          <mat-select formControlName="rightDatasetKey">
                            <mat-option *ngFor="let key of joinCandidates(i)" [value]="key">{{ key }}</mat-option>
                          </mat-select>
                        </mat-form-field>

                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.joinRightColumn' | transloco }}</mat-label>
                          <input matInput formControlName="rightColumn" class="force-ltr" />
                          <mat-hint>{{ 'admin.reports.joinColumnHint' | transloco }}</mat-hint>
                        </mat-form-field>
                      </div>
                    </ng-container>

                    <mat-slide-toggle formControlName="isVisibleInViewer">
                      {{ 'admin.reports.visibleInViewer' | transloco }}
                    </mat-slide-toggle>

                    <h4 *ngIf="mapsOf(i).length > 0">{{ 'admin.reports.parameterMaps' | transloco }}</h4>
                    <div formArrayName="parameterMaps">
                      <div *ngFor="let m of mapsOf(i).controls; let j = index" [formGroupName]="j"
                           class="row map-row">
                        <span class="target force-ltr">&#64;{{ m.get('targetParameterName')?.value }}</span>

                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.reports.mapSource' | transloco }}</mat-label>
                          <mat-select formControlName="sourceKind">
                            <mat-option [value]="sourceKinds.ReportParameter">
                              {{ 'admin.reports.sourceReportParameter' | transloco }}
                            </mat-option>
                            <mat-option [value]="sourceKinds.Constant">
                              {{ 'admin.reports.sourceConstant' | transloco }}
                            </mat-option>
                            <mat-option [value]="sourceKinds.ParentColumn"
                                        *ngIf="ds.get('sourceType')?.value === sourceTypes.Detail">
                              {{ 'admin.reports.sourceParentColumn' | transloco }}
                            </mat-option>
                          </mat-select>
                        </mat-form-field>

                        <mat-form-field appearance="outline"
                                        *ngIf="m.get('sourceKind')?.value === sourceKinds.ReportParameter">
                          <mat-label>{{ 'admin.reports.mapParameter' | transloco }}</mat-label>
                          <mat-select formControlName="reportParameterName">
                            <mat-option *ngFor="let p of parameters.controls"
                                        [value]="p.get('name')?.value">
                              {{ p.get('displayName')?.value || p.get('name')?.value }}
                            </mat-option>
                          </mat-select>
                        </mat-form-field>

                        <mat-form-field appearance="outline"
                                        *ngIf="m.get('sourceKind')?.value === sourceKinds.Constant">
                          <mat-label>{{ 'admin.reports.mapConstant' | transloco }}</mat-label>
                          <input matInput formControlName="constantValue" dir="auto" />
                        </mat-form-field>

                        <mat-form-field appearance="outline"
                                        *ngIf="m.get('sourceKind')?.value === sourceKinds.ParentColumn">
                          <mat-label>{{ 'admin.reports.mapParentColumn' | transloco }}</mat-label>
                          <input matInput formControlName="parentColumn" class="force-ltr" />
                          <mat-hint>{{ 'admin.reports.mapParentColumnHint' | transloco }}</mat-hint>
                        </mat-form-field>
                      </div>
                    </div>

                    <mat-action-row>
                      <button type="button" mat-button color="warn" (click)="removeDataset(i)">
                        <mat-icon>delete</mat-icon> {{ 'admin.reports.removeDataset' | transloco }}
                      </button>
                    </mat-action-row>
                  </mat-expansion-panel>
                </div>

                <button type="button" mat-stroked-button (click)="addDataset()">
                  <mat-icon>add</mat-icon> {{ 'admin.reports.addDataset' | transloco }}
                </button>
              </mat-card-content>
            </mat-card>
          </mat-tab>

          <!-- ---------------------------------------------------------- parameters -->
          <mat-tab [label]="'admin.reports.tabParameters' | transloco">
            <mat-card class="tab-card">
              <mat-card-content>
                <p class="muted">{{ 'admin.reports.parametersIntro' | transloco }}</p>

                <div formArrayName="parameters">
                  <mat-expansion-panel *ngFor="let p of parameters.controls; let i = index"
                                       [formGroupName]="i" class="panel">
                    <mat-expansion-panel-header>
                      <mat-panel-title class="force-ltr">{{ p.get('name')?.value }}</mat-panel-title>
                      <mat-panel-description dir="auto">
                        {{ p.get('displayName')?.value }}
                      </mat-panel-description>
                    </mat-expansion-panel-header>

                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.paramName' | transloco }}</mat-label>
                        <input matInput formControlName="name" class="force-ltr" />
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.paramDisplayName' | transloco }}</mat-label>
                        <input matInput formControlName="displayName" dir="auto" />
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.paramType' | transloco }}</mat-label>
                        <mat-select formControlName="parameterType">
                          <mat-option [value]="0">{{ 'admin.queryForm.typeString' | transloco }}</mat-option>
                          <mat-option [value]="1">{{ 'admin.queryForm.typeNumber' | transloco }}</mat-option>
                          <mat-option [value]="2">{{ 'admin.queryForm.typeDate' | transloco }}</mat-option>
                          <mat-option [value]="3">{{ 'admin.queryForm.typeBoolean' | transloco }}</mat-option>
                        </mat-select>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.paramDefault' | transloco }}</mat-label>
                        <input matInput formControlName="defaultValue" dir="auto" />
                      </mat-form-field>
                    </div>

                    <mat-checkbox formControlName="isRequired">
                      {{ 'admin.reports.paramRequired' | transloco }}
                    </mat-checkbox>

                    <mat-action-row>
                      <button type="button" mat-button color="warn" (click)="removeParameter(i)">
                        <mat-icon>delete</mat-icon> {{ 'admin.reports.removeParameter' | transloco }}
                      </button>
                    </mat-action-row>
                  </mat-expansion-panel>
                </div>

                <button type="button" mat-stroked-button (click)="addParameter()">
                  <mat-icon>add</mat-icon> {{ 'admin.reports.addParameter' | transloco }}
                </button>
              </mat-card-content>
            </mat-card>
          </mat-tab>

          <!-- ---------------------------------------------------------- charts -->
          <mat-tab [label]="'admin.reports.tabCharts' | transloco">
            <mat-card class="tab-card">
              <mat-card-content>
                <p class="muted">{{ 'admin.reports.chartsIntro' | transloco }}</p>

                <div formArrayName="charts">
                  <mat-expansion-panel *ngFor="let ch of charts.controls; let i = index"
                                       [formGroupName]="i" class="panel">
                    <mat-expansion-panel-header>
                      <mat-panel-title>
                        <span class="force-ltr">{{ '{{CHART:' + ch.get('chartKey')?.value + '}}' }}</span>
                      </mat-panel-title>
                      <mat-panel-description dir="auto">{{ ch.get('title')?.value }}</mat-panel-description>
                    </mat-expansion-panel-header>

                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartKey' | transloco }}</mat-label>
                        <input matInput formControlName="chartKey" class="force-ltr" />
                        <mat-hint>{{ 'admin.reports.datasetKeyHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartTitle' | transloco }}</mat-label>
                        <input matInput formControlName="title" dir="auto" />
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartType' | transloco }}</mat-label>
                        <mat-select formControlName="chartType">
                          <mat-option [value]="chartTypes.Column">
                            {{ 'admin.reports.typeColumn' | transloco }}
                          </mat-option>
                          <mat-option [value]="chartTypes.Bar">
                            {{ 'admin.reports.typeBar' | transloco }}
                          </mat-option>
                          <mat-option [value]="chartTypes.Line">
                            {{ 'admin.reports.typeLine' | transloco }}
                          </mat-option>
                          <mat-option [value]="chartTypes.Pie">
                            {{ 'admin.reports.typePie' | transloco }}
                          </mat-option>
                        </mat-select>
                      </mat-form-field>
                    </div>

                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartDataset' | transloco }}</mat-label>
                        <mat-select formControlName="datasetKey">
                          <mat-option *ngFor="let key of datasetKeys()" [value]="key">{{ key }}</mat-option>
                        </mat-select>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartCategory' | transloco }}</mat-label>
                        <input matInput formControlName="categoryColumn" dir="auto" />
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartSeries' | transloco }}</mat-label>
                        <input matInput formControlName="seriesColumns" dir="auto" />
                        <mat-hint>{{ 'admin.reports.chartSeriesHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.reports.chartMaxCategories' | transloco }}</mat-label>
                        <input matInput type="number" formControlName="maxCategories" min="1" />
                      </mat-form-field>
                    </div>

                    <mat-action-row>
                      <button type="button" mat-button color="warn" (click)="removeChart(i)">
                        <mat-icon>delete</mat-icon> {{ 'admin.reports.removeChart' | transloco }}
                      </button>
                    </mat-action-row>
                  </mat-expansion-panel>
                </div>

                <button type="button" mat-stroked-button (click)="addChart()">
                  <mat-icon>add</mat-icon> {{ 'admin.reports.addChart' | transloco }}
                </button>
              </mat-card-content>
            </mat-card>
          </mat-tab>

          <!-- ---------------------------------------------------------- template -->
          <mat-tab [label]="'admin.reports.tabTemplate' | transloco" [disabled]="!isEdit">
            <mat-card class="tab-card">
              <mat-card-content>
                <p class="muted">{{ 'admin.reports.templateIntro' | transloco }}</p>

                <div class="template-actions">
                  <button type="button" mat-stroked-button (click)="downloadStarter()">
                    <mat-icon>download</mat-icon>
                    {{ 'admin.reports.downloadStarter' | transloco }}
                  </button>
                  <button type="button" mat-stroked-button *ngIf="report?.hasTemplate"
                          (click)="downloadTemplate()">
                    <mat-icon>file_open</mat-icon>
                    {{ 'admin.reports.downloadTemplate' | transloco }}
                  </button>
                  <button type="button" mat-stroked-button (click)="fileInput.click()">
                    <mat-icon>upload</mat-icon>
                    {{ 'admin.reports.uploadTemplate' | transloco }}
                  </button>
                  <button type="button" mat-button color="warn" *ngIf="report?.hasTemplate"
                          (click)="removeTemplate()">
                    <mat-icon>delete</mat-icon>
                    {{ 'admin.reports.removeTemplate' | transloco }}
                  </button>
                  <input #fileInput type="file" accept=".docx" hidden
                         (change)="onTemplateSelected($event)" />
                </div>

                <p *ngIf="report?.templateFileName" class="force-ltr">
                  {{ report?.templateFileName }}
                </p>

                <div class="inspection" *ngIf="inspection">
                  <p *ngIf="inspection.matchedDatasetKeys.length > 0">
                    <mat-icon class="ok">check_circle</mat-icon>
                    {{ 'admin.reports.markersMatched' | transloco }}
                    <span class="force-ltr">{{ inspection.matchedDatasetKeys.join(', ') }}</span>
                  </p>
                  <p *ngIf="inspection.unknownDatasetKeys.length > 0" class="error-text">
                    <mat-icon>error</mat-icon>
                    {{ 'admin.reports.markersUnknown' | transloco }}
                    <span class="force-ltr">{{ inspection.unknownDatasetKeys.join(', ') }}</span>
                  </p>
                  <p *ngIf="inspection.datasetsWithoutMarker.length > 0" class="warn">
                    <mat-icon>warning</mat-icon>
                    {{ 'admin.reports.datasetsWithoutMarker' | transloco }}
                    <span class="force-ltr">{{ inspection.datasetsWithoutMarker.join(', ') }}</span>
                  </p>
                  <p *ngIf="inspection.duplicateKeysInOneTable.length > 0" class="error-text">
                    <mat-icon>error</mat-icon>
                    {{ 'admin.reports.markersDuplicated' | transloco }}
                    <span class="force-ltr">{{ inspection.duplicateKeysInOneTable.join(', ') }}</span>
                  </p>
                  <p *ngIf="inspection.hasPlaceholderInsideTextBox" class="warn">
                    <mat-icon>warning</mat-icon>
                    {{ 'admin.reports.placeholderInTextBox' | transloco }}
                  </p>
                </div>
              </mat-card-content>
            </mat-card>
          </mat-tab>
        </mat-tab-group>

        <div class="form-actions">
          <button type="button" mat-button (click)="cancel()">{{ 'common.cancel' | transloco }}</button>
          <button type="submit" mat-flat-button color="primary" [disabled]="saving">
            {{ 'common.save' | transloco }}
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    .tab-card { margin-block-start: 16px; }
    .row { display: flex; gap: 16px; flex-wrap: wrap; }
    /* A hinted full-width field directly above a row: its hint and the row's floating labels
       occupy the same band and overlap without this. */
    mat-form-field + .row { margin-block-start: 8px; }
    .row > mat-form-field { flex: 1 1 220px; }
    .panel { margin-block-end: 8px; }
    .map-row { align-items: center; }
    .map-row .target { min-inline-size: 140px; font-weight: 600; }
    .chips { display: flex; gap: 16px; flex-wrap: wrap; }
    .muted { color: var(--text-secondary); }
    .warn-note { display: flex; align-items: center; gap: 8px; color: var(--status-warning-text); }
    .template-actions { display: flex; gap: 8px; flex-wrap: wrap; margin-block: 12px; }
    .inspection p { display: flex; align-items: center; gap: 8px; }
    .inspection .ok { color: var(--status-success); }
    .inspection .warn { color: var(--status-warning-text); }
    .form-actions { display: flex; justify-content: flex-end; gap: 8px; margin-block-start: 16px; }
  `]
})
export class ReportFormComponent implements OnInit {
  private fb = inject(FormBuilder);
  private reports = inject(ReportService);
  private queryService = inject(QueryService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);
  private confirmService = inject(ConfirmService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  readonly exportFormats = REPORT_EXPORT_FORMATS;
  readonly sourceKinds = ReportParameterSourceKind;
  readonly sourceTypes = ReportDatasetSourceType;
  readonly joinTypes = ReportJoinType;
  readonly chartTypes = ReportChartType;

  form!: FormGroup;
  queries: DynamicQuery[] = [];
  queryGroups: QueryGroup[] = [];
  report: Report | null = null;
  inspection: ReportTemplateInspection | null = null;
  isEdit = false;
  loading = true;
  saving = false;

  private reportId: string | null = null;
  private selectedFormats = new Set<string>();

  get datasets(): FormArray { return this.form.get('datasets') as FormArray; }
  get parameters(): FormArray { return this.form.get('parameters') as FormArray; }
  get charts(): FormArray { return this.form.get('charts') as FormArray; }

  mapsOf(index: number): FormArray {
    return this.datasets.at(index).get('parameterMaps') as FormArray;
  }

  /**
   * The detail-row cap, shown in the warning about how many child runs a detail costs.
   * Read from the live control rather than the loaded report, so the warning tracks what the
   * author is typing instead of what was last saved.
   */
  get maxDetailRows(): number {
    return this.form.get('maxDetailRows')?.value ?? 100;
  }

  /** 0 means every parent row is expanded, which the warning has to say differently. */
  get detailRowsUnlimited(): boolean {
    return Number(this.form.get('maxDetailRows')?.value) === 0;
  }

  /** Datasets a join may read from: any other dataset in this report. */
  joinCandidates(index: number): string[] {
    return this.datasets.controls
      .filter((_, i) => i !== index)
      .map(control => control.get('datasetKey')?.value as string)
      .filter(key => !!key);
  }

  /**
   * Datasets this one may repeat under: any other dataset that is not itself a detail. Only one
   * level of nesting is supported, and the server rejects deeper chains, so the picker does not
   * offer them.
   */
  parentCandidates(index: number): string[] {
    return this.datasets.controls
      .filter((control, i) =>
        i !== index && control.get('sourceType')?.value !== ReportDatasetSourceType.Detail)
      .map(control => control.get('datasetKey')?.value as string)
      .filter(key => !!key);
  }

  /**
   * Switching a dataset to or from Detail changes which parameter sources make sense, so any
   * mapping whose source no longer applies is reset rather than silently saved as invalid.
   */
  onSourceTypeChange(index: number): void {
    const dataset = this.datasets.at(index);
    const isDetail = dataset.get('sourceType')?.value === ReportDatasetSourceType.Detail;

    const isJoin = dataset.get('sourceType')?.value === ReportDatasetSourceType.Join;

    if (!isJoin) {
      dataset.patchValue({
        leftDatasetKey: null, rightDatasetKey: null,
        joinType: null, leftColumn: '', rightColumn: ''
      });
    } else {
      // A join draws its rows from other datasets, so its own query picker is gone; clear the
      // query and the parameter maps that belonged to it rather than saving them unreachable.
      dataset.patchValue({ dynamicQueryId: null, joinType: dataset.get('joinType')?.value ?? ReportJoinType.Inner });
      this.mapsOf(index).clear();
    }

    if (!isDetail) {
      dataset.patchValue({ parentDatasetKey: null });
      for (const map of this.mapsOf(index).controls) {
        if (map.get('sourceKind')?.value === ReportParameterSourceKind.ParentColumn) {
          map.patchValue({ sourceKind: ReportParameterSourceKind.ReportParameter, parentColumn: '' });
        }
      }
    }
    this.cdr.detectChanges();
  }

  ngOnInit(): void {
    this.form = this.fb.group({
      name: ['', [Validators.required, Validators.maxLength(200)]],
      description: [''],
      isEnabled: [true],
      queryGroupId: [null],
      timeoutSeconds: [120, [Validators.required, Validators.min(1)]],
      // 0 means no limit on both. Only the sign is validated — how large either may be is the
      // administrator's decision, and the hints say what each one costs.
      maxTotalRows: [200000, [Validators.required, Validators.min(0)]],
      maxDetailRows: [100, [Validators.required, Validators.min(0)]],
      datasets: this.fb.array([]),
      parameters: this.fb.array([]),
      charts: this.fb.array([])
    });

    this.reportId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.reportId;

    this.queryService.getAllQueryGroups().subscribe({
      next: groups => {
        this.queryGroups = groups;
        this.cdr.detectChanges();
      },
      error: () => { /* the picker simply stays empty; the group is optional */ }
    });

    this.queryService.getAllQueries().subscribe({
      next: list => {
        this.queries = list;
        if (this.reportId) {
          this.loadReport(this.reportId);
        } else {
          this.loading = false;
          this.cdr.detectChanges();
        }
      },
      error: err => {
        this.toast.error(err, 'admin.reports.loadFailed');
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  private loadReport(id: string): void {
    this.reports.getById(id).subscribe({
      next: report => {
        this.report = report;
        this.selectedFormats = new Set(report.allowedExportFormats);
        this.form.patchValue({
          name: report.name,
          description: report.description,
          isEnabled: report.isEnabled,
          queryGroupId: report.queryGroupId ?? null,
          timeoutSeconds: report.timeoutSeconds,
          maxTotalRows: report.maxTotalRows,
          maxDetailRows: report.maxDetailRows
        });

        for (const parameter of report.parameters) {
          this.parameters.push(this.buildParameterGroup({
            name: parameter.name,
            displayName: parameter.displayName,
            parameterType: parameter.parameterType,
            isRequired: parameter.isRequired,
            defaultValue: parameter.defaultValue ?? ''
          }));
        }

        for (const chart of report.charts ?? []) {
          this.charts.push(this.buildChartGroup({
            chartKey: chart.chartKey,
            title: chart.title ?? '',
            chartType: chart.chartType,
            datasetKey: chart.datasetKey ?? '',
            categoryColumn: chart.categoryColumn,
            seriesColumns: (chart.seriesColumns ?? []).join(', '),
            maxCategories: chart.maxCategories
          }));
        }

        for (const dataset of report.datasets) {
          const group = this.buildDatasetGroup({
            datasetKey: dataset.datasetKey,
            displayName: dataset.displayName,
            dynamicQueryId: dataset.dynamicQueryId ?? null,
            isVisibleInViewer: dataset.isVisibleInViewer,
            sourceType: dataset.sourceType,
            parentDatasetKey: dataset.parentDatasetKey ?? null,
            leftDatasetKey: dataset.leftDatasetKey ?? null,
            rightDatasetKey: dataset.rightDatasetKey ?? null,
            joinType: dataset.joinType ?? null,
            leftColumn: dataset.leftColumn ?? '',
            rightColumn: dataset.rightColumn ?? ''
          });
          const maps = group.get('parameterMaps') as FormArray;
          for (const map of dataset.parameterMaps) {
            maps.push(this.buildMapGroup(
              map.targetParameterName,
              map.sourceKind,
              this.parameterNameById(report, map.reportParameterId),
              map.constantValue ?? '',
              map.parentColumn ?? ''
            ));
          }
          this.datasets.push(group);
        }

        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.toast.error(err, 'admin.reports.loadFailed');
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  private parameterNameById(report: Report, parameterId?: string | null): string {
    if (!parameterId) return '';
    return report.parameters.find(p => p.id === parameterId)?.name ?? '';
  }

  // ---------------------------------------------------------------- datasets

  addDataset(): void {
    this.datasets.push(this.buildDatasetGroup({
      datasetKey: `dataset${this.datasets.length + 1}`,
      displayName: '',
      dynamicQueryId: null,
      isVisibleInViewer: true,
      sourceType: ReportDatasetSourceType.Query,
      parentDatasetKey: null
    }));
  }

  removeDataset(index: number): void {
    this.datasets.removeAt(index);
  }

  /**
   * Rebuilds the dataset's parameter maps from the chosen query's declared parameters, so the
   * author sees exactly the parameters that query will be asked for — the same approach the
   * scheduled-task form takes when its query changes.
   */
  onQueryChange(index: number, queryId: string): void {
    const maps = this.mapsOf(index);
    maps.clear();

    const group = this.datasets.at(index);
    if (!group.get('displayName')?.value) {
      const query = this.queries.find(q => q.id === queryId);
      if (query) group.patchValue({ displayName: query.name });
    }

    this.queryService.getQueryById(queryId).subscribe({
      next: query => {
        for (const parameter of query.parameters ?? []) {
          maps.push(this.buildMapGroup(
            parameter.name, ReportParameterSourceKind.ReportParameter, '', '', ''));
        }
        this.cdr.detectChanges();
      },
      error: err => this.toast.error(err, 'admin.reports.loadFailed')
    });
  }

  // ---------------------------------------------------------------- parameters

  addParameter(): void {
    this.parameters.push(this.buildParameterGroup({
      name: `param${this.parameters.length + 1}`,
      displayName: '',
      parameterType: ParameterType.String,
      isRequired: false,
      defaultValue: ''
    }));
  }

  removeParameter(index: number): void {
    this.parameters.removeAt(index);
  }

  // ---------------------------------------------------------------- charts

  /** The datasets a chart may draw from — every dataset in this report. */
  datasetKeys(): string[] {
    return this.datasets.controls
      .map(control => control.get('datasetKey')?.value as string)
      .filter(key => !!key);
  }

  addChart(): void {
    this.charts.push(this.buildChartGroup({
      chartKey: `chart${this.charts.length + 1}`,
      title: '',
      chartType: ReportChartType.Column,
      datasetKey: this.datasetKeys()[0] ?? '',
      categoryColumn: '',
      seriesColumns: '',
      maxCategories: 25
    }));
  }

  removeChart(index: number): void {
    this.charts.removeAt(index);
  }

  // ---------------------------------------------------------------- formats

  isFormatSelected(name: string): boolean {
    return this.selectedFormats.has(name);
  }

  toggleFormat(name: string, checked: boolean): void {
    if (checked) {
      this.selectedFormats.add(name);
    } else {
      this.selectedFormats.delete(name);
    }
  }

  // ---------------------------------------------------------------- template

  downloadStarter(): void {
    if (!this.reportId) return;
    this.reports.downloadStarterTemplate(this.reportId).subscribe({
      next: blob => this.saveBlob(blob, `${this.form.value.name || 'report'}-template.docx`),
      error: err => this.toast.error(err, 'admin.reports.templateDownloadFailed')
    });
  }

  downloadTemplate(): void {
    if (!this.reportId) return;
    this.reports.downloadTemplate(this.reportId).subscribe({
      next: blob => this.saveBlob(blob, this.report?.templateFileName ?? 'template.docx'),
      error: err => this.toast.error(err, 'admin.reports.templateDownloadFailed')
    });
  }

  onTemplateSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    if (!file || !this.reportId) return;

    this.reports.uploadTemplate(this.reportId, file).subscribe({
      next: inspection => {
        this.inspection = inspection;
        if (this.report) {
          this.report.hasTemplate = true;
          this.report.templateFileName = inspection.templateFileName ?? file.name;
        }
        this.toast.success('admin.reports.templateUploaded');
        this.cdr.detectChanges();
      },
      error: err => this.toast.error(err, 'admin.reports.templateUploadFailed')
    });
  }

  removeTemplate(): void {
    if (!this.reportId) return;
    this.confirmService.askThen({
      titleKey: 'admin.reports.removeTemplateTitle',
      messageKey: 'admin.reports.removeTemplateMessage',
      confirmText: this.transloco.translate('common.delete'),
      destructive: true
    }, () => {
      this.reports.removeTemplate(this.reportId!).subscribe({
        next: () => {
          if (this.report) {
            this.report.hasTemplate = false;
            this.report.templateFileName = null;
          }
          this.inspection = null;
          this.toast.success('admin.reports.templateRemoved');
          this.cdr.detectChanges();
        },
        error: err => this.toast.error(err, 'admin.reports.templateRemoveFailed')
      });
    });
  }

  // ---------------------------------------------------------------- save

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toast.error(null, 'admin.reports.fixErrors');
      return;
    }

    const value = this.form.value;
    const input: ReportInput = {
      name: value.name,
      description: value.description,
      isEnabled: value.isEnabled,
      queryGroupId: value.queryGroupId,
      timeoutSeconds: value.timeoutSeconds,
      maxDetailRows: value.maxDetailRows,
      maxTotalRows: value.maxTotalRows,
      allowedExportFormats: [...this.selectedFormats],
      datasets: (value.datasets ?? []).map((d: any, index: number) => ({
        datasetKey: d.datasetKey,
        displayName: d.displayName || d.datasetKey,
        sourceType: d.sourceType,
        sortOrder: index,
        isVisibleInViewer: d.isVisibleInViewer,
        dynamicQueryId: d.dynamicQueryId,
        parentDatasetKey: d.sourceType === ReportDatasetSourceType.Detail ? d.parentDatasetKey : null,
        leftDatasetKey: d.sourceType === ReportDatasetSourceType.Join ? d.leftDatasetKey : null,
        rightDatasetKey: d.sourceType === ReportDatasetSourceType.Join ? d.rightDatasetKey : null,
        joinType: d.sourceType === ReportDatasetSourceType.Join ? d.joinType : null,
        leftColumn: d.sourceType === ReportDatasetSourceType.Join ? d.leftColumn : null,
        rightColumn: d.sourceType === ReportDatasetSourceType.Join ? d.rightColumn : null,
        parameterMaps: (d.parameterMaps ?? [])
          // A map with nothing chosen is left out entirely, so the query falls back to its own
          // default rather than being bound to an empty value.
          .filter((m: any) =>
            (m.sourceKind === ReportParameterSourceKind.ReportParameter && m.reportParameterName) ||
            (m.sourceKind === ReportParameterSourceKind.Constant && m.constantValue) ||
            (m.sourceKind === ReportParameterSourceKind.ParentColumn && m.parentColumn))
          .map((m: any) => ({
            targetParameterName: m.targetParameterName,
            sourceKind: m.sourceKind,
            reportParameterName: m.reportParameterName || null,
            constantValue: m.constantValue || null,
            parentColumn: m.parentColumn || null
          }))
      })),
      charts: (value.charts ?? []).map((c: any, index: number) => ({
        chartKey: c.chartKey,
        title: c.title || null,
        chartType: c.chartType,
        datasetKey: c.datasetKey,
        categoryColumn: c.categoryColumn,
        seriesColumns: String(c.seriesColumns || '')
          .split(',')
          .map((x: string) => x.trim())
          .filter((x: string) => !!x),
        maxCategories: c.maxCategories,
        sortOrder: index
      })),
      parameters: (value.parameters ?? []).map((p: any, index: number) => ({
        name: p.name,
        displayName: p.displayName || p.name,
        parameterType: p.parameterType,
        isRequired: p.isRequired,
        defaultValue: p.defaultValue || null,
        sortOrder: index,
        allowMultiple: false,
        dropdownSourceType: null,
        dropdownStaticValues: null,
        dropdownQueryId: null,
        dropdownQueryValueColumn: null,
        dropdownQueryLabelColumn: null
      }))
    };

    this.saving = true;
    const request$ = this.reportId
      ? this.reports.update(this.reportId, input)
      : this.reports.create(input);

    request$.subscribe({
      next: saved => {
        this.saving = false;
        this.toast.success(this.reportId ? 'admin.reports.updated' : 'admin.reports.created');
        // A new report goes to its edit route so the template tab — which needs an id — opens.
        this.router.navigate(['/admin/reports/edit', saved.id]);
      },
      error: err => {
        this.saving = false;
        this.toast.error(err, 'admin.reports.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  cancel(): void {
    this.router.navigate(['/admin/reports']);
  }

  // ---------------------------------------------------------------- builders

  private buildDatasetGroup(value: {
    datasetKey: string; displayName: string;
    dynamicQueryId: string | null; isVisibleInViewer: boolean;
    sourceType?: ReportDatasetSourceType; parentDatasetKey?: string | null;
    leftDatasetKey?: string | null; rightDatasetKey?: string | null;
    joinType?: ReportJoinType | null; leftColumn?: string | null; rightColumn?: string | null;
  }): FormGroup {
    return this.fb.group({
      datasetKey: [value.datasetKey, [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]*$/)]],
      displayName: [value.displayName],
      // Not Validators.required: a join has no query of its own, and the server enforces the
      // rule per source type anyway.
      dynamicQueryId: [value.dynamicQueryId],
      isVisibleInViewer: [value.isVisibleInViewer],
      sourceType: [value.sourceType ?? ReportDatasetSourceType.Query],
      parentDatasetKey: [value.parentDatasetKey ?? null],
      leftDatasetKey: [value.leftDatasetKey ?? null],
      rightDatasetKey: [value.rightDatasetKey ?? null],
      joinType: [value.joinType ?? null],
      leftColumn: [value.leftColumn ?? ''],
      rightColumn: [value.rightColumn ?? ''],
      parameterMaps: this.fb.array([])
    });
  }

  private buildMapGroup(
    targetParameterName: string,
    sourceKind: ReportParameterSourceKind,
    reportParameterName: string,
    constantValue: string,
    parentColumn = ''
  ): FormGroup {
    return this.fb.group({
      targetParameterName: [targetParameterName],
      sourceKind: [sourceKind],
      reportParameterName: [reportParameterName],
      constantValue: [constantValue],
      parentColumn: [parentColumn]
    });
  }

  private buildChartGroup(value: {
    chartKey: string; title: string; chartType: ReportChartType; datasetKey: string;
    categoryColumn: string; seriesColumns: string; maxCategories: number;
  }): FormGroup {
    return this.fb.group({
      // Charts and datasets share one key namespace, so {{RESULTS:x}} and {{CHART:x}} can never
      // mean two different things. The server rejects a collision; this keeps the shape right.
      chartKey: [value.chartKey, [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]*$/)]],
      title: [value.title],
      chartType: [value.chartType],
      datasetKey: [value.datasetKey, Validators.required],
      categoryColumn: [value.categoryColumn, Validators.required],
      // Edited as one comma-separated field and split on save: it is an ordered short list,
      // not something worth a nested FormArray.
      seriesColumns: [value.seriesColumns, Validators.required],
      maxCategories: [value.maxCategories, [Validators.required, Validators.min(1)]]
    });
  }

  private buildParameterGroup(value: {
    name: string; displayName: string; parameterType: ParameterType;
    isRequired: boolean; defaultValue: string;
  }): FormGroup {
    return this.fb.group({
      name: [value.name, [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]*$/)]],
      displayName: [value.displayName],
      parameterType: [value.parameterType],
      isRequired: [value.isRequired],
      defaultValue: [value.defaultValue]
    });
  }

  private saveBlob(blob: Blob, fileName: string): void {
    const url = window.URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    window.URL.revokeObjectURL(url);
  }
}
