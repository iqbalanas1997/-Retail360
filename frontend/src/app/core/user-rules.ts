import { SHOP_ROLES } from './user.models';

export function requireUserName(value: string): string {
  const userName = value.trim();
  if (userName.length < 1 || userName.length > 256) {
    throw new Error('User name must be between 1 and 256 characters.');
  }

  return userName;
}

export function requireRole(value: string): string {
  if (!SHOP_ROLES.some((role) => role === value)) {
    throw new Error('Role must be Admin, Manager, Cashier, or Inventory Staff.');
  }

  return value;
}

export function requirePassword(value: string): string {
  if (value.length < 8 || value.length > 128) {
    throw new Error('Password must be between 8 and 128 characters.');
  }

  return value;
}

export function optionalPassword(value: string): string | null {
  if (value.length === 0) {
    return null;
  }

  return requirePassword(value);
}
