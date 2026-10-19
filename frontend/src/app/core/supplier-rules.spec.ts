import { optionalEmail, optionalPhone, supplierName } from './supplier-rules';

describe('supplier rules', () => {
  it('trims the name and treats a blank phone as missing', () => {
    expect(supplierName('  Sufi  ')).toBe('Sufi');
    expect(optionalPhone('   ')).toBeNull();
    expect(optionalPhone(' 0300 ')).toBe('0300');
  });

  it('rejects a missing name, a long phone, and an email that is not one address', () => {
    expect(() => supplierName(' ')).toThrow('Name must be between 1 and 200 characters.');
    expect(() => optionalPhone('1'.repeat(41))).toThrow('Phone must be between 1 and 40 characters.');
    expect(optionalEmail(' shop@example.com ')).toBe('shop@example.com');
    expect(optionalEmail('  ')).toBeNull();
    expect(() => optionalEmail('not-an-email')).toThrow(
      'Email must be empty or a single address of at most 200 characters.',
    );
    expect(() => optionalEmail('a@b')).toThrow(
      'Email must be empty or a single address of at most 200 characters.',
    );
  });
});
