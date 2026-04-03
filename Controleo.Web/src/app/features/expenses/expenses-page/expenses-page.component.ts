import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ExpenseItem, MovementTypeConfig, PaymentMethodConfig } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import { SelectorModalComponent, SelectorOptionItem } from '../../../shared/ui/selector-modal/selector-modal.component';
import { CustomDateInputComponent } from '../../../shared/ui/custom-date-input/custom-date-input.component';
import {
  resolveMovementColor,
  resolveMovementIcon,
  resolvePaymentIcon,
  resolvePaymentStyle
} from '../../../core/utils/catalog-visual.utils';

@Component({
  selector: 'app-expenses-page',
  imports: [CommonModule, ReactiveFormsModule, ConfirmModalComponent, SelectorModalComponent, CustomDateInputComponent],
  templateUrl: './expenses-page.component.html',
  styleUrl: './expenses-page.component.scss'
})
export class ExpensesPageComponent {
  readonly allowedPageSizes = [5, 10, 20];

  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  items: ExpenseItem[] = [];
  pageNumber = 1;
  pageSize = 5;
  totalPages = 1;
  totalCount = 0;
  isLoading = false;
  isSavingEdit = false;
  editingExpense: ExpenseItem | null = null;
  activeMovementType: string | null = null;
  activePaymentMethod: string | null = null;
  searchTerm = '';
  totalAmount = 0;
  filtersOpen = false;
  pendingMovementType: string | null = null;
  pendingPaymentMethod: string | null = null;
  confirmDeleteOpen = false;
  pendingDelete: ExpenseItem | null = null;
  selectorOpen = false;
  selectorMode: 'list' | 'tiles' = 'list';
  selectorTitle = '';
  selectorOptions: string[] = [];
  selectorItems: SelectorOptionItem[] = [];
  selectorValue = '';
  selectorContext: 'movementType' | 'paymentMethod' | 'pageSize' | null = null;
  private searchDebounceHandle: ReturnType<typeof setTimeout> | null = null;

  readonly editForm;

  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  paymentMethodConfigs: PaymentMethodConfig[] = [];

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder,
    private readonly notify: NotificationService
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

  get hasActiveFilters(): boolean {
    return this.activeFiltersCount > 0;
  }

  get activeFiltersCount(): number {
    let count = 0;
    if (this.activeMovementType) {
      count += 1;
    }

    if (this.activePaymentMethod) {
      count += 1;
    }

    return count;
  }

  get activeFiltersLabel(): string {
    if (!this.hasActiveFilters) {
      return 'Sin filtros activos';
    }

    const movement = this.activeMovementType ?? 'Todos los tipos';
    const payment = this.activePaymentMethod ?? 'Todos los medios';
    return `${movement} · ${payment}`;
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

  get movementFilterItems(): SelectorOptionItem[] {
    return [
      { value: '', label: 'Todos', icon: '🧩', color: '#EFF3F1' },
      ...this.movementTypes.map((movementType) => ({
        value: movementType,
        label: movementType,
        icon: this.iconForMovementType(movementType),
        color: this.colorForMovementType(movementType)
      }))
    ];
  }

  get paymentFilterItems(): SelectorOptionItem[] {
    return [
      { value: '', label: 'Todos', icon: '💳', color: '#EFF3F1' },
      ...this.paymentMethods.map((paymentMethod) => {
        const style = this.styleForPaymentMethod(paymentMethod);

        return {
          value: paymentMethod,
          label: paymentMethod,
          icon: this.iconForPaymentMethod(paymentMethod),
          color: style.background,
          textColor: style.foreground
        };
      })
    ];
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
    this.openSelector('pageSize', 'Elementos por página', `${this.pageSize}`, 'list', this.allowedPageSizes.map((size) => `${size}`));
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

  onEditBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.cancelEdit();
    }
  }

  openMovementTypeSelector(): void {
    this.openSelector(
      'movementType',
      'Tipo de movimiento',
      this.editForm.controls.movementType.value || '',
      'tiles',
      [],
      this.movementFilterItems.filter((item) => item.value)
    );
  }

  openPaymentMethodSelector(): void {
    this.openSelector(
      'paymentMethod',
      'Medio de pago',
      this.editForm.controls.paymentMethod.value || '',
      'tiles',
      [],
      this.paymentFilterItems.filter((item) => item.value)
    );
  }

