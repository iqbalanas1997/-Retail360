import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  type CreateExpenseBody,
  type Expense,
  type ExpenseList,
  type ExpenseQuery,
  type SaveExpenseBody,
} from './expense.models';

@Injectable({ providedIn: 'root' })
export class ExpenseApi {
  private readonly http = inject(HttpClient);

  list(query: ExpenseQuery): Observable<ExpenseList> {
    let params = new HttpParams().set('limit', String(query.limit)).set('offset', String(query.offset));
    const from = query.from.trim();
    const to = query.to.trim();
    const category = query.category.trim();
    if (from) {
      params = params.set('from', from);
    }

    if (to) {
      params = params.set('to', to);
    }

    if (category) {
      params = params.set('category', category);
    }

    return this.http.get<ExpenseList>('/api/expenses', { params });
  }

  get(id: string): Observable<Expense> {
    return this.http.get<Expense>(`/api/expenses/${id}`);
  }

  create(body: CreateExpenseBody): Observable<Expense> {
    return this.http.post<Expense>('/api/expenses', body);
  }

  update(id: string, body: SaveExpenseBody): Observable<Expense> {
    return this.http.put<Expense>(`/api/expenses/${id}`, body);
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/expenses/${id}`);
  }
}
