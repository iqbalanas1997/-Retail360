import { Component, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';

import { ApiError } from '../core/api-error';
import { formatScaled } from '../core/catalog-rules';
import { expenseCategoryLabel } from '../core/expense-rules';
import { ReportApi } from '../core/report-api.service';
import { type DashboardReport, type LowStockReport } from '../core/report.models';
import { type ReportPeriod, reportRange, requireReportRange, utcToday } from '../core/report-rules';
import { formatSoldAt } from '../core/sale-rules';

@Component({
  selector: 'app-dashboard-page',
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './dashboard-page.html',
})
export class DashboardPage {
  private readonly formBuilder = inject(FormBuilder);
  private readonly api = inject(ReportApi);
  private dashboardGeneration = 0;

  protected readonly report = signal<DashboardReport | null>(null);
  protected readonly lowStock = signal<LowStockReport | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadingStock = signal(true);
  protected readonly error = signal('');
  protected readonly stockError = signal('');
  protected readonly form = this.formBuilder.nonNullable.group({
    from: '',
    to: '',
  });

  constructor() {
    const today = utcToday();
    this.form.setValue(reportRange('today', today));
    void this.loadDashboard();
    void this.loadLowStock();
  }

  protected selectPeriod(period: ReportPeriod): void {
    this.form.setValue(reportRange(period, utcToday()));
    void this.loadDashboard();
  }

  protected applyCustom(): void {
    try {
      const range = requireReportRange(this.form.controls.from.value, this.form.controls.to.value);
      this.form.setValue(range);
      this.error.set('');
    } catch (error) {
      this.error.set(error instanceof Error ? error.message : 'The request failed.');
      return;
    }

    void this.loadDashboard();
  }

  protected async loadDashboard(): Promise<void> {
    const generation = ++this.dashboardGeneration;
    const from = this.form.controls.from.value;
    const to = this.form.controls.to.value;
    this.loading.set(true);
    this.error.set('');
    this.report.set(null);
    try {
      const report = await firstValueFrom(this.api.dashboard(from, to));
      if (generation !== this.dashboardGeneration) {
        return;
      }

      this.report.set(report);
    } catch (error) {
      if (generation !== this.dashboardGeneration) {
        return;
      }

      this.error.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      if (generation === this.dashboardGeneration) {
        this.loading.set(false);
      }
    }
  }

  protected async loadLowStock(): Promise<void> {
    this.loadingStock.set(true);
    this.stockError.set('');
    try {
      this.lowStock.set(await firstValueFrom(this.api.lowStock()));
    } catch (error) {
      this.stockError.set(error instanceof ApiError ? error.message : 'The request failed.');
    } finally {
      this.loadingStock.set(false);
    }
  }

  protected money(value: number): string {
    return formatScaled(value, 2);
  }

  protected quantity(value: number): string {
    return formatScaled(value, 3);
  }

  protected average(value: number | null): string {
    return value === null ? 'No sales' : formatScaled(value, 2);
  }

  protected categoryLabel(value: string): string {
    return expenseCategoryLabel(value);
  }

  protected soldAt(value: string): string {
    return formatSoldAt(value);
  }
}
