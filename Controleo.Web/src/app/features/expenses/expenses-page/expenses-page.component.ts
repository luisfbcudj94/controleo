import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ExpenseItem } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import { SelectorModalComponent } from '../../../shared/ui/selector-modal/selector-modal.component';
import { CustomDateInputComponent } from '../../../shared/ui/custom-date-input/custom-date-input.component';

@Component({
  selector: 'app-expenses-page',
  imports: [CommonModule, ReactiveFormsModule, ConfirmModalComponent, SelectorModalComponent, CustomDateInputComponent],
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
  totalAmount = 0;
  confirmDeleteOpen = false;
  pendingDelete: ExpenseItem | null = null;
  selectorOpen = false;
  selectorTitle = '';
  selectorOptions: string[] = [];
  selectorValue = '';
  selectorContext: 'movementType' | 'paymentMethod' | 'pageSize' | null = null;
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

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

  get canGoPreviousMonth(): boolean {
    return this.months.indexOf(this.selectedMonth) > 0;
  }

  get canGoNextMonth(): boolean {
    const index = this.months.indexOf(this.selectedMonth);
    return index >= 0 && index < this.months.length - 1;
  }

  get isSearchActive(): boolean {
    return this.searchTerm.length > 0;
  }

  get effectivePageSize(): number {
    return this.isSearchActive ? 20 : this.pageSize;
  }

  get effectivePageSizeLabel(): string {
    return this.isSearchActive ? '20 por página (búsqueda)' : `${this.pageSize} por página`;
  }

  get filteredItems(): ExpenseItem[] {
    return this.items;
  }

  get totalFilteredAmount(): number {
    return this.totalAmount;
  }

  get maxFilteredAmount(): number {
    return this.filteredItems.length ? Math.max(...this.filteredItems.map((item) => item.amount)) : 0;
  }

  get averageFilteredAmount(): number {
    return this.filteredItems.length ? this.totalFilteredAmount / this.filteredItems.length : 0;
  }

  get movementTypeFilters(): string[] {
    return this.movementTypes;
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

  openPageSizeSelector(): void {
    this.openSelector(
      'pageSize',
      'Elementos por página',
      this.allowedPageSizes.map((size) => `${size}`),
      `${this.pageSize}`
    );
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

  openMovementTypeSelector(): void {
    this.openSelector('movementType', 'Selecciona tipo de movimiento', this.movementTypes, this.editForm.controls.movementType.value || '');
  }

  openPaymentMethodSelector(): void {
    this.openSelector('paymentMethod', 'Selecciona medio de pago', this.paymentMethods, this.editForm.controls.paymentMethod.value || '');
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
    this.pendingDelete = item;
    this.confirmDeleteOpen = true;
  }

  confirmDelete(): void {
    if (!this.pendingDelete) {
      this.confirmDeleteOpen = false;
      return;
    }

    const item = this.pendingDelete;
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;

    this.api.deleteExpense(item.id).subscribe({
      next: (result) => {
        this.statusMessage = result.message;
        this.loadPage();
      },
      error: () => (this.statusMessage = 'No fue posible eliminar el gasto.')
    });
  }

  cancelDelete(): void {
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;
  }

  closeSelector(): void {
    this.selectorOpen = false;
    this.selectorContext = null;
  }

  selectOption(value: string): void {
    if (this.selectorContext === 'movementType') {
      this.editForm.patchValue({ movementType: value });
    }

    if (this.selectorContext === 'paymentMethod') {
      this.editForm.patchValue({ paymentMethod: value });
    }

    if (this.selectorContext === 'pageSize') {
      this.setPageSize(Number(value));
    }

    this.closeSelector();
  }

  setFilter(value: string): void {
    this.activeMovementType = value;
    this.pageNumber = 1;
    this.loadPage();
  }

  updateSearch(value: string): void {
    this.searchTerm = value.trim();
    this.pageNumber = 1;

    if (this.searchDebounceHandle) {
      clearTimeout(this.searchDebounceHandle);
    }

    this.searchDebounceHandle = setTimeout(() => {
      this.loadPage();
    }, 220);
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
    const selectedMovementType = this.activeMovementType === 'all' ? undefined : this.activeMovementType;

    if (this.isSearchActive) {
      this.api
        .getExpenses(this.selectedMonth)
        .pipe(finalize(() => (this.isLoading = false)))
        .subscribe({
          next: (items) => {
            const byMovement = selectedMovementType
              ? items.filter((item) => item.movementType === selectedMovementType)
              : items;

            const filtered = byMovement.filter((item) => this.matchesSearch(item, this.searchTerm));
            const totalCount = filtered.length;
            const totalAmount = filtered.reduce((sum, item) => sum + Number(item.amount || 0), 0);
            const totalPages = totalCount === 0 ? 1 : Math.ceil(totalCount / this.effectivePageSize);
            const safePageNumber = Math.min(Math.max(this.pageNumber, 1), totalPages);
            const skip = (safePageNumber - 1) * this.effectivePageSize;

            this.items = filtered.slice(skip, skip + this.effectivePageSize);
            this.pageNumber = safePageNumber;
            this.totalPages = totalPages;
            this.totalCount = totalCount;
            this.totalAmount = totalAmount;
          },
          error: () => {
            this.statusMessage = 'No fue posible cargar gastos.';
          }
        });

      return;
    }

    this.api
      .getExpensesPage(this.selectedMonth, this.pageNumber, this.effectivePageSize, selectedMovementType, this.searchTerm)
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (page) => {
          this.items = page.items;
          this.pageNumber = page.pageNumber;
          this.totalPages = page.totalPages;
          this.totalCount = page.totalCount;
          this.totalAmount = page.totalAmount;
        },
        error: () => {
          this.statusMessage = 'No fue posible cargar gastos.';
        }
      });
  }

  private matchesSearch(item: ExpenseItem, term: string): boolean {
    const query = this.normalizeText(term);
    if (!query) {
      return true;
    }

    const description = this.normalizeText(item.description);
    const movementType = this.normalizeText(item.movementType);
    const paymentMethod = this.normalizeText(item.paymentMethod);
    const joined = `${description} ${movementType} ${paymentMethod}`.trim();

    if (joined.includes(query)) {
      return true;
    }

    return this.hasFuzzyWordMatch(query, joined);
  }

  private hasFuzzyWordMatch(query: string, text: string): boolean {
    if (!text) {
      return false;
    }

    const queryTokens = query.split(/\s+/).filter((token) => token.length > 0);
    const textTokens = text.split(/\s+/).filter((token) => token.length > 0);

    return queryTokens.every((queryToken) => {
      const maxDistance = queryToken.length >= 7 ? 2 : 1;
      return textTokens.some((textToken) => {
        if (textToken.includes(queryToken) || queryToken.includes(textToken)) {
          return true;
        }

        const distance = this.levenshteinDistance(queryToken, textToken);
        return distance <= maxDistance;
      });
    });
  }

  private normalizeText(value: string | null | undefined): string {
    return (value ?? '')
      .normalize('NFD')
      .replace(/\p{Diacritic}/gu, '')
      .toLowerCase()
      .trim();
  }

  private levenshteinDistance(source: string, target: string): number {
    if (source === target) {
      return 0;
    }

    if (!source.length) {
      return target.length;
    }

    if (!target.length) {
      return source.length;
    }

    const matrix: number[][] = Array.from({ length: source.length + 1 }, (_, row) => {
      const values = new Array<number>(target.length + 1).fill(0);
      values[0] = row;
      return values;
    });

    for (let column = 0; column <= target.length; column += 1) {
      matrix[0][column] = column;
    }

    for (let row = 1; row <= source.length; row += 1) {
      for (let column = 1; column <= target.length; column += 1) {
        const cost = source[row - 1] === target[column - 1] ? 0 : 1;
        matrix[row][column] = Math.min(
          matrix[row - 1][column] + 1,
          matrix[row][column - 1] + 1,
          matrix[row - 1][column - 1] + cost
        );
      }
    }

    return matrix[source.length][target.length];
  }

  private openSelector(
    context: 'movementType' | 'paymentMethod' | 'pageSize',
    title: string,
    options: string[],
    currentValue: string
  ): void {
    this.selectorContext = context;
    this.selectorTitle = title;
    this.selectorOptions = options;
    this.selectorValue = currentValue;
    this.selectorOpen = true;
  }

}
