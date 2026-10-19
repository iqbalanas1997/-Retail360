import { formatSoldAt, parseReceiptNumber, parseSaleQuantity, saleLineTotal, saleTotal } from './sale-rules';

describe('sale rules', () => {
  it('rounds a line total half away from zero and sums the sale', () => {
    const line = saleLineTotal(parseSaleQuantity('1.250'), 0.1);

    expect(line).toBe(0.13);
    expect(saleTotal([line, saleLineTotal(1, 35)])).toBe(35.13);
  });

  it('rejects an extra quantity digit and a blank receipt filter', () => {
    expect(() => parseSaleQuantity('1.2501')).toThrow(
      'Quantity must be greater than zero with at most 3 decimal places.',
    );
    expect(parseReceiptNumber('  ')).toBeNull();
    expect(parseReceiptNumber('1')).toBe(1);
    expect(() => parseReceiptNumber('0')).toThrow('Receipt number must be greater than zero.');
    expect(formatSoldAt('2026-10-19T21:05:46.5421+00:00')).toBe('2026-10-19 21:05:46 UTC');
  });
});
