export interface Area {
  id: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface Shelf {
  id: string;
  areaId: string;
  areaName: string;
  name: string;
  isActive: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface ProductStock {
  productId: string;
  productName: string;
  productIsActive: boolean;
  onHand: number;
  minimumStock: number;
  status: string;
}

export interface ShelfBalance {
  productId: string;
  productName: string;
  shelfId: string;
  shelfName: string;
  areaId: string;
  areaName: string;
  quantity: number;
  shelfIsActive: boolean;
}

export interface StockMovement {
  id: string;
  operationId: string;
  productId: string;
  shelfId: string;
  quantity: number;
  direction: string;
  movementType: string;
  reason: string;
  otherShelfId: string | null;
  occurredAt: string;
}

export interface OpeningStockBody {
  operationId: string;
  productId: string;
  shelfId: string;
  quantity: number;
  reason: string;
}

export interface AdjustmentBody {
  operationId: string;
  productId: string;
  shelfId: string;
  quantity: number;
  reason: string;
}

export interface TransferBody {
  operationId: string;
  productId: string;
  sourceShelfId: string;
  destinationShelfId: string;
  quantity: number;
  reason: string;
}
