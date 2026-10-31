export interface Customer {
  id: string;
  name: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface SaveCustomerBody {
  name: string;
  phone: string | null;
  email: string | null;
  address: string | null;
  notes: string | null;
}

export interface CustomerQuery {
  search: string;
  activeOnly: boolean;
  limit: number;
  offset: number;
}

export interface CustomerSale {
  id: string;
  receiptNumber: number;
  soldAt: string;
  paymentMethod: string;
  total: number;
}
