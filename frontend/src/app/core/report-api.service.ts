import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type DashboardReport, type LowStockReport } from './report.models';

@Injectable({ providedIn: 'root' })
export class ReportApi {
  private readonly http = inject(HttpClient);

  dashboard(from: string, to: string): Observable<DashboardReport> {
    const params = new HttpParams().set('from', from).set('to', to);
    return this.http.get<DashboardReport>('/api/reports/dashboard', { params });
  }

  lowStock(): Observable<LowStockReport> {
    return this.http.get<LowStockReport>('/api/reports/low-stock', {
      params: new HttpParams().set('limit', '10'),
    });
  }
}
