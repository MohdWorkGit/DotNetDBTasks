import { Component, OnInit, ChangeDetectorRef, inject } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  AbstractControl, FormArray, FormBuilder, FormGroup, Validators
} from '@angular/forms';
import { CdkDragDrop, moveItemInArray } from '@angular/cdk/drag-drop';
import { forkJoin, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { TranslocoService } from '@jsverse/transloco';
import { DashboardService } from '@core/services/dashboard.service';
import { QueryService } from '@core/services/query.service';
import { ReportService } from '@core/services/report.service';
import { ToastService, extractApiError } from '@core/services/toast.service';
import {
  DynamicQuery, DropdownSourceType, ParameterType, QueryType
} from '@core/models/dynamic-query.model';
import { ReportSummary } from '@core/models/report.model';
import {
  Dashboard, DashboardConditionalRule, DashboardDrillAction, DashboardFilter, DashboardInput,
  DashboardKpiAggregate, DashboardTile, DashboardTileParameterSource, DashboardValueFormat,
  DashboardVisualType, DASHBOARD_MAX_REFRESH_SECONDS, DASHBOARD_MIN_REFRESH_SECONDS,
  DASHBOARD_TILE_WIDTHS
} from '@core/models/dashboard.model';
import { DATE_PRESETS } from '@core/models/date-presets';

/** A map row's source in the form: '' means "not mapped — the query's own default applies". */
type MapSource = '' | DashboardTileParameterSource;

/**
 * Builds a dashboard: its details, the filter bar, and the tiles.
 *
 * <p>Saved as one graph, the way a report is: tiles name filters, so the whole design goes to the
 * server together and is validated together. Each tile's parameter mapping is generated from the
 * chosen query's declared parameters, so the author picks a source per parameter rather than
 * typing names that might not exist.</p>
 */
@Component({
  selector: 'app-dashboard-form',
  standalone: false,
  template: `
    <div class="container">
      <div class="page-header">
        <h1>{{ (isEdit ? 'admin.dashboards.editTitle' : 'admin.dashboards.createTitle') | transloco }}</h1>
        <span class="spacer"></span>
        <button mat-stroked-button type="button" *ngIf="isEdit" (click)="openViewer()">
          <mat-icon>open_in_new</mat-icon> {{ 'admin.dashboards.open' | transloco }}
        </button>
      </div>

      <div class="loading" *ngIf="loading">
        <mat-progress-spinner mode="indeterminate" diameter="40"></mat-progress-spinner>
      </div>

      <form *ngIf="!loading" [formGroup]="form" (ngSubmit)="save()">
        <mat-tab-group>
          <!-- ------------------------------------------------------------ details -->
          <mat-tab [label]="'admin.dashboards.tabDetails' | transloco">
            <div class="tab-body">
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'admin.dashboards.name' | transloco }}</mat-label>
                <input matInput formControlName="name" dir="auto" required />
              </mat-form-field>
              <mat-form-field appearance="outline" class="full-width">
                <mat-label>{{ 'admin.dashboards.description' | transloco }}</mat-label>
                <textarea matInput formControlName="description" rows="2" dir="auto"></textarea>
              </mat-form-field>
              <div class="row">
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'admin.dashboards.defaultRefresh' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="defaultRefreshSeconds"
                         [min]="minRefresh" [max]="maxRefresh" />
                  <mat-hint>{{ 'admin.dashboards.defaultRefreshHint' | transloco: { min: minRefresh } }}</mat-hint>
                </mat-form-field>
                <mat-form-field appearance="outline">
                  <mat-label>{{ 'admin.dashboards.sortOrder' | transloco }}</mat-label>
                  <input matInput type="number" formControlName="sortOrder" />
                </mat-form-field>
              </div>
              <mat-slide-toggle formControlName="isEnabled">
                {{ 'admin.dashboards.enabled' | transloco }}
              </mat-slide-toggle>
            </div>
          </mat-tab>

          <!-- ------------------------------------------------------------ filters -->
          <mat-tab [label]="('admin.dashboards.tabFilters' | transloco) + ' (' + filters.length + ')'">
            <div class="tab-body">
              <p class="muted">{{ 'admin.dashboards.filtersIntro' | transloco }}</p>

              <div formArrayName="filters">
                <mat-card appearance="outlined" class="item" *ngFor="let f of filters.controls; let i = index"
                          [formGroupName]="i">
                  <mat-card-content>
                    <div class="row">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.filterName' | transloco }}</mat-label>
                        <input matInput formControlName="name" class="force-ltr" required />
                        <mat-hint>{{ 'admin.dashboards.filterNameHint' | transloco }}</mat-hint>
                      </mat-form-field>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.filterLabel' | transloco }}</mat-label>
                        <input matInput formControlName="displayName" dir="auto" />
                      </mat-form-field>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.filterType' | transloco }}</mat-label>
                        <mat-select formControlName="parameterType">
                          <mat-option [value]="ParameterType.String">{{ 'admin.dashboards.typeText' | transloco }}</mat-option>
                          <mat-option [value]="ParameterType.Number">{{ 'admin.dashboards.typeNumber' | transloco }}</mat-option>
                          <mat-option [value]="ParameterType.Date">{{ 'admin.dashboards.typeDate' | transloco }}</mat-option>
                          <mat-option [value]="ParameterType.Boolean">{{ 'admin.dashboards.typeBoolean' | transloco }}</mat-option>
                          <mat-option [value]="ParameterType.Dropdown">{{ 'admin.dashboards.typeDropdown' | transloco }}</mat-option>
                        </mat-select>
                      </mat-form-field>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.filterDefault' | transloco }}</mat-label>
                        <input matInput formControlName="defaultValue" dir="auto" />
                        <mat-hint>{{ defaultHint(f) | transloco: { presets: presetTokens } }}</mat-hint>
                      </mat-form-field>
                    </div>

                    <div class="row toggles">
                      <mat-checkbox formControlName="isRequired">{{ 'admin.dashboards.required' | transloco }}</mat-checkbox>
                      <mat-checkbox formControlName="allowMultiple"
                                    *ngIf="f.get('parameterType')?.value === ParameterType.Dropdown">
                        {{ 'admin.dashboards.allowMultiple' | transloco }}
                      </mat-checkbox>
                    </div>

                    <ng-container *ngIf="f.get('parameterType')?.value === ParameterType.Dropdown">
                      <mat-radio-group formControlName="dropdownSourceType" class="row toggles">
                        <mat-radio-button [value]="DropdownSourceType.Static">{{ 'admin.dashboards.optionsStatic' | transloco }}</mat-radio-button>
                        <mat-radio-button [value]="DropdownSourceType.Query">{{ 'admin.dashboards.optionsQuery' | transloco }}</mat-radio-button>
                      </mat-radio-group>

                      <mat-form-field appearance="outline" class="full-width"
                                      *ngIf="f.get('dropdownSourceType')?.value === DropdownSourceType.Static">
                        <mat-label>{{ 'admin.dashboards.staticOptions' | transloco }}</mat-label>
                        <textarea matInput formControlName="dropdownStaticValues" rows="3" class="force-ltr"></textarea>
                        <mat-hint>{{ 'admin.dashboards.staticOptionsHint' | transloco }}</mat-hint>
                      </mat-form-field>

                      <div class="row" *ngIf="f.get('dropdownSourceType')?.value === DropdownSourceType.Query">
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.lookupQuery' | transloco }}</mat-label>
                          <mat-select formControlName="dropdownQueryId">
                            <mat-option *ngFor="let q of readQueries" [value]="q.id"><span dir="auto">{{ q.name }}</span></mat-option>
                          </mat-select>
                        </mat-form-field>
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.valueColumn' | transloco }}</mat-label>
                          <input matInput formControlName="dropdownQueryValueColumn" dir="auto" />
                        </mat-form-field>
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.labelColumn' | transloco }}</mat-label>
                          <input matInput formControlName="dropdownQueryLabelColumn" dir="auto" />
                        </mat-form-field>
                      </div>
                    </ng-container>

                    <div class="item-actions">
                      <button mat-button type="button" color="warn" (click)="removeFilter(i)">
                        <mat-icon>delete</mat-icon> {{ 'admin.dashboards.removeFilter' | transloco }}
                      </button>
                    </div>
                  </mat-card-content>
                </mat-card>
              </div>

              <button mat-stroked-button type="button" (click)="addFilter()">
                <mat-icon>add</mat-icon> {{ 'admin.dashboards.addFilter' | transloco }}
              </button>
            </div>
          </mat-tab>

          <!-- ------------------------------------------------------------ tiles -->
          <mat-tab [label]="('admin.dashboards.tabTiles' | transloco) + ' (' + tiles.length + ')'">
            <div class="tab-body">
              <p class="muted">{{ 'admin.dashboards.layoutIntro' | transloco }}</p>

              <!-- Layout preview: the real 12-column grid at a smaller scale. Drag to reorder. -->
              <div class="layout" cdkDropList cdkDropListOrientation="mixed"
                   (cdkDropListDropped)="dropTile($event)"
                   [attr.aria-label]="'admin.dashboards.layoutPreview' | transloco">
                <div class="layout-box" *ngFor="let t of tiles.controls; let i = index" cdkDrag
                     [attr.data-w]="t.get('width')?.value" [attr.data-h]="t.get('height')?.value">
                  <mat-icon class="drag-handle" cdkDragHandle aria-hidden="true">drag_indicator</mat-icon>
                  <mat-icon class="box-icon" aria-hidden="true">{{ visualIcon(t.get('visualType')?.value) }}</mat-icon>
                  <span class="box-title" dir="auto">{{ t.get('title')?.value || ('admin.dashboards.untitled' | transloco) }}</span>
                </div>
              </div>

              <mat-accordion multi formArrayName="tiles">
                <mat-expansion-panel *ngFor="let t of tiles.controls; let i = index" [formGroupName]="i"
                                     [expanded]="expandedTile === i" (opened)="expandedTile = i">
                  <mat-expansion-panel-header>
                    <mat-panel-title>
                      <mat-icon class="panel-icon" aria-hidden="true">{{ visualIcon(t.get('visualType')?.value) }}</mat-icon>
                      <span dir="auto">{{ t.get('title')?.value || ('admin.dashboards.untitled' | transloco) }}</span>
                    </mat-panel-title>
                    <mat-panel-description dir="auto">{{ queryName(t.get('dynamicQueryId')?.value) }}</mat-panel-description>
                  </mat-expansion-panel-header>

                  <div class="row">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.tileTitle' | transloco }}</mat-label>
                      <input matInput formControlName="title" dir="auto" required />
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.tileQuery' | transloco }}</mat-label>
                      <mat-select formControlName="dynamicQueryId" (selectionChange)="onTileQueryChange(i)" required>
                        <mat-option *ngFor="let q of readQueries" [value]="q.id"><span dir="auto">{{ q.name }}</span></mat-option>
                      </mat-select>
                      <mat-hint>{{ 'admin.dashboards.tileQueryHint' | transloco }}</mat-hint>
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.visualType' | transloco }}</mat-label>
                      <mat-select formControlName="visualType">
                        <mat-option *ngFor="let v of visualTypes" [value]="v.value">
                          <mat-icon>{{ v.icon }}</mat-icon> {{ v.labelKey | transloco }}
                        </mat-option>
                      </mat-select>
                    </mat-form-field>
                  </div>

                  <div class="row">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.width' | transloco }}</mat-label>
                      <mat-select formControlName="width">
                        <mat-option *ngFor="let w of widths" [value]="w">
                          {{ ('admin.dashboards.width' + w) | transloco }}
                        </mat-option>
                      </mat-select>
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.height' | transloco }}</mat-label>
                      <mat-select formControlName="height">
                        <mat-option *ngFor="let h of [1, 2, 3]" [value]="h">
                          {{ 'admin.dashboards.heightRows' | transloco: { rows: h } }}
                        </mat-option>
                      </mat-select>
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.tileRefresh' | transloco }}</mat-label>
                      <input matInput type="number" formControlName="refreshSeconds"
                             [min]="minRefresh" [max]="maxRefresh" />
                      <mat-hint>{{ 'admin.dashboards.tileRefreshHint' | transloco }}</mat-hint>
                    </mat-form-field>
                  </div>

                  <!-- Columns: learned by running the query once, then offered in the pickers below. -->
                  <div class="columns-bar" *ngIf="t.get('dynamicQueryId')?.value && t.get('visualType')?.value !== VisualType.Table">
                    <button mat-stroked-button type="button" (click)="loadColumns(i)" [disabled]="probing.has(t)">
                      <mat-icon>view_column</mat-icon> {{ 'admin.dashboards.loadColumns' | transloco }}
                    </button>
                    <span class="muted" *ngIf="probing.has(t)">{{ 'common.loading' | transloco }}</span>
                    <span class="muted" *ngIf="columnsOf(t).length > 0 && !probing.has(t)">
                      {{ 'admin.dashboards.columnsLoaded' | transloco: { count: columnsOf(t).length } }}
                    </span>
                    <span class="error-text" *ngIf="probeErrors.get(t) as err">{{ err }}</span>
                  </div>

                  <!-- KPI -->
                  <div class="row" *ngIf="t.get('visualType')?.value === VisualType.Kpi">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.kpiAggregate' | transloco }}</mat-label>
                      <mat-select formControlName="kpiAggregate">
                        <mat-option *ngFor="let a of aggregates" [value]="a.value">{{ a.labelKey | transloco }}</mat-option>
                      </mat-select>
                      <mat-hint>{{ (t.get('kpiAggregate')?.value === Aggregate.Last
                        ? 'admin.dashboards.kpiAggregateLastHint' : 'admin.dashboards.kpiAggregateHint') | transloco }}</mat-hint>
                    </mat-form-field>
                    <mat-form-field appearance="outline" *ngIf="t.get('kpiAggregate')?.value !== Aggregate.Count">
                      <mat-label>{{ 'admin.dashboards.kpiValueColumn' | transloco }}</mat-label>
                      <input matInput formControlName="valueColumn" dir="auto" [matAutocomplete]="valueAuto" />
                      <mat-autocomplete #valueAuto="matAutocomplete">
                        <mat-option *ngFor="let c of columnsOf(t)" [value]="c"><span dir="auto">{{ c }}</span></mat-option>
                      </mat-autocomplete>
                    </mat-form-field>
                    <mat-form-field appearance="outline" *ngIf="t.get('kpiAggregate')?.value !== Aggregate.Count">
                      <mat-label>{{ 'admin.dashboards.kpiCompareColumn' | transloco }}</mat-label>
                      <input matInput formControlName="compareColumn" dir="auto" [matAutocomplete]="compareAuto" />
                      <mat-autocomplete #compareAuto="matAutocomplete">
                        <mat-option *ngFor="let c of columnsOf(t)" [value]="c"><span dir="auto">{{ c }}</span></mat-option>
                      </mat-autocomplete>
                      <mat-hint>{{ (t.get('kpiAggregate')?.value === Aggregate.Last
                        ? 'admin.dashboards.kpiCompareColumnHint' : 'admin.dashboards.kpiCompareAggregateHint') | transloco }}</mat-hint>
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.valueFormat' | transloco }}</mat-label>
                      <mat-select formControlName="valueFormat">
                        <mat-option [value]="ValueFormat.Number">{{ 'admin.dashboards.formatNumber' | transloco }}</mat-option>
                        <mat-option [value]="ValueFormat.Percent">{{ 'admin.dashboards.formatPercent' | transloco }}</mat-option>
                        <mat-option [value]="ValueFormat.Currency">{{ 'admin.dashboards.formatCurrency' | transloco }}</mat-option>
                      </mat-select>
                    </mat-form-field>
                    <mat-checkbox formControlName="higherIsBetter" class="inline-check">
                      {{ 'admin.dashboards.higherIsBetter' | transloco }}
                    </mat-checkbox>
                  </div>
                  <div class="row" *ngIf="t.get('visualType')?.value === VisualType.Kpi">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.target' | transloco }}</mat-label>
                      <input matInput type="number" formControlName="targetValue" />
                      <mat-hint>{{ 'admin.dashboards.targetHint' | transloco }}</mat-hint>
                    </mat-form-field>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.targetWarn' | transloco }}</mat-label>
                      <input matInput type="number" formControlName="targetWarnPercent" min="0" max="100" />
                      <mat-hint>{{ 'admin.dashboards.targetWarnHint' | transloco }}</mat-hint>
                    </mat-form-field>
                  </div>

                  <!-- Charts -->
                  <div class="row" *ngIf="isChart(t.get('visualType')?.value)">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.categoryColumn' | transloco }}</mat-label>
                      <input matInput formControlName="categoryColumn" dir="auto" [matAutocomplete]="categoryAuto" />
                      <mat-autocomplete #categoryAuto="matAutocomplete">
                        <mat-option *ngFor="let c of columnsOf(t)" [value]="c"><span dir="auto">{{ c }}</span></mat-option>
                      </mat-autocomplete>
                    </mat-form-field>
                    <!-- With the columns known, the series is a pick list; before that, typed. -->
                    <mat-form-field appearance="outline" *ngIf="columnsOf(t).length > 0; else seriesText">
                      <mat-label>{{ 'admin.dashboards.seriesColumns' | transloco }}</mat-label>
                      <mat-select multiple [value]="seriesOf(t)" (selectionChange)="setSeries(t, $event.value)">
                        <mat-option *ngFor="let c of columnsOf(t)" [value]="c"><span dir="auto">{{ c }}</span></mat-option>
                      </mat-select>
                      <mat-hint>{{ 'admin.dashboards.seriesPickHint' | transloco }}</mat-hint>
                    </mat-form-field>
                    <ng-template #seriesText>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.seriesColumns' | transloco }}</mat-label>
                        <input matInput formControlName="seriesColumns" dir="auto" />
                        <mat-hint>{{ 'admin.dashboards.seriesColumnsHint' | transloco }}</mat-hint>
                      </mat-form-field>
                    </ng-template>
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.maxCategories' | transloco }}</mat-label>
                      <input matInput type="number" formControlName="maxCategories" min="1" />
                    </mat-form-field>
                  </div>

                  <!-- Colour rules: tables colour cells, KPIs colour their number. -->
                  <ng-container *ngIf="t.get('visualType')?.value === VisualType.Table || t.get('visualType')?.value === VisualType.Kpi">
                    <h3 class="sub">{{ 'admin.dashboards.rules' | transloco }}</h3>
                    <p class="muted">{{ 'admin.dashboards.rulesIntro' | transloco }}</p>
                    <div formArrayName="conditionalRules">
                      <div class="row map-row" *ngFor="let r of rulesOf(i).controls; let k = index" [formGroupName]="k">
                        <mat-form-field appearance="outline" *ngIf="t.get('visualType')?.value === VisualType.Table">
                          <mat-label>{{ 'admin.dashboards.ruleColumn' | transloco }}</mat-label>
                          <input matInput formControlName="column" dir="auto" required [matAutocomplete]="ruleAuto" />
                          <mat-autocomplete #ruleAuto="matAutocomplete">
                            <mat-option *ngFor="let c of columnsOf(t)" [value]="c"><span dir="auto">{{ c }}</span></mat-option>
                          </mat-autocomplete>
                        </mat-form-field>
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.ruleOperator' | transloco }}</mat-label>
                          <mat-select formControlName="operator">
                            <mat-option *ngFor="let o of operators" [value]="o">{{ ('admin.dashboards.op.' + o) | transloco }}</mat-option>
                          </mat-select>
                        </mat-form-field>
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.ruleValue' | transloco }}</mat-label>
                          <input matInput formControlName="value" dir="auto" required />
                        </mat-form-field>
                        <mat-form-field appearance="outline">
                          <mat-label>{{ 'admin.dashboards.ruleTone' | transloco }}</mat-label>
                          <mat-select formControlName="tone">
                            <mat-option *ngFor="let tone of tones" [value]="tone">{{ ('admin.dashboards.tone.' + tone) | transloco }}</mat-option>
                          </mat-select>
                        </mat-form-field>
                        <button mat-icon-button type="button" (click)="rulesOf(i).removeAt(k)"
                                [attr.aria-label]="'admin.dashboards.removeRule' | transloco">
                          <mat-icon>delete</mat-icon>
                        </button>
                      </div>
                    </div>
                    <button mat-button type="button" (click)="addRule(i)">
                      <mat-icon>add</mat-icon> {{ 'admin.dashboards.addRule' | transloco }}
                    </button>
                  </ng-container>

                  <!-- Parameters -->
                  <h3 class="sub" *ngIf="mapsOf(i).length > 0">{{ 'admin.dashboards.parameters' | transloco }}</h3>
                  <p class="muted" *ngIf="t.get('dynamicQueryId')?.value && mapsOf(i).length === 0">
                    {{ 'admin.dashboards.noParameters' | transloco }}
                  </p>
                  <div formArrayName="parameterMaps">
                    <div class="row map-row" *ngFor="let m of mapsOf(i).controls; let j = index" [formGroupName]="j">
                      <span class="map-target force-ltr">&#64;{{ m.get('targetParameterName')?.value }}</span>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.mapSource' | transloco }}</mat-label>
                        <mat-select formControlName="sourceKind">
                          <mat-option [value]="''">{{ 'admin.dashboards.sourceDefault' | transloco }}</mat-option>
                          <mat-option [value]="MapSource.Filter" [disabled]="filterNames.length === 0">
                            {{ 'admin.dashboards.sourceFilter' | transloco }}
                          </mat-option>
                          <mat-option [value]="MapSource.Constant">{{ 'admin.dashboards.sourceConstant' | transloco }}</mat-option>
                        </mat-select>
                      </mat-form-field>
                      <mat-form-field appearance="outline" *ngIf="m.get('sourceKind')?.value === MapSource.Filter">
                        <mat-label>{{ 'admin.dashboards.mapFilter' | transloco }}</mat-label>
                        <mat-select formControlName="filterName">
                          <mat-option *ngFor="let n of filterNames" [value]="n">{{ n }}</mat-option>
                        </mat-select>
                      </mat-form-field>
                      <mat-form-field appearance="outline" *ngIf="m.get('sourceKind')?.value === MapSource.Constant">
                        <mat-label>{{ 'admin.dashboards.mapConstant' | transloco }}</mat-label>
                        <input matInput formControlName="constantValue" dir="auto" />
                      </mat-form-field>
                    </div>
                  </div>

                  <!-- Drill-through -->
                  <h3 class="sub">{{ 'admin.dashboards.drill' | transloco }}</h3>
                  <div class="row">
                    <mat-form-field appearance="outline">
                      <mat-label>{{ 'admin.dashboards.drillAction' | transloco }}</mat-label>
                      <mat-select formControlName="drillAction">
                        <mat-option [value]="DrillAction.None">{{ 'admin.dashboards.drillNone' | transloco }}</mat-option>
                        <mat-option [value]="DrillAction.FilterDashboard" [disabled]="filterNames.length === 0 || !isChart(t.get('visualType')?.value)">
                          {{ 'admin.dashboards.drillFilter' | transloco }}
                        </mat-option>
                        <mat-option [value]="DrillAction.OpenReport" [disabled]="!isChart(t.get('visualType')?.value)">
                          {{ 'admin.dashboards.drillReport' | transloco }}
                        </mat-option>
                        <mat-option [value]="DrillAction.ShowRows">{{ 'admin.dashboards.drillRows' | transloco }}</mat-option>
                      </mat-select>
                    </mat-form-field>
                    <mat-form-field appearance="outline" *ngIf="t.get('drillAction')?.value === DrillAction.FilterDashboard">
                      <mat-label>{{ 'admin.dashboards.drillFilterName' | transloco }}</mat-label>
                      <mat-select formControlName="drillFilterName">
                        <mat-option *ngFor="let n of filterNames" [value]="n">{{ n }}</mat-option>
                      </mat-select>
                    </mat-form-field>
                    <ng-container *ngIf="t.get('drillAction')?.value === DrillAction.OpenReport">
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.drillReportId' | transloco }}</mat-label>
                        <mat-select formControlName="drillReportId">
                          <mat-option *ngFor="let r of reports" [value]="r.id"><span dir="auto">{{ r.name }}</span></mat-option>
                        </mat-select>
                      </mat-form-field>
                      <mat-form-field appearance="outline">
                        <mat-label>{{ 'admin.dashboards.drillReportParameter' | transloco }}</mat-label>
                        <input matInput formControlName="drillReportParameter" class="force-ltr" />
                        <mat-hint>{{ 'admin.dashboards.drillReportParameterHint' | transloco }}</mat-hint>
                      </mat-form-field>
                    </ng-container>
                  </div>

                  <mat-action-row>
                    <button mat-icon-button type="button" (click)="moveTile(i, -1)" [disabled]="i === 0"
                            [attr.aria-label]="'admin.dashboards.moveUp' | transloco" [matTooltip]="'admin.dashboards.moveUp' | transloco">
                      <mat-icon>arrow_upward</mat-icon>
                    </button>
                    <button mat-icon-button type="button" (click)="moveTile(i, 1)" [disabled]="i === tiles.length - 1"
                            [attr.aria-label]="'admin.dashboards.moveDown' | transloco" [matTooltip]="'admin.dashboards.moveDown' | transloco">
                      <mat-icon>arrow_downward</mat-icon>
                    </button>
                    <span class="spacer"></span>
                    <button mat-button type="button" color="warn" (click)="removeTile(i)">
                      <mat-icon>delete</mat-icon> {{ 'admin.dashboards.removeTile' | transloco }}
                    </button>
                  </mat-action-row>
                </mat-expansion-panel>
              </mat-accordion>

              <button mat-stroked-button type="button" class="add-tile" (click)="addTile()">
                <mat-icon>add</mat-icon> {{ 'admin.dashboards.addTile' | transloco }}
              </button>
            </div>
          </mat-tab>
        </mat-tab-group>

        <div class="actions">
          <button mat-button type="button" routerLink="/admin/dashboards">{{ 'common.cancel' | transloco }}</button>
          <button mat-flat-button color="primary" type="submit" [disabled]="saving">
            {{ (saving ? 'common.saving' : 'common.save') | transloco }}
          </button>
        </div>
      </form>
    </div>
  `,
  styles: [`
    .tab-body { padding: 20px 4px; }
    .full-width { inline-size: 100%; }
    /* The row gap leaves room for a hint under one field before the next row's labels begin. */
    .row { display: flex; flex-wrap: wrap; gap: 14px 12px; align-items: flex-start; }
    /* Room for the hint under one row before the next row's labels begin. */
    .row + .row, .columns-bar + .row { margin-block-start: 14px; }
    .row > mat-form-field { flex: 1 1 220px; }
    .toggles { gap: 16px; margin-block-end: 12px; }
    .muted { color: var(--text-secondary); }
    .item { margin-block-end: 12px; }
    .item-actions { display: flex; justify-content: flex-end; }
    .sub { font-size: 14px; font-weight: 600; margin: 12px 0 8px; color: var(--text-primary); }
    .map-row { align-items: center; }
    .map-target { flex: 0 0 160px; font-family: monospace; color: var(--text-secondary); padding-block-end: 20px; }
    .inline-check { align-self: center; padding-block-end: 20px; }
    .columns-bar { display: flex; align-items: center; flex-wrap: wrap; gap: 12px; margin-block-end: 16px; }
    .panel-icon { margin-inline-end: 8px; color: var(--accent-primary); }
    .add-tile { margin-block-start: 12px; }
    .actions { display: flex; justify-content: flex-end; gap: 12px; margin-block-start: 16px; }
    .spacer { flex: 1 1 auto; }

    .layout {
      display: grid; grid-template-columns: repeat(12, minmax(0, 1fr)); grid-auto-rows: 44px;
      gap: 6px; padding: 10px; margin-block-end: 16px; border-radius: 8px;
      border: 1px dashed var(--border-color); background: var(--bg-secondary); min-block-size: 44px;
    }
    .layout-box {
      display: flex; align-items: center; gap: 6px; padding: 0 8px; min-inline-size: 0;
      border-radius: 6px; border: 1px solid var(--border-color); background: var(--bg-card);
      color: var(--text-primary); font-size: 12px; cursor: grab;
    }
    .layout-box .box-title { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .layout-box .box-icon { color: var(--accent-primary); font-size: 18px; inline-size: 18px; block-size: 18px; flex: none; }
    .layout-box .drag-handle { color: var(--text-secondary); font-size: 18px; inline-size: 18px; block-size: 18px; flex: none; }
    .layout > [data-w="3"] { grid-column: span 3; }
    .layout > [data-w="4"] { grid-column: span 4; }
    .layout > [data-w="6"] { grid-column: span 6; }
    .layout > [data-w="8"] { grid-column: span 8; }
    .layout > [data-w="12"] { grid-column: span 12; }
    .layout > [data-h="2"] { grid-row: span 2; }
    .layout > [data-h="3"] { grid-row: span 3; }
    .cdk-drag-preview { box-shadow: 0 4px 12px rgba(0, 0, 0, 0.2); }
    .cdk-drag-placeholder { opacity: 0.4; }
  `]
})
export class DashboardFormComponent implements OnInit {
  readonly ParameterType = ParameterType;
  readonly DropdownSourceType = DropdownSourceType;
  readonly VisualType = DashboardVisualType;
  readonly ValueFormat = DashboardValueFormat;
  readonly DrillAction = DashboardDrillAction;
  readonly MapSource = DashboardTileParameterSource;
  readonly widths = DASHBOARD_TILE_WIDTHS;
  readonly Aggregate = DashboardKpiAggregate;
  readonly presetTokens = DATE_PRESETS.map(p => p.token).join(', ') + ', today-N';
  readonly operators = ['gt', 'gte', 'lt', 'lte', 'eq', 'neq', 'contains'];
  readonly tones = ['good', 'warn', 'bad'];
  readonly aggregates = [
    { value: DashboardKpiAggregate.Last, labelKey: 'admin.dashboards.aggLast' },
    { value: DashboardKpiAggregate.Sum, labelKey: 'admin.dashboards.aggSum' },
    { value: DashboardKpiAggregate.Average, labelKey: 'admin.dashboards.aggAverage' },
    { value: DashboardKpiAggregate.Count, labelKey: 'admin.dashboards.aggCount' },
    { value: DashboardKpiAggregate.Min, labelKey: 'admin.dashboards.aggMin' },
    { value: DashboardKpiAggregate.Max, labelKey: 'admin.dashboards.aggMax' }
  ];

  /**
   * Columns learned per tile, keyed by the tile's form group so they survive reordering. Not part
   * of the form: they are a hint for the author, not something saved.
   */
  private columns = new WeakMap<AbstractControl, string[]>();
  probing = new Set<AbstractControl>();
  probeErrors = new Map<AbstractControl, string>();
  readonly minRefresh = DASHBOARD_MIN_REFRESH_SECONDS;
  readonly maxRefresh = DASHBOARD_MAX_REFRESH_SECONDS;

  readonly visualTypes = [
    { value: DashboardVisualType.Kpi, icon: 'speed', labelKey: 'admin.dashboards.visualKpi' },
    { value: DashboardVisualType.Column, icon: 'bar_chart', labelKey: 'admin.dashboards.visualColumn' },
    { value: DashboardVisualType.Bar, icon: 'align_horizontal_left', labelKey: 'admin.dashboards.visualBar' },
    { value: DashboardVisualType.Line, icon: 'show_chart', labelKey: 'admin.dashboards.visualLine' },
    { value: DashboardVisualType.Pie, icon: 'pie_chart', labelKey: 'admin.dashboards.visualPie' },
    { value: DashboardVisualType.Table, icon: 'table_chart', labelKey: 'admin.dashboards.visualTable' }
  ];

  private fb = inject(FormBuilder);
  private dashboards = inject(DashboardService);
  private queryService = inject(QueryService);
  private reportService = inject(ReportService);
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private toast = inject(ToastService);
  private transloco = inject(TranslocoService);
  private cdr = inject(ChangeDetectorRef);

  form: FormGroup = this.newForm();
  /** Only read queries: a write query on a refresh timer would repeat the write every interval. */
  readQueries: DynamicQuery[] = [];
  reports: ReportSummary[] = [];
  isEdit = false;
  loading = true;
  saving = false;
  expandedTile = -1;

  private dashboardId: string | null = null;

  get filters(): FormArray { return this.form.get('filters') as FormArray; }
  get tiles(): FormArray { return this.form.get('tiles') as FormArray; }

  get filterNames(): string[] {
    return this.filters.controls
      .map(c => (c.get('name')?.value as string ?? '').trim())
      .filter(n => n.length > 0);
  }

  mapsOf(i: number): FormArray {
    return this.tiles.at(i).get('parameterMaps') as FormArray;
  }

  rulesOf(i: number): FormArray {
    return this.tiles.at(i).get('conditionalRules') as FormArray;
  }

  addRule(i: number): void {
    this.rulesOf(i).push(this.ruleGroup());
  }

  columnsOf(tile: AbstractControl): string[] {
    return this.columns.get(tile) ?? [];
  }

  seriesOf(tile: AbstractControl): string[] {
    return String(tile.get('seriesColumns')?.value ?? '').split(',').map(s => s.trim()).filter(s => s);
  }

  setSeries(tile: AbstractControl, series: string[]): void {
    tile.get('seriesColumns')?.setValue(series.join(', '));
  }

  /**
   * Runs the tile's query once, with the values the tile would send — constants, and each mapped
   * filter's default — and keeps the column names for the pickers.
   */
  loadColumns(i: number): void {
    const tile = this.tiles.at(i);
    const queryId = tile.get('dynamicQueryId')?.value;
    if (!queryId) return;

    const parameters: Record<string, string> = {};
    for (const m of this.mapsOf(i).controls) {
      const target = m.get('targetParameterName')?.value;
      if (m.get('sourceKind')?.value === DashboardTileParameterSource.Constant) {
        parameters[target] = m.get('constantValue')?.value ?? '';
      } else if (m.get('sourceKind')?.value === DashboardTileParameterSource.Filter) {
        const filter = this.filters.controls.find(f => f.get('name')?.value === m.get('filterName')?.value);
        const value = filter?.get('defaultValue')?.value;
        if (value) parameters[target] = value;
      }
    }

    this.probing.add(tile);
    this.probeErrors.delete(tile);
    this.dashboards.probeColumns(queryId, parameters).subscribe({
      next: probe => {
        this.probing.delete(tile);
        if (probe.error) {
          this.probeErrors.set(tile, probe.error);
        } else {
          this.columns.set(tile, probe.columns);
        }
        this.cdr.detectChanges();
      },
      error: err => {
        this.probing.delete(tile);
        this.probeErrors.set(tile, extractApiError(err, this.transloco.translate('admin.dashboards.probeFailed')));
        this.cdr.detectChanges();
      }
    });
  }

  ngOnInit(): void {
    this.dashboardId = this.route.snapshot.paramMap.get('id');
    this.isEdit = !!this.dashboardId;

    forkJoin({
      queries: this.queryService.getAllQueries(),
      // Only feeds the drill-to-report picker; a role that cannot list reports still builds
      // dashboards, it just cannot offer that drill.
      reports: this.reportService.getAll().pipe(catchError(() => of([] as ReportSummary[]))),
      dashboard: this.dashboardId ? this.dashboards.getById(this.dashboardId) : of(null)
    }).subscribe({
      next: ({ queries, reports, dashboard }) => {
        this.readQueries = queries
          .filter(q => q.queryType === QueryType.Select || q.queryType === QueryType.Other)
          .sort((a, b) => a.name.localeCompare(b.name));
        this.reports = reports;
        if (dashboard) this.patch(dashboard);
        this.loading = false;
        this.cdr.detectChanges();
      },
      error: err => {
        this.toast.error(err, 'admin.dashboards.loadFailed');
        this.loading = false;
        this.cdr.detectChanges();
      }
    });
  }

  // ---------------------------------------------------------------- filters

  addFilter(): void {
    this.filters.push(this.filterGroup());
  }

  removeFilter(i: number): void {
    const name = this.filters.at(i).get('name')?.value;
    this.filters.removeAt(i);
    // Anything that pointed at the removed filter falls back rather than being saved broken.
    for (const tile of this.tiles.controls) {
      if (tile.get('drillFilterName')?.value === name) {
        tile.patchValue({ drillAction: DashboardDrillAction.None, drillFilterName: null });
      }
      for (const map of (tile.get('parameterMaps') as FormArray).controls) {
        if (map.get('filterName')?.value === name) map.patchValue({ sourceKind: '', filterName: null });
      }
    }
  }

  defaultHint(f: AbstractControl): string {
    const type = f.get('parameterType')?.value;
    if (type === ParameterType.Date) return 'admin.dashboards.defaultHintDatePresets';
    if (type === ParameterType.Dropdown && f.get('allowMultiple')?.value) return 'admin.dashboards.defaultHintMulti';
    return 'admin.dashboards.defaultHintPlain';
  }

  // ---------------------------------------------------------------- tiles

  addTile(): void {
    this.tiles.push(this.tileGroup());
    this.expandedTile = this.tiles.length - 1;
  }

  removeTile(i: number): void {
    this.tiles.removeAt(i);
    this.expandedTile = -1;
  }

  moveTile(i: number, delta: number): void {
    const target = i + delta;
    if (target < 0 || target >= this.tiles.length) return;
    const control = this.tiles.at(i);
    this.tiles.removeAt(i);
    this.tiles.insert(target, control);
    this.expandedTile = target;
  }

  dropTile(event: CdkDragDrop<unknown>): void {
    if (event.previousIndex === event.currentIndex) return;
    const controls = [...this.tiles.controls];
    moveItemInArray(controls, event.previousIndex, event.currentIndex);
    this.tiles.clear();
    controls.forEach(c => this.tiles.push(c));
    this.expandedTile = -1;
  }

  /**
   * Regenerates the parameter rows from the newly chosen query's declared parameters, keeping any
   * choice already made for a parameter of the same name.
   */
  onTileQueryChange(i: number): void {
    const tile = this.tiles.at(i);
    const query = this.readQueries.find(q => q.id === tile.get('dynamicQueryId')?.value);
    const maps = this.mapsOf(i);
    const previous = new Map(maps.controls.map(m => [m.get('targetParameterName')?.value, m.value]));

    maps.clear();
    for (const p of [...(query?.parameters ?? [])].sort((a, b) => a.sortOrder - b.sortOrder)) {
      const kept = previous.get(p.name);
      // A filter with the same name as the parameter is almost always the intended source.
      const sameName = this.filterNames.find(n => n.toLowerCase() === p.name.toLowerCase());
      maps.push(this.mapGroup(p.name, kept?.sourceKind ?? (sameName ? DashboardTileParameterSource.Filter : ''),
        kept?.filterName ?? sameName ?? null, kept?.constantValue ?? null));
    }

    if (!tile.get('title')?.value && query) {
      tile.patchValue({ title: query.name });
    }

    this.columns.delete(tile);
    this.loadColumns(i);
  }

  isChart(type: DashboardVisualType): boolean {
    return type === DashboardVisualType.Column || type === DashboardVisualType.Bar
      || type === DashboardVisualType.Line || type === DashboardVisualType.Pie;
  }

  visualIcon(type: DashboardVisualType): string {
    return this.visualTypes.find(v => v.value === type)?.icon ?? 'widgets';
  }

  queryName(id: string | null): string {
    return this.readQueries.find(q => q.id === id)?.name ?? '';
  }

  openViewer(): void {
    if (!this.dashboardId) return;
    window.open(this.router.serializeUrl(this.router.createUrlTree(['/user/dashboards', this.dashboardId])), '_blank');
  }

  // ---------------------------------------------------------------- save

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      this.toast.error(null, 'common.correctFields');
      return;
    }

    const input = this.toInput();
    this.saving = true;
    const request = this.dashboardId
      ? this.dashboards.update(this.dashboardId, input)
      : this.dashboards.create(input);

    request.subscribe({
      next: saved => {
        this.saving = false;
        this.toast.success('admin.dashboards.saved');
        if (!this.dashboardId) {
          // Stay in the builder, now editing, so "Open dashboard" works straight away.
          this.router.navigate(['/admin/dashboards/edit', saved.id], { replaceUrl: true });
          this.dashboardId = saved.id;
          this.isEdit = true;
        }
        this.cdr.detectChanges();
      },
      error: err => {
        this.saving = false;
        this.toast.error(err, 'admin.dashboards.saveFailed');
        this.cdr.detectChanges();
      }
    });
  }

  private toInput(): DashboardInput {
    const v = this.form.getRawValue();
    return {
      name: v.name.trim(),
      description: v.description?.trim() || null,
      isEnabled: v.isEnabled,
      defaultRefreshSeconds: Number(v.defaultRefreshSeconds),
      sortOrder: Number(v.sortOrder) || 0,
      filters: v.filters.map((f: any, i: number) => ({
        name: f.name.trim(),
        displayName: f.displayName?.trim() || f.name.trim(),
        parameterType: f.parameterType,
        isRequired: !!f.isRequired,
        defaultValue: f.defaultValue?.trim() || null,
        sortOrder: i,
        allowMultiple: f.parameterType === ParameterType.Dropdown && !!f.allowMultiple,
        dropdownSourceType: f.parameterType === ParameterType.Dropdown ? f.dropdownSourceType : null,
        dropdownStaticValues: f.dropdownSourceType === DropdownSourceType.Static ? f.dropdownStaticValues || null : null,
        dropdownQueryId: f.dropdownSourceType === DropdownSourceType.Query ? f.dropdownQueryId || null : null,
        dropdownQueryValueColumn: f.dropdownSourceType === DropdownSourceType.Query ? f.dropdownQueryValueColumn || null : null,
        dropdownQueryLabelColumn: f.dropdownSourceType === DropdownSourceType.Query ? f.dropdownQueryLabelColumn || null : null
      })),
      tiles: v.tiles.map((t: any, i: number) => ({
        title: t.title.trim(),
        dynamicQueryId: t.dynamicQueryId,
        sortOrder: i,
        width: t.width,
        height: t.height,
        visualType: t.visualType,
        categoryColumn: t.categoryColumn?.trim() || null,
        seriesColumns: String(t.seriesColumns ?? '').split(',').map((s: string) => s.trim()).filter((s: string) => s),
        maxCategories: Number(t.maxCategories) || 25,
        valueColumn: t.valueColumn?.trim() || null,
        compareColumn: t.compareColumn?.trim() || null,
        kpiAggregate: t.kpiAggregate,
        valueFormat: t.valueFormat,
        higherIsBetter: !!t.higherIsBetter,
        targetValue: t.targetValue === null || t.targetValue === '' ? null : Number(t.targetValue),
        targetWarnPercent: Number(t.targetWarnPercent ?? 10),
        conditionalRules: (t.conditionalRules as DashboardConditionalRule[])
          .filter(r => String(r.value ?? '').trim() !== '')
          .map(r => ({ column: r.column?.trim() || null, operator: r.operator, value: String(r.value).trim(), tone: r.tone })),
        refreshSeconds: t.refreshSeconds === null || t.refreshSeconds === '' ? null : Number(t.refreshSeconds),
        drillAction: t.drillAction,
        drillFilterName: t.drillAction === DashboardDrillAction.FilterDashboard ? t.drillFilterName : null,
        drillReportId: t.drillAction === DashboardDrillAction.OpenReport ? t.drillReportId : null,
        drillReportParameter: t.drillAction === DashboardDrillAction.OpenReport ? t.drillReportParameter?.trim() || null : null,
        parameterMaps: t.parameterMaps
          .filter((m: any) => m.sourceKind !== '')
          .map((m: any) => ({
            targetParameterName: m.targetParameterName,
            sourceKind: m.sourceKind,
            filterName: m.sourceKind === DashboardTileParameterSource.Filter ? m.filterName : null,
            constantValue: m.sourceKind === DashboardTileParameterSource.Constant ? m.constantValue : null
          }))
      }))
    };
  }

  // ---------------------------------------------------------------- form building

  private newForm(): FormGroup {
    return this.fb.group({
      name: ['', Validators.required],
      description: [''],
      isEnabled: [true],
      defaultRefreshSeconds: [60, [Validators.required, Validators.min(DASHBOARD_MIN_REFRESH_SECONDS), Validators.max(DASHBOARD_MAX_REFRESH_SECONDS)]],
      sortOrder: [0],
      filters: this.fb.array([]),
      tiles: this.fb.array([])
    });
  }

  private filterGroup(f?: Partial<DashboardFilter>): FormGroup {
    return this.fb.group({
      name: [f?.name ?? '', [Validators.required, Validators.pattern(/^[A-Za-z][A-Za-z0-9_]*$/)]],
      displayName: [f?.displayName ?? ''],
      parameterType: [f?.parameterType ?? ParameterType.String],
      isRequired: [f?.isRequired ?? false],
      defaultValue: [f?.defaultValue ?? ''],
      allowMultiple: [f?.allowMultiple ?? false],
      dropdownSourceType: [f?.dropdownSourceType ?? DropdownSourceType.Static],
      dropdownStaticValues: [f?.dropdownStaticValues ?? ''],
      dropdownQueryId: [f?.dropdownQueryId ?? null],
      dropdownQueryValueColumn: [f?.dropdownQueryValueColumn ?? ''],
      dropdownQueryLabelColumn: [f?.dropdownQueryLabelColumn ?? '']
    });
  }

  private tileGroup(t?: Partial<DashboardTile>): FormGroup {
    const refreshValidators = [Validators.min(DASHBOARD_MIN_REFRESH_SECONDS), Validators.max(DASHBOARD_MAX_REFRESH_SECONDS)];
    return this.fb.group({
      title: [t?.title ?? '', Validators.required],
      dynamicQueryId: [t?.dynamicQueryId ?? null, Validators.required],
      visualType: [t?.visualType ?? DashboardVisualType.Column],
      width: [t?.width ?? 6],
      height: [t?.height ?? 1],
      refreshSeconds: [t?.refreshSeconds ?? null, refreshValidators],
      categoryColumn: [t?.categoryColumn ?? ''],
      seriesColumns: [(t?.seriesColumns ?? []).join(', ')],
      maxCategories: [t?.maxCategories ?? 25],
      valueColumn: [t?.valueColumn ?? ''],
      compareColumn: [t?.compareColumn ?? ''],
      kpiAggregate: [t?.kpiAggregate ?? DashboardKpiAggregate.Last],
      valueFormat: [t?.valueFormat ?? DashboardValueFormat.Number],
      higherIsBetter: [t?.higherIsBetter ?? true],
      targetValue: [t?.targetValue ?? null],
      targetWarnPercent: [t?.targetWarnPercent ?? 10, [Validators.min(0), Validators.max(100)]],
      conditionalRules: this.fb.array((t?.conditionalRules ?? []).map(r => this.ruleGroup(r))),
      drillAction: [t?.drillAction ?? DashboardDrillAction.None],
      drillFilterName: [t?.drillFilterName ?? null],
      drillReportId: [t?.drillReportId ?? null],
      drillReportParameter: [t?.drillReportParameter ?? ''],
      parameterMaps: this.fb.array([])
    });
  }

  private ruleGroup(r?: Partial<DashboardConditionalRule>): FormGroup {
    return this.fb.group({
      column: [r?.column ?? ''],
      operator: [r?.operator ?? 'gt'],
      value: [r?.value ?? ''],
      tone: [r?.tone ?? 'bad']
    });
  }

  private mapGroup(target: string, sourceKind: MapSource, filterName: string | null, constantValue: string | null): FormGroup {
    return this.fb.group({
      targetParameterName: [target],
      sourceKind: [sourceKind],
      filterName: [filterName],
      constantValue: [constantValue ?? '']
    });
  }

  private patch(d: Dashboard): void {
    this.form.patchValue({
      name: d.name,
      description: d.description,
      isEnabled: d.isEnabled,
      defaultRefreshSeconds: d.defaultRefreshSeconds,
      sortOrder: d.sortOrder
    });

    for (const f of [...d.filters].sort((a, b) => a.sortOrder - b.sortOrder)) {
      this.filters.push(this.filterGroup(f));
    }

    for (const t of [...d.tiles].sort((a, b) => a.sortOrder - b.sortOrder)) {
      const group = this.tileGroup(t);
      this.tiles.push(group);
      const maps = group.get('parameterMaps') as FormArray;
      const saved = new Map(t.parameterMaps.map(m => [m.targetParameterName.toLowerCase(), m]));
      const query = this.readQueries.find(q => q.id === t.dynamicQueryId);

      // One row per declared parameter, filled from what was saved. A saved map for a parameter
      // the query no longer declares is kept too, so nothing is dropped silently.
      const names = [...(query?.parameters ?? [])].sort((a, b) => a.sortOrder - b.sortOrder).map(p => p.name);
      for (const m of t.parameterMaps) {
        if (!names.some(n => n.toLowerCase() === m.targetParameterName.toLowerCase())) names.push(m.targetParameterName);
      }
      for (const name of names) {
        const m = saved.get(name.toLowerCase());
        maps.push(this.mapGroup(name, m ? m.sourceKind : '', m?.filterName ?? null, m?.constantValue ?? null));
      }
    }
  }
}
