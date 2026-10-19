export interface SaleLine {
  id: string;
  productId: string;
  productName: string;
  shelfId: string;
  shelfName: string;
  quantity: number;
  unitPrice: number;
  lineTotal: number;
}

export interface Sale {
  id: string;
  receiptNumber: number;
  soldAt: string;
  paymentMethod: string;
  total: number;
  lines: SaleLine[];
  createdAt: string;
  updatedAt: string;
}

export interface CompleteSaleLineBody {
  productId: string;
  shelfId: string;
  quantity: number;
}

export interface CompleteSaleBody {
  operationId: string;
  lines: CompleteSaleLineBody[];
}

export interface SaleQuery {
  receiptNumber: number | null;
  limit: number;
  offset: number;
}
