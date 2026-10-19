import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  type Purchase,
  type PurchaseQuery,
  type ReceivePurchaseBody,
  type SavePurchaseBody,
} from './purchase.models';

@Injectable({ providedIn: 'root' })
export class PurchaseApi {
  private readonly http = inject(HttpClient);

  list(query: PurchaseQuery): Observable<Purchase[]> {
    let params = new HttpParams().set('limit', String(query.limit)).set('offset', String(query.offset));
    if (query.supplierId) {
      params = params.set('supplierId', query.supplierId);
    }

    if (query.status) {
      params = params.set('status', query.status);
    }

    return this.http.get<Purchase[]>('/api/purchases', { params });
  }

  get(id: string): Observable<Purchase> {
    return this.http.get<Purchase>(`/api/purchases/${id}`);
  }

  create(body: SavePurchaseBody): Observable<Purchase> {
    return this.http.post<Purchase>('/api/purchases', body);
  }

  update(id: string, body: SavePurchaseBody): Observable<Purchase> {
    return this.http.put<Purchase>(`/api/purchases/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/purchases/${id}`);
  }

  receive(id: string, body: ReceivePurchaseBody): Observable<Purchase> {
    return this.http.post<Purchase>(`/api/purchases/${id}/receive`, body);
  }
}
