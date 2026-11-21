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
  outstanding: number;
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

export interface CustomerBalance {
  customerId: string;
  outstanding: number;
}

export interface CustomerPaymentAllocation {
  saleId: string;
  receiptNumber: number;
  amount: number;
}

export interface CustomerPayment {
  id: string;
  customerId: string;
  amount: number;
  occurredAt: string;
  outstanding: number;
  allocations: CustomerPaymentAllocation[];
}

export interface RecordCustomerPaymentBody {
  operationId: string;
  amount: number;
}

export interface CustomerSale {
  id: string;
  receiptNumber: number;
  soldAt: string;
  paymentMethod: string;
  total: number;
}
