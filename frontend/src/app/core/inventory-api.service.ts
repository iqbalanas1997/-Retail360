import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import {
  type AdjustmentBody,
  type Area,
  type OpeningStockBody,
  type ProductStock,
  type Shelf,
  type ShelfBalance,
  type StockMovement,
  type TransferBody,
} from './inventory.models';

@Injectable({ providedIn: 'root' })
export class InventoryApi {
  private readonly http = inject(HttpClient);

  listAreas(): Observable<Area[]> {
    return this.http.get<Area[]>('/api/areas');
  }

  createArea(name: string): Observable<Area> {
    return this.http.post<Area>('/api/areas', { name });
  }

  renameArea(id: string, name: string): Observable<Area> {
    return this.http.put<Area>(`/api/areas/${id}`, { name });
  }

  activateArea(id: string): Observable<Area> {
    return this.http.post<Area>(`/api/areas/${id}/activate`, {});
  }

  deactivateArea(id: string): Observable<Area> {
    return this.http.post<Area>(`/api/areas/${id}/deactivate`, {});
  }

  deleteArea(id: string): Observable<void> {
    return this.http.delete<void>(`/api/areas/${id}`);
  }

  listShelves(areaId?: string): Observable<Shelf[]> {
    const params = areaId ? new HttpParams().set('areaId', areaId) : undefined;
    return this.http.get<Shelf[]>('/api/shelves', { params });
  }

  createShelf(areaId: string, name: string): Observable<Shelf> {
    return this.http.post<Shelf>('/api/shelves', { areaId, name });
  }

  renameShelf(id: string, name: string): Observable<Shelf> {
    return this.http.put<Shelf>(`/api/shelves/${id}`, { name });
  }

  activateShelf(id: string): Observable<Shelf> {
    return this.http.post<Shelf>(`/api/shelves/${id}/activate`, {});
  }

  deactivateShelf(id: string): Observable<Shelf> {
    return this.http.post<Shelf>(`/api/shelves/${id}/deactivate`, {});
  }

  deleteShelf(id: string): Observable<void> {
    return this.http.delete<void>(`/api/shelves/${id}`);
  }

  listStock(status: string, limit: number, offset: number): Observable<ProductStock[]> {
    const params = new HttpParams()
      .set('status', status)
      .set('limit', String(limit))
      .set('offset', String(offset));
    return this.http.get<ProductStock[]>('/api/inventory/stock', { params });
  }

  listBalances(productId?: string, shelfId?: string): Observable<ShelfBalance[]> {
    let params = new HttpParams();
    if (productId) {
      params = params.set('productId', productId);
    }

    if (shelfId) {
      params = params.set('shelfId', shelfId);
    }

    return this.http.get<ShelfBalance[]>('/api/inventory/balances', { params });
  }

  openStock(body: OpeningStockBody): Observable<StockMovement[]> {
    return this.http.post<StockMovement[]>('/api/inventory/opening', body);
  }

  adjustStock(body: AdjustmentBody): Observable<StockMovement[]> {
    return this.http.post<StockMovement[]>('/api/inventory/adjustments', body);
  }

  transferStock(body: TransferBody): Observable<StockMovement[]> {
    return this.http.post<StockMovement[]>('/api/inventory/transfers', body);
  }
}
