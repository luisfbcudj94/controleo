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

export interface AuthUserProfile {
  userId: string;
  name: string;
  email: string;
}

export interface AuthSessionResponse {
  accessToken: string;
  expiresAt: string;
  user: AuthUserProfile;
}

export interface RegisterRequest {
  name: string;
  email: string;
  password: string;
}

export interface LoginRequest {
  email: string;
  password: string;
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

export interface RecurringExpenseItem {
  id: string;
  description: string;
  amount: number;
  movementType: string;
  paymentMethod: string;
  dayOfMonth: number;
  startDate?: string;
  startMonth?: string;
  isActive: boolean;
}

export interface RecurringExpenseUpsertRequest {
  description: string;
  amount: number;
  movementType: string;
  paymentMethod: string;
  dayOfMonth: number;
  startDate: string;
  isActive: boolean;
}
