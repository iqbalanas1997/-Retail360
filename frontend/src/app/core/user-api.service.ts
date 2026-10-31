import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type SaveUserBody, type ShopUser, type UserQuery } from './user.models';

@Injectable({ providedIn: 'root' })
export class UserApi {
  private readonly http = inject(HttpClient);

  list(query: UserQuery): Observable<ShopUser[]> {
    let params = new HttpParams()
      .set('activeOnly', String(query.activeOnly))
      .set('limit', String(query.limit))
      .set('offset', String(query.offset));
    const name = query.name.trim();
    if (name) {
      params = params.set('name', name);
    }

    return this.http.get<ShopUser[]>('/api/users', { params });
  }

  get(id: string): Observable<ShopUser> {
    return this.http.get<ShopUser>(`/api/users/${id}`);
  }

  create(body: SaveUserBody): Observable<ShopUser> {
    return this.http.post<ShopUser>('/api/users', body);
  }

  update(id: string, body: SaveUserBody): Observable<ShopUser> {
    return this.http.put<ShopUser>(`/api/users/${id}`, body);
  }

  activate(id: string): Observable<ShopUser> {
    return this.http.post<ShopUser>(`/api/users/${id}/activate`, {});
  }

  deactivate(id: string): Observable<ShopUser> {
    return this.http.post<ShopUser>(`/api/users/${id}/deactivate`, {});
  }

  delete(id: string): Observable<void> {
    return this.http.delete<void>(`/api/users/${id}`);
  }
}
