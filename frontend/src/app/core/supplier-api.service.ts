import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type SaveSupplierBody, type Supplier, type SupplierQuery } from './supplier.models';

@Injectable({ providedIn: 'root' })
export class SupplierApi {
  private readonly http = inject(HttpClient);

  list(query: SupplierQuery): Observable<Supplier[]> {
    let params = new HttpParams()
      .set('activeOnly', String(query.activeOnly))
      .set('limit', String(query.limit))
      .set('offset', String(query.offset));
    const name = query.name.trim();
    if (name) {
      params = params.set('name', name);
    }

    return this.http.get<Supplier[]>('/api/suppliers', { params });
  }

  get(id: string): Observable<Supplier> {
    return this.http.get<Supplier>(`/api/suppliers/${id}`);
  }

  create(body: SaveSupplierBody): Observable<Supplier> {
    return this.http.post<Supplier>('/api/suppliers', body);
  }

  update(id: string, body: SaveSupplierBody): Observable<Supplier> {
    return this.http.put<Supplier>(`/api/suppliers/${id}`, body);
  }

  activate(id: string): Observable<Supplier> {
    return this.http.post<Supplier>(`/api/suppliers/${id}/activate`, {});
  }

  deactivate(id: string): Observable<Supplier> {
    return this.http.post<Supplier>(`/api/suppliers/${id}/deactivate`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/suppliers/${id}`);
  }
}
