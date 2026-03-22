export interface ExpenseCatalog {
  movementTypes: string[];
  paymentMethods: string[];
}

export interface ExpenseItem {
  id: string;
  date: string;
  description: string;
  amount: number;
  movementType: string;
  paymentMethod: string;
  createdAt: string;
  updatedAt: string;
}

export interface PagedExpenseResult {
  items: ExpenseItem[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalAmount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface ExpenseEntryRequest {
  date: string;
  description: string;
  amount: number;
  movementType: string;
  paymentMethod: string;
}

export interface OperationResult {
  isSuccess: boolean;
  message: string;
}

export interface BudgetItem {
  movementType: string;
  amount: number;
  updatedAt: string;
}

export interface BudgetUpsertRequest {
  amount: number;
}

export interface DashboardCategoryItem {
  movementType: string;
  expenseTotal: number;
  budgetTotal: number;
  balance: number;
}

export interface UpdateCatalogsRequest {
  movementTypes: string[];
  paymentMethods: string[];
}
