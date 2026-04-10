export interface MovementTypeConfig {
  name: string;
  icon: string;
  color: string;
}

export interface PaymentMethodConfig {
  name: string;
  icon: string;
}

export interface ExpenseCatalog {
  movementTypes: string[];
  paymentMethods: string[];
  movementTypeConfigs?: MovementTypeConfig[];
  paymentMethodConfigs?: PaymentMethodConfig[];
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
  isPremium: boolean;
  isAdmin: boolean;
  isImpersonating?: boolean;
  actorUserId?: string | null;
  actorName?: string | null;
  actorEmail?: string | null;
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

export interface DashboardPaymentMethodItem {
  paymentMethod: string;
  expenseTotal: number;
}

export interface UpdateCatalogsRequest {
  movementTypes: string[];
  paymentMethods: string[];
  movementTypeConfigs?: MovementTypeConfig[];
  paymentMethodConfigs?: PaymentMethodConfig[];
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

export interface AdminUserItem {
  userId: string;
  name: string;
  email: string;
  isPremium: boolean;
  isAdmin: boolean;
  isSuperAdmin: boolean;
  isDisabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastLoginAt: string;
}

export interface AdminUserUpdateRequest {
  isPremium: boolean;
  isAdmin: boolean;
  isDisabled: boolean;
}

export interface PagedAdminUsersResult {
  items: AdminUserItem[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}
