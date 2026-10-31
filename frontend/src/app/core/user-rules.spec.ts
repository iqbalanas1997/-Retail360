import { optionalPassword, requirePassword, requireRole, requireUserName } from './user-rules';

describe('user rules', () => {
  it('accepts a trimmed user name and one shop role', () => {
    expect(requireUserName('  cashier  ')).toBe('cashier');
    expect(requireRole('Inventory Staff')).toBe('Inventory Staff');
    expect(optionalPassword('')).toBeNull();
    expect(requirePassword('correct-horse')).toBe('correct-horse');
  });

  it('rejects a blank name, an unknown role, and a short password', () => {
    expect(() => requireUserName('   ')).toThrow('User name must be between 1 and 256 characters.');
    expect(() => requireRole('Owner')).toThrow('Role must be Admin, Manager, Cashier, or Inventory Staff.');
    expect(() => requirePassword('short')).toThrow('Password must be between 8 and 128 characters.');
    expect(() => optionalPassword('short')).toThrow('Password must be between 8 and 128 characters.');
  });
});
