export const SHOP_ROLES = ['Admin', 'Manager', 'Cashier', 'Inventory Staff'] as const;

export interface ShopUser {
  id: string;
  userName: string;
  role: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SaveUserBody {
  userName: string;
  role: string;
  password: string | null;
}

export interface UserQuery {
  name: string;
  activeOnly: boolean;
  limit: number;
  offset: number;
}
