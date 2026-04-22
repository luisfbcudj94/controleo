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

// ── Goals ──

export interface SavingsGoalItem {
  id: string;
  name: string;
  icon: string;
  targetAmount: number;
  currentAmount: number;
  targetDate: string;
  status: string;
  priority: string;
  progressPercent: number;
  suggestedMonthlyContribution: number;
  projectedCompletionDate: string | null;
  daysRemaining: number;
  isOnTrack: boolean;
  monthlyAvailableSavings: number | null;
  contributions: GoalContributionItem[];
  createdAt: string;
  updatedAt: string;
}

export interface GoalContributionItem {
  id: string;
  goalId: string;
  amount: number;
  date: string;
  note: string | null;
  createdAt: string;
}

export interface GoalUpsertRequest {
  name: string;
  icon: string;
  targetAmount: number;
  targetDate: string;
  priority: string;
  status?: string;
}

export interface GoalContributionRequest {
  amount: number;
  date: string;
  note?: string;
}

export interface GoalSimulationRequest {
  scenarioType: string;
  newValue: number;
}

export interface GoalSimulationResponse {
  projectedDate: string | null;
  requiredMonthlyAmount: number;
  feasibility: string;
  description: string;
}

export interface GoalAlertItem {
  goalId: string;
  goalName: string;
  message: string;
  severity: string;
}

// ── Coach IA ──

export interface ChatMessageItem {
  role: string;
  content: string;
  createdAt: string;
}

export interface ChatHistoryResult {
  messages: ChatMessageItem[];
}

export interface CoachSuggestionsResult {
  suggestions: string[];
}
