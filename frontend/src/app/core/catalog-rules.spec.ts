import { parseBarcodes, parseMinimumStock, parseName, parsePrice } from './catalog-rules';

describe('catalog rules', () => {
  it('accepts the price and stock limits and rejects an extra digit', () => {
    expect(parsePrice('0')).toBe(0);
    expect(parsePrice('1.20')).toBe(1.2);
    expect(parsePrice('9999999999.99')).toBe(9999999999.99);
    expect(parseMinimumStock('1.230')).toBe(1.23);
    expect(parseMinimumStock('99999999999.999')).toBe(99999999999.999);
    expect(() => parsePrice('1.235')).toThrow('Price must be zero or greater with at most 2 decimal places.');
    expect(() => parsePrice('10000000000.00')).toThrow(
      'Price must be zero or greater with at most 2 decimal places.',
    );
    expect(() => parseMinimumStock('1.2345')).toThrow(
      'Minimum stock must be zero or greater with at most 3 decimal places.',
    );
  });

  it('rejects a duplicated barcode and a name that is too long', () => {
    expect(parseBarcodes([' 8901 ', ''])).toEqual(['8901']);
    expect(() => parseBarcodes(['8901', '8901'])).toThrow('Barcode is duplicated on the product.');
    expect(() => parseName(` ${'a'.repeat(201)} `)).toThrow('Name must be between 1 and 200 characters.');
  });
});
