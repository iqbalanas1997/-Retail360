import { readRoleClaims, readStoredSession, writeStoredSession } from './shop-session';

describe('shop session', () => {
  beforeEach(() => {
    sessionStorage.clear();
  });

  it('reads role claims and drops an expired session', () => {
    expect(readRoleClaims(token({ role: 'Admin' }))).toEqual(['Admin']);
    expect(readRoleClaims(token({ role: ['Manager', 'Cashier'] }))).toEqual(['Manager', 'Cashier']);

    writeStoredSession({
      accessToken: token({ role: 'Admin' }),
      expiresAt: new Date(Date.now() - 1000).toISOString(),
      userId: 'user-1',
      userName: 'admin',
      roles: ['Admin'],
    });

    expect(readStoredSession()).toBeNull();
    expect(sessionStorage.getItem('retail360.session')).toBeNull();
  });

  it('stores the access token without a password field', () => {
    writeStoredSession({
      accessToken: token({ role: 'Inventory Staff' }),
      expiresAt: new Date(Date.now() + 60_000).toISOString(),
      userId: 'user-1',
      userName: 'stock',
      roles: ['Inventory Staff'],
    });

    const stored = sessionStorage.getItem('retail360.session') ?? '';
    expect(stored).not.toContain('password');
    expect(readStoredSession()?.userName).toBe('stock');
  });
});

function token(payload: unknown): string {
  const body = btoa(JSON.stringify(payload)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/g, '');
  return `header.${body}.signature`;
}
