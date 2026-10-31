import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { type AuditEntry, type AuditQuery } from './audit.models';

@Injectable({ providedIn: 'root' })
export class AuditApi {
  private readonly http = inject(HttpClient);

  list(query: AuditQuery): Observable<AuditEntry[]> {
    let params = new HttpParams().set('limit', String(query.limit)).set('offset', String(query.offset));
    const from = query.from.trim();
    const to = query.to.trim();
    const actorUserId = query.actorUserId.trim();
    const action = query.action.trim();
    const entityType = query.entityType.trim();
    if (from) {
      params = params.set('from', from);
    }

    if (to) {
      params = params.set('to', to);
    }

    if (actorUserId) {
      params = params.set('actorUserId', actorUserId);
    }

    if (action) {
      params = params.set('action', action);
    }

    if (entityType) {
      params = params.set('entityType', entityType);
    }

    return this.http.get<AuditEntry[]>('/api/audit', { params });
  }
}