  saveEdit(): void {
    if (!this.editingExpense || this.isSavingEdit) {
      return;
    }

    if (this.editForm.invalid) {
      this.notify.warning('Completa todos los campos para actualizar el gasto.');
      return;
    }

    const value = this.editForm.getRawValue();
    this.isSavingEdit = true;

    this.api
      .updateExpense(this.editingExpense.id, {
        date: value.date,
        description: value.description,
        amount: Number(value.amount),
        movementType: value.movementType,
        paymentMethod: value.paymentMethod
      })
      .pipe(finalize(() => (this.isSavingEdit = false)))
      .subscribe({
        next: () => {
          this.notify.success('Gasto actualizado correctamente.');
          this.editingExpense = null;
          this.loadPage();
        },
        error: () => {
          this.notify.error('No fue posible actualizar el gasto.');
        }
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
      next: () => {
        this.notify.success('Gasto eliminado correctamente.');
        this.loadPage();
      },
      error: () => {
        this.notify.error('No fue posible eliminar el gasto.');
      }
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

  openFilters(): void {
    this.pendingMovementType = this.activeMovementType;
    this.pendingPaymentMethod = this.activePaymentMethod;
    this.filtersOpen = true;
  }

  closeFilters(): void {
    this.filtersOpen = false;
  }

  onFiltersBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closeFilters();
    }
  }

  setPendingMovementType(value: string): void {
    this.pendingMovementType = value || null;
  }

  setPendingPaymentMethod(value: string): void {
    this.pendingPaymentMethod = value || null;
  }

  applyFilters(): void {
    this.activeMovementType = this.pendingMovementType;
    this.activePaymentMethod = this.pendingPaymentMethod;
    this.pageNumber = 1;
    this.closeFilters();
    this.loadPage();
  }

  clearFilters(): void {
    this.pendingMovementType = null;
    this.pendingPaymentMethod = null;
    this.applyFilters();
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
    return this.colorForMovementType(item.movementType);
  }

  bgFor(item: ExpenseItem): string {
    return `color-mix(in srgb, ${this.colorForMovementType(item.movementType)} 26%, #FFFFFF)`;
  }

  iconFor(item: ExpenseItem): string {
    return this.iconForMovementType(item.movementType);
  }

  trackById(_: number, item: ExpenseItem): string {
    return item.id;
  }

  private refreshMonthsAndLoad(): void {
    const currentMonth = monthKeyFromDate(new Date());

    this.api.getAvailableMonths().subscribe({
      next: (months) => {
        const normalizedMonths = Array.from(new Set([...months, currentMonth])).sort((left, right) => left.localeCompare(right));
        this.months = normalizedMonths;
        this.selectedMonth = currentMonth;
        this.loadPage();
      },
      error: () => {
        this.months = [currentMonth];
        this.selectedMonth = currentMonth;
        this.loadPage();
      }
    });
  }

  private loadCatalogs(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.paymentMethods = catalog.paymentMethods;
        this.movementTypeConfigs = catalog.movementTypeConfigs ?? [];
        this.paymentMethodConfigs = catalog.paymentMethodConfigs ?? [];
      }
    });
  }

  private loadPage(): void {
    this.isLoading = true;
    const selectedMovementType = this.activeMovementType || undefined;
    const selectedPaymentMethod = this.activePaymentMethod || undefined;

    if (this.isSearchActive) {
      this.api
        .getExpenses(this.selectedMonth)
        .pipe(finalize(() => (this.isLoading = false)))
        .subscribe({
          next: (items) => {
            const byMovement = selectedMovementType
              ? items.filter((item) => item.movementType === selectedMovementType)
              : items;

            const byPayment = selectedPaymentMethod
              ? byMovement.filter((item) => item.paymentMethod === selectedPaymentMethod)
              : byMovement;

            const filtered = byPayment.filter((item) => this.matchesSearch(item, this.searchTerm));
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
            this.notify.error('No fue posible cargar gastos.');
          }
        });

      return;
    }

    this.api
      .getExpensesPage(
        this.selectedMonth,
        this.pageNumber,
        this.effectivePageSize,
        selectedMovementType,
        this.searchTerm,
        selectedPaymentMethod
      )
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
          this.notify.error('No fue posible cargar gastos.');
        }
      });
  }

  private iconForMovementType(movementType: string): string {
    return resolveMovementIcon(movementType, this.movementTypeConfigs);
  }

  private colorForMovementType(movementType: string): string {
    return resolveMovementColor(movementType, this.movementTypeConfigs);
  }

  private iconForPaymentMethod(paymentMethod: string): string {
    return resolvePaymentIcon(paymentMethod, this.paymentMethodConfigs);
  }

  private styleForPaymentMethod(paymentMethod: string): { background: string; foreground: string } {
    return resolvePaymentStyle(paymentMethod);
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
    currentValue: string,
    mode: 'list' | 'tiles',
    options: string[] = [],
    optionItems: SelectorOptionItem[] = []
  ): void {
    this.selectorContext = context;
    this.selectorTitle = title;
    this.selectorMode = mode;
    this.selectorOptions = options;
    this.selectorItems = optionItems;
    this.selectorValue = currentValue;
    this.selectorOpen = true;
  }

}
