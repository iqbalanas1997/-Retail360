export interface SalesSummary {
  count: number;
  total: number;
  average: number | null;
}

export interface ExpenseCategoryTotal {
  category: string;
  count: number;
  total: number;
}

export interface ExpenseSummary {
  count: number;
  total: number;
  categories: ExpenseCategoryTotal[];
}

export interface PurchaseSummary {
  recordedCount: number;
  recordedTotal: number;
  receivedCount: number;
  receivedTotal: number;
}

export interface RecentSale {
  id: string;
  receiptNumber: number;
  soldAt: string;
  total: number;
}

export interface RecentExpense {
  id: string;
  category: string;
  expenseDate: string;
  amount: number;
}

export interface DashboardReport {
  from: string;
  to: string;
  sales: SalesSummary;
  expenses: ExpenseSummary;
  purchases: PurchaseSummary;
  recentSales: RecentSale[];
  recentExpenses: RecentExpense[];
}

export interface LowStockProduct {
  id: string;
  name: string;
  onHand: number;
  minimumStock: number;
}

export interface LowStockReport {
  count: number;
  products: LowStockProduct[];
}
