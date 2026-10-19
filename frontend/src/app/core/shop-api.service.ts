import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type Product, type ProductQuery, type SaveProductBody, type NamedCatalogItem } from './catalog.models';
import { type LoginResponse } from './shop-session';

@Injectable({ providedIn: 'root' })
export class ShopApi {
  private readonly http = inject(HttpClient);

  login(userName: string, password: string): Observable<LoginResponse> {
    return this.http.post<LoginResponse>('/api/auth/login', { userName, password });
  }

  listProducts(query: ProductQuery): Observable<Product[]> {
    let params = new HttpParams()
      .set('activeOnly', String(query.activeOnly))
      .set('limit', String(query.limit))
      .set('offset', String(query.offset));
    const name = query.name.trim();
    if (name) {
      params = params.set('name', name);
    }

    return this.http.get<Product[]>('/api/products', { params });
  }

  getProduct(id: string): Observable<Product> {
    return this.http.get<Product>(`/api/products/${id}`);
  }

  findProductByBarcode(barcode: string): Observable<Product> {
    return this.http.get<Product>(`/api/products/by-barcode/${encodeURIComponent(barcode)}`);
  }

  createProduct(body: SaveProductBody): Observable<Product> {
    return this.http.post<Product>('/api/products', body);
  }

  updateProduct(id: string, body: SaveProductBody): Observable<Product> {
    return this.http.put<Product>(`/api/products/${id}`, body);
  }

  activateProduct(id: string): Observable<Product> {
    return this.http.post<Product>(`/api/products/${id}/activate`, {});
  }

  deactivateProduct(id: string): Observable<Product> {
    return this.http.post<Product>(`/api/products/${id}/deactivate`, {});
  }

  deleteProduct(id: string): Observable<void> {
    return this.http.delete<void>(`/api/products/${id}`);
  }

  listNamed(path: string): Observable<NamedCatalogItem[]> {
    return this.http.get<NamedCatalogItem[]>(path);
  }

  createNamed(path: string, name: string): Observable<NamedCatalogItem> {
    return this.http.post<NamedCatalogItem>(path, { name });
  }

  updateNamed(path: string, id: string, name: string): Observable<NamedCatalogItem> {
    return this.http.put<NamedCatalogItem>(`${path}/${id}`, { name });
  }

  deleteNamed(path: string, id: string): Observable<void> {
    return this.http.delete<void>(`${path}/${id}`);
  }
}
