export interface PurchaseLine {
  id: string;
  productId: string;
  productName: string;
  quantity: number;
  unitCost: number;
  lineTotal: number;
  shelfId: string | null;
}

export interface Purchase {
  id: string;
  supplierId: string;
  supplierName: string;
  purchaseDate: string;
  reference: string | null;
  notes: string | null;
  status: string;
  total: number;
  receiveOperationId: string | null;
  receivedAt: string | null;
  lines: PurchaseLine[];
  createdAt: string;
  updatedAt: string;
}

export interface SavePurchaseLineBody {
  productId: string;
  quantity: number;
  unitCost: number;
}

export interface SavePurchaseBody {
  supplierId: string;
  purchaseDate: string;
  reference: string | null;
  notes: string | null;
  lines: SavePurchaseLineBody[];
}

export interface ReceivePurchaseLineBody {
  lineId: string;
  shelfId: string;
}

export interface ReceivePurchaseBody {
  operationId: string;
  lines: ReceivePurchaseLineBody[];
}

export interface PurchaseQuery {
  supplierId: string;
  status: string;
  limit: number;
  offset: number;
}
