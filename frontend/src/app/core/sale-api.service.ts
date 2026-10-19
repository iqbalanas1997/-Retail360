import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type CompleteSaleBody, type Sale, type SaleQuery } from './sale.models';

@Injectable({ providedIn: 'root' })
export class SaleApi {
  private readonly http = inject(HttpClient);

  list(query: SaleQuery): Observable<Sale[]> {
    let params = new HttpParams().set('limit', String(query.limit)).set('offset', String(query.offset));
    if (query.receiptNumber !== null) {
      params = params.set('receiptNumber', String(query.receiptNumber));
    }

    return this.http.get<Sale[]>('/api/sales', { params });
  }

  get(id: string): Observable<Sale> {
    return this.http.get<Sale>(`/api/sales/${id}`);
  }

  complete(body: CompleteSaleBody): Observable<Sale> {
    return this.http.post<Sale>('/api/sales', body);
  }
}
