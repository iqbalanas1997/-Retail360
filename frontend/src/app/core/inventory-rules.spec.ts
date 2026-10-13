import { OperationAttempt, parseAdjustmentQuantity, parsePositiveQuantity, parseReason } from './inventory-rules';

describe('inventory quantity rules', () => {
  it('accepts a stock quantity and rejects zero, an extra digit, and the next value', () => {
    expect(parsePositiveQuantity('1.230')).toBe(1.23);
    expect(parsePositiveQuantity('99999999999.999')).toBe(99999999999.999);
    expect(() => parsePositiveQuantity('0')).toThrow(
      'Quantity must be greater than zero with at most 3 decimal places.',
    );
    expect(() => parsePositiveQuantity('1.2345')).toThrow(
      'Quantity must be greater than zero with at most 3 decimal places.',
    );
    expect(parseAdjustmentQuantity('-1.500')).toBe(-1.5);
    expect(() => parseAdjustmentQuantity('0')).toThrow(
      'Adjustment quantity must be non-zero with at most 3 decimal places.',
    );
    expect(parseReason(' damaged ', true)).toBe('damaged');
    expect(parseReason('   ', false)).toBe('');
  });
});

describe('operation attempts', () => {
  it('reuses one id for the same submission and creates a new id when the payload changes', () => {
    const ids = ['first-id', 'second-id'];
    const attempt = new OperationAttempt(() => ids.shift() ?? 'fallback');
    const first = JSON.stringify({ quantity: 1 });
    const second = JSON.stringify({ quantity: 2 });

    expect(attempt.idFor(first)).toBe('first-id');
    expect(attempt.idFor(first)).toBe('first-id');
    expect(attempt.idFor(second)).toBe('second-id');
    attempt.complete();
    expect(attempt.idFor(second)).toBe('fallback');
  });
});
