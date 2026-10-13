export interface NamedCatalogItem {
  id: string;
  name: string;
  createdAt: string;
  updatedAt: string;
}

export interface Product {
  id: string;
  name: string;
  categoryId: string | null;
  categoryName: string | null;
  brandId: string | null;
  brandName: string | null;
  unitId: string;
  unitName: string;
  purchasePrice: number;
  sellingPrice: number;
  minimumStock: number;
  isActive: boolean;
  barcodes: string[];
  createdAt: string;
  updatedAt: string;
}

export interface SaveProductBody {
  name: string;
  categoryId: string | null;
  brandId: string | null;
  unitId: string;
  purchasePrice: number;
  sellingPrice: number;
  minimumStock: number;
  barcodes: string[];
}

export interface ProductQuery {
  name: string;
  activeOnly: boolean;
  limit: number;
  offset: number;
}
