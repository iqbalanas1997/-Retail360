import {
  parsePurchaseDate,
  parsePurchaseNotes,
  parsePurchaseQuantity,
  parsePurchaseReference,
  parseUnitCost,
  purchaseLineTotal,
  purchaseTotal,
} from './purchase-rules';

describe('purchase rules', () => {
  it('rounds a line total half away from zero', () => {
    expect(purchaseLineTotal(parsePurchaseQuantity('1.250'), parseUnitCost('0.10'))).toBe(0.13);
    expect(purchaseTotal([0.13, 1.1])).toBe(1.23);
  });

  it('rejects extra fractional digits, an empty document, and a long reference', () => {
    expect(() => parsePurchaseQuantity('1.2501')).toThrow(/3 decimal places/);
    expect(() => parseUnitCost('1.001')).toThrow(/2 decimal places/);
    expect(parsePurchaseReference('  INV-1  ')).toBe('INV-1');
    expect(parsePurchaseNotes('   ')).toBeNull();
    expect(() => parsePurchaseReference('x'.repeat(41))).toThrow(/40/);
    expect(() => parsePurchaseDate('')).toThrow(/required/);
    expect(() => purchaseTotal([])).toThrow(/at least one line/);
  });
});
