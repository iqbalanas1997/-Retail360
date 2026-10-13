import { HttpErrorResponse } from '@angular/common/http';

export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
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
      return new ApiError(error.status, serverMessage);
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
