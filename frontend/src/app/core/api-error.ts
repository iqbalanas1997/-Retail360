import { HttpErrorResponse } from '@angular/common/http';

export interface StockShortage {
  productId: string;
  productName: string;
  shelfId: string;
  shelfName: string;
  required: number;
  available: number;
}

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
    readonly shortages: StockShortage[] = [],
  ) {
    super(message);
  }
}

export function toApiError(error: unknown): ApiError {
  if (error instanceof ApiError) {
    return error;
  }

  if (error instanceof HttpErrorResponse) {
    const body: unknown = error.error;
    const serverMessage =
      typeof body === 'object' && body !== null && 'error' in body && typeof body.error === 'string'
        ? body.error
        : '';
    if (serverMessage) {
      return new ApiError(error.status, serverMessage, readShortages(body));
    }

    if (error.status === 0 || error.status >= 500) {
      return new ApiError(error.status, 'The shop API could not be reached.');
    }

    if (error.status === 401) {
      return new ApiError(401, 'Authentication is required.');
    }

    if (error.status === 403) {
      return new ApiError(403, 'You do not have access to this action.');
    }

    return new ApiError(error.status, 'The request failed.');
  }

  return new ApiError(0, 'The request failed.');
}

function readShortages(body: unknown): StockShortage[] {
  if (typeof body !== 'object' || body === null || !('shortages' in body) || !Array.isArray(body.shortages)) {
    return [];
  }

  return body.shortages.flatMap((item) => {
    if (typeof item !== 'object' || item === null) {
      return [];
    }

    const row = item as Record<string, unknown>;
    const required = typeof row['required'] === 'number' ? row['required'] : Number(row['required']);
    const available = typeof row['available'] === 'number' ? row['available'] : Number(row['available']);
    if (typeof row['productId'] !== 'string' || typeof row['shelfId'] !== 'string') {
      return [];
    }

    if (!Number.isFinite(required) || !Number.isFinite(available)) {
      return [];
    }

    return [
      {
        productId: row['productId'],
        productName: typeof row['productName'] === 'string' ? row['productName'] : '',
        shelfId: row['shelfId'],
        shelfName: typeof row['shelfName'] === 'string' ? row['shelfName'] : '',
        required,
        available,
      },
    ];
  });
}
