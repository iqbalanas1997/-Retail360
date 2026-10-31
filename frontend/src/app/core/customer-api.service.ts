import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  type Customer,
  type CustomerQuery,
  type CustomerSale,
  type SaveCustomerBody,
} from './customer.models';

@Injectable({ providedIn: 'root' })
export class CustomerApi {
  private readonly http = inject(HttpClient);

  list(query: CustomerQuery): Observable<Customer[]> {
    let params = new HttpParams()
      .set('activeOnly', String(query.activeOnly))
      .set('limit', String(query.limit))
      .set('offset', String(query.offset));
    const search = query.search.trim();
    if (search) {
      params = params.set('search', search);
    }

    return this.http.get<Customer[]>('/api/customers', { params });
  }

  get(id: string): Observable<Customer> {
    return this.http.get<Customer>(`/api/customers/${id}`);
  }

  sales(id: string, limit: number, offset: number): Observable<CustomerSale[]> {
    const params = new HttpParams().set('limit', String(limit)).set('offset', String(offset));
    return this.http.get<CustomerSale[]>(`/api/customers/${id}/sales`, { params });
  }

  create(body: SaveCustomerBody): Observable<Customer> {
    return this.http.post<Customer>('/api/customers', body);
  }

  update(id: string, body: SaveCustomerBody): Observable<Customer> {
    return this.http.put<Customer>(`/api/customers/${id}`, body);
  }

  activate(id: string): Observable<Customer> {
    return this.http.post<Customer>(`/api/customers/${id}/activate`, {});
  }

  deactivate(id: string): Observable<Customer> {
    return this.http.post<Customer>(`/api/customers/${id}/deactivate`, {});
  }

  remove(id: string): Observable<void> {
    return this.http.delete<void>(`/api/customers/${id}`);
  }
}
