import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import {
  BudgetItem,
  BudgetUpsertRequest,
  DashboardCategoryItem,
  ExpenseCatalog,
  ExpenseEntryRequest,
  OperationResult,
  PagedExpenseResult,
  UpdateCatalogsRequest
} from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly baseUrl = 'http://localhost:5051';

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

  getExpensesPage(month: string, pageNumber: number, pageSize: number, movementType?: string): Observable<PagedExpenseResult> {
    const movement = movementType ? `&movementType=${encodeURIComponent(movementType)}` : '';
    return this.http.get<PagedExpenseResult>(
      `${this.baseUrl}/api/expenses/paged?month=${encodeURIComponent(month)}&pageNumber=${pageNumber}&pageSize=${pageSize}${movement}`
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
}
