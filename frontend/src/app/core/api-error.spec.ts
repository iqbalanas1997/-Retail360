import { HttpErrorResponse } from '@angular/common/http';

import { toApiError } from './api-error';

describe('API errors', () => {
  it('shows the server message and a proxy failure', () => {
    expect(
      toApiError(new HttpErrorResponse({ status: 409, error: { error: 'Barcode is already assigned to a product.' } }))
        .message,
    ).toBe('Barcode is already assigned to a product.');
    expect(toApiError(new HttpErrorResponse({ status: 403, error: { error: 'You do not have access to this action.' } })).message).toBe(
      'You do not have access to this action.',
    );
    expect(toApiError(new HttpErrorResponse({ status: 500, error: 'proxy failed' })).message).toBe(
      'The shop API could not be reached.',
    );
    expect(
      toApiError(new HttpErrorResponse({ status: 500, error: { error: 'The request could not be completed.' } })).message,
    ).toBe('The request could not be completed.');
    expect(toApiError(new HttpErrorResponse({ status: 400, error: { title: 'One or more validation errors occurred.' } })).message).toBe(
      'The request failed.',
    );
  });

  it('keeps every named shortage from a sale conflict', () => {
    const failure = toApiError(
      new HttpErrorResponse({
        status: 409,
        error: {
          error: 'Shelf does not have enough stock.',
          shortages: [
            {
              productId: 'product-1',
              productName: 'Basmati Rice',
              shelfId: 'shelf-1',
              shelfName: 'Shop Floor / Shelf A',
              required: 10,
              available: 6,
            },
            {
              productId: 'product-2',
              productName: 'Milk',
              shelfId: 'shelf-2',
              shelfName: 'Shop Floor / Shelf C',
              required: 4,
              available: 2,
            },
          ],
        },
      }),
    );

    expect(failure.message).toBe('Shelf does not have enough stock.');
    expect(failure.shortages).toEqual([
      {
        productId: 'product-1',
        productName: 'Basmati Rice',
        shelfId: 'shelf-1',
        shelfName: 'Shop Floor / Shelf A',
        required: 10,
        available: 6,
      },
      {
        productId: 'product-2',
        productName: 'Milk',
        shelfId: 'shelf-2',
        shelfName: 'Shop Floor / Shelf C',
        required: 4,
        available: 2,
      },
    ]);
  });
});