import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  BudgetItem,
  BudgetUpsertRequest,
  DashboardCategoryItem,
  ExpenseCatalog,
  ExpenseEntryRequest,
  ExpenseItem,
  OperationResult,
  PagedExpenseResult,
  RecurringExpenseItem,
  RecurringExpenseUpsertRequest,
  UpdateCatalogsRequest
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = environment.apiBaseUrl;
  // local: http://localhost:5051
  // cloud: https://controleo-api.azurewebsites.net

  constructor(private readonly http: HttpClient) {}

  getCatalogs(): Observable<ExpenseCatalog> {
    return this.http.get<ExpenseCatalog>(`${this.baseUrl}/api/catalogs`);
  }

  updateCatalogs(request: UpdateCatalogsRequest): Observable<OperationResult> {
    return this.http.put<OperationResult>(`${this.baseUrl}/api/catalogs`, request);
  }

  getAvailableMonths(): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/api/expenses/months`);
  }

  getExpenses(month: string): Observable<ExpenseItem[]> {
    return this.http.get<ExpenseItem[]>(`${this.baseUrl}/api/expenses?month=${encodeURIComponent(month)}`);
  }

  getExpensesPage(month: string, pageNumber: number, pageSize: number, movementType?: string, searchTerm?: string): Observable<PagedExpenseResult> {
    const movement = movementType ? `&movementType=${encodeURIComponent(movementType)}` : '';
    const search = searchTerm ? `&searchTerm=${encodeURIComponent(searchTerm)}` : '';
    return this.http.get<PagedExpenseResult>(
      `${this.baseUrl}/api/expenses/paged?month=${encodeURIComponent(month)}&pageNumber=${pageNumber}&pageSize=${pageSize}${movement}${search}`
    );
  }

  saveExpense(request: ExpenseEntryRequest): Observable<OperationResult> {
    return this.http.post<OperationResult>(`${this.baseUrl}/api/expenses`, request);
  }

  updateExpense(id: string, request: ExpenseEntryRequest): Observable<OperationResult> {
    return this.http.put<OperationResult>(`${this.baseUrl}/api/expenses/${id}`, request);
  }

  deleteExpense(id: string): Observable<OperationResult> {
    return this.http.delete<OperationResult>(`${this.baseUrl}/api/expenses/${id}`);
  }

  getBudgets(): Observable<BudgetItem[]> {
    return this.http.get<BudgetItem[]>(`${this.baseUrl}/api/budgets`);
  }

  upsertBudget(movementType: string, request: BudgetUpsertRequest): Observable<OperationResult> {
    return this.http.put<OperationResult>(`${this.baseUrl}/api/budgets/${encodeURIComponent(movementType)}`, request);
  }

  deleteBudget(movementType: string): Observable<OperationResult> {
    return this.http.delete<OperationResult>(`${this.baseUrl}/api/budgets/${encodeURIComponent(movementType)}`);
  }

  getDashboardByCategory(month: string): Observable<DashboardCategoryItem[]> {
    return this.http.get<DashboardCategoryItem[]>(`${this.baseUrl}/api/dashboard/by-category?month=${encodeURIComponent(month)}`);
  }

  getRecurringExpenses(): Observable<RecurringExpenseItem[]> {
    return this.http.get<RecurringExpenseItem[]>(`${this.baseUrl}/api/recurring-expenses`);
  }

  createRecurringExpense(request: RecurringExpenseUpsertRequest): Observable<OperationResult> {
    return this.http.post<OperationResult>(`${this.baseUrl}/api/recurring-expenses`, request);
  }

  updateRecurringExpense(id: string, request: RecurringExpenseUpsertRequest): Observable<OperationResult> {
    return this.http.put<OperationResult>(`${this.baseUrl}/api/recurring-expenses/${id}`, request);
  }

  deleteRecurringExpense(id: string): Observable<OperationResult> {
    return this.http.delete<OperationResult>(`${this.baseUrl}/api/recurring-expenses/${id}`);
  }
}
