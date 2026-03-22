import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ExpenseItem } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';

@Component({
  selector: 'app-expenses-page',
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './expenses-page.component.html',
  styleUrl: './expenses-page.component.scss'
})
export class ExpensesPageComponent {
  readonly allowedPageSizes = [5, 10, 20];
  readonly icons = ['🛒', '🏗️', '💳', '🍽️', '✈️', '💊', '🏠', '📦'];
  readonly palette = [
    { color: 'var(--amber)', bg: 'var(--amb2)' },
    { color: 'var(--info)', bg: 'var(--inf2)' },
    { color: 'var(--purple)', bg: 'var(--pur2)' },
    { color: 'var(--teal)', bg: 'var(--tea2)' },
    { color: 'var(--coral)', bg: 'var(--cor2)' },
    { color: 'var(--accent)', bg: 'var(--ac2)' }
  ];

  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  items: ExpenseItem[] = [];
  pageNumber = 1;
  pageSize = 5;
  totalPages = 1;
  totalCount = 0;
  isLoading = false;
  statusMessage = '';
  editingExpense: ExpenseItem | null = null;
  activeMovementType = 'all';
  searchTerm = '';

  readonly editForm;

  movementTypes: string[] = [];
  paymentMethods: string[] = [];

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder
  ) {
    this.editForm = this.fb.nonNullable.group({
      date: ['', Validators.required],
      description: ['', Validators.required],
      amount: [0, Validators.required],
      movementType: ['', Validators.required],
      paymentMethod: ['', Validators.required]
    });

    this.loadCatalogs();
    this.refreshMonthsAndLoad();
  }

  get monthLabelText(): string {
    return monthLabel(this.selectedMonth);
  }

  get paginationText(): string {
    return `Página ${this.pageNumber}/${this.totalPages} · ${this.totalCount} registros`;
  }

  get filteredItems(): ExpenseItem[] {
    return this.items.filter((item) => {
      const byType = this.activeMovementType === 'all' || item.movementType === this.activeMovementType;
      const bySearch = !this.searchTerm || item.description.toLowerCase().includes(this.searchTerm.toLowerCase());
      return byType && bySearch;
    });
  }

  get totalFilteredAmount(): number {
    return this.filteredItems.reduce((sum, item) => sum + item.amount, 0);
  }

  get maxFilteredAmount(): number {
    return this.filteredItems.length ? Math.max(...this.filteredItems.map((item) => item.amount)) : 0;
  }

  get averageFilteredAmount(): number {
    return this.filteredItems.length ? this.totalFilteredAmount / this.filteredItems.length : 0;
  }

  get movementTypeFilters(): string[] {
    return Array.from(new Set(this.items.map((item) => item.movementType))).slice(0, 6);
  }

  previousMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index > 0) {
      this.selectedMonth = this.months[index - 1];
      this.pageNumber = 1;
      this.loadPage();
    }
  }

  nextMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index >= 0 && index < this.months.length - 1) {
      this.selectedMonth = this.months[index + 1];
      this.pageNumber = 1;
      this.loadPage();
    }
  }

  setPageSize(size: number): void {
    this.pageSize = size;
    this.pageNumber = 1;
    this.loadPage();
  }

  prevPage(): void {
    if (this.pageNumber <= 1) {
      return;
    }

    this.pageNumber -= 1;
    this.loadPage();
  }

  nextPage(): void {
    if (this.pageNumber >= this.totalPages) {
      return;
    }

    this.pageNumber += 1;
    this.loadPage();
  }

  openEdit(item: ExpenseItem): void {
    this.editingExpense = item;
    this.editForm.patchValue({
      date: item.date,
      description: item.description,
      amount: item.amount,
      movementType: item.movementType,
      paymentMethod: item.paymentMethod
    });
  }

  cancelEdit(): void {
    this.editingExpense = null;
  }

  saveEdit(): void {
    if (!this.editingExpense || this.editForm.invalid) {
      return;
    }

    const value = this.editForm.getRawValue();
    this.api
      .updateExpense(this.editingExpense.id, {
        date: value.date,
        description: value.description,
        amount: Number(value.amount),
        movementType: value.movementType,
        paymentMethod: value.paymentMethod
      })
      .subscribe({
        next: (result) => {
          this.statusMessage = result.message;
          this.editingExpense = null;
          this.loadPage();
        },
        error: () => (this.statusMessage = 'No fue posible actualizar el gasto.')
      });
  }

  delete(item: ExpenseItem): void {
    if (!confirm(`¿Eliminar '${item.description}'?`)) {
      return;
    }

    this.api.deleteExpense(item.id).subscribe({
      next: (result) => {
        this.statusMessage = result.message;
        this.loadPage();
      },
      error: () => (this.statusMessage = 'No fue posible eliminar el gasto.')
    });
  }

  setFilter(value: string): void {
    this.activeMovementType = value;
  }

  updateSearch(value: string): void {
    this.searchTerm = value;
  }

  colorFor(item: ExpenseItem): string {
    return this.palette[this.paletteIndex(item)].color;
  }

  bgFor(item: ExpenseItem): string {
    return this.palette[this.paletteIndex(item)].bg;
  }

  iconFor(item: ExpenseItem): string {
    return this.icons[this.paletteIndex(item) % this.icons.length];
  }

  trackById(_: number, item: ExpenseItem): string {
    return item.id;
  }

  private paletteIndex(item: ExpenseItem): number {
    const key = this.movementTypes.findIndex((x) => x === item.movementType);
    return key >= 0 ? key % this.palette.length : Math.abs(item.movementType.length) % this.palette.length;
  }

  private refreshMonthsAndLoad(): void {
    this.api.getAvailableMonths().subscribe({
      next: (months) => {
        this.months = months;
        this.selectedMonth = firstMonthOfAvailable(months);
        this.loadPage();
      },
      error: () => {
        this.months = [monthKeyFromDate(new Date())];
        this.selectedMonth = this.months[0];
        this.loadPage();
      }
    });
  }

  private loadCatalogs(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.paymentMethods = catalog.paymentMethods;
      }
    });
  }

  private loadPage(): void {
    this.isLoading = true;
    this.api
      .getExpensesPage(this.selectedMonth, this.pageNumber, this.pageSize)
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (page) => {
          this.items = page.items;
          this.pageNumber = page.pageNumber;
          this.totalPages = page.totalPages;
          this.totalCount = page.totalCount;
        },
        error: () => {
          this.statusMessage = 'No fue posible cargar gastos.';
        }
      });
  }

}
