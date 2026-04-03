import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { DashboardCategoryItem, DashboardPaymentMethodItem, ExpenseItem, MovementTypeConfig, PaymentMethodConfig } from '../../../core/models/api.models';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';
import { forkJoin } from 'rxjs';
import { resolveMovementColor, resolveMovementIcon, resolvePaymentIcon, stableCatalogIndex } from '../../../core/utils/catalog-visual.utils';
import { SelectorModalComponent } from '../../../shared/ui/selector-modal/selector-modal.component';

interface DashboardDisplayRow {
  key: string;
  label: string;
  amount: number;
  progress: number;
  color: string;
  bg: string;
  icon: string;
  balance: number | null;
}

interface DashboardMobileRow {
  key: string;
  label: string;
  amount: number;
  progress: number;
  color: string;
  bg: string;
  icon: string;
  budgetTotal: number | null;
  balance: number | null;
  detail: DashboardDisplayRow;
}

@Component({
  selector: 'app-dashboard-page',
  imports: [CommonModule, SelectorModalComponent],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss'
})
export class DashboardPageComponent {
  readonly allowedPageSizes = [5, 10, 20];

  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  categoryRows: DashboardCategoryItem[] = [];
  paymentRows: DashboardPaymentMethodItem[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  paymentMethodConfigs: PaymentMethodConfig[] = [];
  distributionPageSize = 5;
  distributionPageNumber = 1;
  mode: 'category' | 'payment' = 'category';
  loading = false;
  message = '';
  detailOpen = false;
  detailTitle = '';
  detailItems: ExpenseItem[] = [];
  detailLoading = false;
  detailTotal = 0;
  detailTotalCount = 0;
  detailPageSize = 5;
  detailPageNumber = 1;
  detailTotalPages = 1;
  detailHasPreviousPage = false;
  detailHasNextPage = false;
  distributionBreakdownOpen = false;
  distributionBreakdownTitle = '';
  distributionBreakdownOptions: string[] = [];
  private detailMovementType: string | null = null;
  private detailPaymentMethod: string | null = null;
  readonly donutCircumference = 2 * Math.PI * 56;

  private readonly paymentCardPalette = ['#DDEEE4', '#DFEAF7', '#F8E5D8', '#E8E0F4', '#F7E1E7', '#E3F1EC'];
  private readonly paymentProgressPalette = ['#2D6A4F', '#3A8FBF', '#D4765A', '#7B5EA7', '#C0608F', '#3A9E8E'];
  private readonly moneyFormatter = new Intl.NumberFormat('es-CO', { maximumFractionDigits: 0 });

  constructor(private readonly api: ApiService) {
    this.loadCatalogVisuals();
    this.refreshMonthsAndData();
  }

  get monthLabelText(): string {
    return monthLabel(this.selectedMonth);
  }

  get canGoPreviousMonth(): boolean {
    return this.months.indexOf(this.selectedMonth) > 0;
  }

  get canGoNextMonth(): boolean {
    const index = this.months.indexOf(this.selectedMonth);
    return index >= 0 && index < this.months.length - 1;
  }

  get totalBudget(): number {
    return this.categoryRows.reduce((sum, row) => sum + row.budgetTotal, 0);
  }

  get totalSpentFromBudget(): number {
    return this.categoryRows
      .filter((row) => row.budgetTotal > 0)
      .reduce((sum, row) => sum + row.expenseTotal, 0);
  }

  get totalSpent(): number {
    return this.categoryRows.reduce((sum, row) => sum + row.expenseTotal, 0);
  }

  get available(): number {
    return this.totalBudget - this.totalSpentFromBudget;
  }

  get totalSpentWithExtras(): number {
    return this.totalSpent;
  }

  get usagePercent(): number {
    if (this.totalBudget <= 0) {
      return this.totalSpentFromBudget > 0 ? 100 : 0;
    }

    return Math.max(0, Math.min(100, (this.totalSpentFromBudget / this.totalBudget) * 100));
  }

  get donutOffset(): number {
    return this.donutCircumference - this.donutCircumference * (this.usagePercent / 100);
  }

  get distributionRows(): DashboardDisplayRow[] {
    if (this.mode === 'category') {
      return [...this.categoryRows]
        .filter((row) => row.expenseTotal > 0)
        .sort((left, right) => right.expenseTotal - left.expenseTotal)
        .map((row) => {
          const color = this.colorForMovementType(row.movementType);
          const progress = row.budgetTotal > 0 ? Math.max(0, Math.min(100, (row.expenseTotal / row.budgetTotal) * 100)) : (row.expenseTotal > 0 ? 100 : 0);

          return {
            key: row.movementType,
            label: row.movementType,
            amount: row.expenseTotal,
            progress,
            color,
            bg: this.softBackgroundForMovement(color),
            icon: this.iconForMovementType(row.movementType),
            balance: row.balance
          };
        });
    }

    const paymentTotal = this.paymentRows.reduce((sum, row) => sum + row.expenseTotal, 0);
    return [...this.paymentRows]
      .filter((row) => row.expenseTotal > 0)
      .sort((left, right) => right.expenseTotal - left.expenseTotal)
      .map((row) => {
        const index = this.stableIndexFor(row.paymentMethod, this.paymentProgressPalette.length);
        const progress = paymentTotal > 0 ? Math.max(0, Math.min(100, (row.expenseTotal / paymentTotal) * 100)) : 0;

        return {
          key: row.paymentMethod,
          label: row.paymentMethod,
          amount: row.expenseTotal,
          progress,
          color: this.paymentProgressPalette[index],
          bg: this.paymentCardPalette[index],
          icon: this.iconForPaymentMethod(row.paymentMethod),
          balance: null
        };
      });
  }

  get topDistributionRows(): DashboardDisplayRow[] {
    const safePageNumber = this.currentDistributionPageNumber;
    const start = (safePageNumber - 1) * this.distributionPageSize;
    return this.distributionRows.slice(start, start + this.distributionPageSize);
  }

  get distributionTotalPages(): number {
    return Math.max(1, Math.ceil(this.distributionRows.length / this.distributionPageSize));
  }

  get currentDistributionPageNumber(): number {
    return Math.min(this.distributionPageNumber, this.distributionTotalPages);
  }

  get canGoDistributionPreviousPage(): boolean {
    return this.currentDistributionPageNumber > 1;
  }

  get canGoDistributionNextPage(): boolean {
    return this.currentDistributionPageNumber < this.distributionTotalPages;
  }

  get distributionPaginationText(): string {
    return `Página ${this.currentDistributionPageNumber}/${this.distributionTotalPages}`;
  }

  get distributionTotal(): number {
    return this.distributionRows.reduce((sum, row) => sum + row.amount, 0);
  }

  get distributionSummaryText(): string {
    if (this.distributionTotal <= 0) {
      return 'Añade gastos para ver la distribución';
    }

    return this.mode === 'category' ? '100% por categoría' : '100% por medio de pago';
  }

  get mobileListRows(): DashboardMobileRow[] {
    if (this.mode === 'category') {
      return [...this.categoryRows]
        .sort((left, right) => right.expenseTotal - left.expenseTotal)
        .map((row) => {
          const color = this.colorForMovementType(row.movementType);
          const progress = row.budgetTotal > 0
            ? Math.max(0, Math.min(100, (row.expenseTotal / row.budgetTotal) * 100))
            : (row.expenseTotal > 0 ? 100 : 0);
          const detail: DashboardDisplayRow = {
            key: row.movementType,
            label: row.movementType,
            amount: row.expenseTotal,
            progress,
            color,
            bg: this.softBackgroundForMovement(color),
            icon: this.iconForMovementType(row.movementType),
            balance: row.balance
          };

          return {
            key: row.movementType,
            label: row.movementType,
            amount: row.expenseTotal,
            progress,
            color,
            bg: this.softBackgroundForMovement(color),
            icon: this.iconForMovementType(row.movementType),
            budgetTotal: row.budgetTotal > 0 ? row.budgetTotal : null,
            balance: row.budgetTotal > 0 ? row.balance : null,
            detail
          };
        });
    }

    const paymentTotal = this.paymentRows.reduce((sum, row) => sum + row.expenseTotal, 0);
    return [...this.paymentRows]
      .filter((row) => row.expenseTotal > 0)
      .sort((left, right) => right.expenseTotal - left.expenseTotal)
      .map((row) => {
        const index = this.stableIndexFor(row.paymentMethod, this.paymentProgressPalette.length);
        const progress = paymentTotal > 0 ? Math.max(0, Math.min(100, (row.expenseTotal / paymentTotal) * 100)) : 0;
        const detail: DashboardDisplayRow = {
          key: row.paymentMethod,
          label: row.paymentMethod,
          amount: row.expenseTotal,
          progress,
          color: this.paymentProgressPalette[index],
          bg: this.paymentCardPalette[index],
          icon: this.iconForPaymentMethod(row.paymentMethod),
          balance: null
        };

        return {
          key: row.paymentMethod,
          label: row.paymentMethod,
          amount: row.expenseTotal,
          progress,
          color: this.paymentProgressPalette[index],
          bg: this.paymentCardPalette[index],
          icon: this.iconForPaymentMethod(row.paymentMethod),
          budgetTotal: null,
          balance: null,
          detail
        };
      });
  }

  get distributionGradient(): string {
    if (!this.distributionRows.length || this.distributionTotal <= 0) {
      return 'conic-gradient(#DCE5DF 0% 100%)';
    }

    let cumulative = 0;
    const chunks = this.distributionRows.map((row, index) => {
      const start = cumulative;
      const ratio = index === this.distributionRows.length - 1
        ? 100 - cumulative
        : (row.amount / this.distributionTotal) * 100;
      cumulative = Math.min(100, cumulative + ratio);

      return `${row.color} ${start.toFixed(2)}% ${cumulative.toFixed(2)}%`;
    });

    return `conic-gradient(${chunks.join(', ')})`;
  }

  get modeTitle(): string {
    return this.mode === 'category' ? 'Por categoría' : 'Por medio de pago';
  }

  get detailContextText(): string {
    return this.mode === 'category' ? 'Categoría' : 'Medio de pago';
  }

  get detailPaginationText(): string {
    return `Página ${this.detailPageNumber}/${this.detailTotalPages}`;
  }

  trackByDistribution(_: number, row: DashboardDisplayRow): string {
    return row.key;
  }

  previousMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index > 0) {
      this.selectedMonth = this.months[index - 1];
      this.distributionPageNumber = 1;
      this.closeDetail();
      this.loadData();
    }
  }

  nextMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index >= 0 && index < this.months.length - 1) {
      this.selectedMonth = this.months[index + 1];
      this.distributionPageNumber = 1;
      this.closeDetail();
      this.loadData();
    }
  }

  setMode(mode: 'category' | 'payment'): void {
    if (this.mode === mode) {
      return;
    }

    this.mode = mode;
    this.distributionPageNumber = 1;
    this.normalizeDistributionPageNumber();
  }

  onDistributionPageSizeChange(rawValue: string): void {
    const parsed = Number(rawValue);
    if (!this.allowedPageSizes.includes(parsed)) {
      return;
    }

    this.distributionPageSize = parsed;
    this.distributionPageNumber = 1;
    this.normalizeDistributionPageNumber();
  }

  previousDistributionPage(): void {
    if (!this.canGoDistributionPreviousPage) {
      return;
    }

    this.distributionPageNumber -= 1;
  }

  nextDistributionPage(): void {
    if (!this.canGoDistributionNextPage) {
      return;
    }

    this.distributionPageNumber += 1;
  }

  openDetail(row: DashboardDisplayRow): void {
    this.detailTitle = row.label;
    this.detailOpen = true;
    this.detailMovementType = this.mode === 'category' ? row.key : null;
    this.detailPaymentMethod = this.mode === 'payment' ? row.key : null;
    this.detailPageNumber = 1;
    this.detailPageSize = this.allowedPageSizes[0];
    this.detailTotalPages = 1;
    this.detailHasPreviousPage = false;
    this.detailHasNextPage = false;
    this.detailTotalCount = 0;
    this.loadDetailItems();
  }

  closeDetail(): void {
    this.detailOpen = false;
    this.detailMovementType = null;
    this.detailPaymentMethod = null;
    this.detailItems = [];
    this.detailTotal = 0;
    this.detailTotalCount = 0;
    this.detailPageNumber = 1;
    this.detailTotalPages = 1;
    this.detailHasPreviousPage = false;
    this.detailHasNextPage = false;
  }

  onDetailPageSizeChange(rawValue: string): void {
    const parsed = Number(rawValue);
    if (!this.allowedPageSizes.includes(parsed) || parsed === this.detailPageSize) {
      return;
    }

    this.detailPageSize = parsed;
    this.detailPageNumber = 1;
    this.loadDetailItems();
  }

  previousDetailPage(): void {
    if (!this.detailHasPreviousPage) {
      return;
    }

    this.detailPageNumber -= 1;
    this.loadDetailItems();
  }

  nextDetailPage(): void {
    if (!this.detailHasNextPage) {
      return;
    }

    this.detailPageNumber += 1;
    this.loadDetailItems();
  }

  onDetailBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closeDetail();
    }
  }

  openDistributionBreakdown(): void {
    this.distributionBreakdownTitle = this.mode === 'category'
      ? 'Distribución por categorías'
      : 'Distribución por medios de pago';

    this.distributionBreakdownOptions = this.distributionRows.map((row) => {
      const percentage = this.formatPercentage(row.progress);
      return `${row.label} · ${percentage} · $${this.moneyFormatter.format(row.amount)}`;
    });

    this.distributionBreakdownOpen = true;
  }

  closeDistributionBreakdown(): void {
    this.distributionBreakdownOpen = false;
  }

  private refreshMonthsAndData(): void {
    this.api.getAvailableMonths().subscribe({
      next: (months) => {
        this.months = months;
        this.selectedMonth = firstMonthOfAvailable(months);
        this.loadData();
      },
      error: () => {
        this.months = [monthKeyFromDate(new Date())];
        this.selectedMonth = this.months[0];
        this.loadData();
      }
    });
  }

  private loadData(): void {
    this.loading = true;

    forkJoin({
      categories: this.api.getDashboardByCategory(this.selectedMonth),
      payments: this.api.getDashboardByPaymentMethod(this.selectedMonth)
    }).subscribe({
      next: ({ categories, payments }) => {
        this.categoryRows = categories;
        this.paymentRows = payments;
        this.normalizeDistributionPageNumber();
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.message = 'No fue posible cargar el dashboard.';
      }
    });
  }

  private loadCatalogVisuals(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypeConfigs = catalog.movementTypeConfigs ?? [];
        this.paymentMethodConfigs = catalog.paymentMethodConfigs ?? [];
      }
    });
  }

  private iconForMovementType(movementType: string): string {
    return resolveMovementIcon(movementType, this.movementTypeConfigs);
  }

  private colorForMovementType(movementType: string): string {
    return resolveMovementColor(movementType, this.movementTypeConfigs);
  }

  private softBackgroundForMovement(color: string): string {
    const trimmed = color.trim();
    if (/^#[0-9A-Fa-f]{6}$/.test(trimmed)) {
      return `${trimmed}33`;
    }

    return 'var(--ac2)';
  }

  private loadDetailItems(): void {
    if (!this.detailMovementType && !this.detailPaymentMethod) {
      this.detailItems = [];
      this.detailTotal = 0;
      this.detailTotalCount = 0;
      this.detailPageNumber = 1;
      this.detailTotalPages = 1;
      this.detailHasPreviousPage = false;
      this.detailHasNextPage = false;
      return;
    }

    this.detailLoading = true;

    this.api.getExpensesPage(
      this.selectedMonth,
      this.detailPageNumber,
      this.detailPageSize,
      this.detailMovementType ?? undefined,
      undefined,
      this.detailPaymentMethod ?? undefined
    ).subscribe({
      next: (page) => {
        this.detailItems = page.items;
        this.detailTotal = page.totalAmount;
        this.detailTotalCount = page.totalCount;
        this.detailPageNumber = page.pageNumber;
        this.detailTotalPages = page.totalPages;
        this.detailHasPreviousPage = page.hasPreviousPage;
        this.detailHasNextPage = page.hasNextPage;
        this.detailLoading = false;
      },
      error: () => {
        this.detailItems = [];
        this.detailTotal = 0;
        this.detailTotalCount = 0;
        this.detailPageNumber = 1;
        this.detailTotalPages = 1;
        this.detailHasPreviousPage = false;
        this.detailHasNextPage = false;
        this.detailLoading = false;
      }
    });
  }

  private normalizeDistributionPageNumber(): void {
    if (this.distributionPageNumber < 1) {
      this.distributionPageNumber = 1;
      return;
    }

    if (this.distributionPageNumber > this.distributionTotalPages) {
      this.distributionPageNumber = this.distributionTotalPages;
    }
  }

  private iconForPaymentMethod(paymentMethod: string): string {
    return resolvePaymentIcon(paymentMethod, this.paymentMethodConfigs);
  }

  private stableIndexFor(value: string, length: number): number {
    return stableCatalogIndex(value, length);
  }

  private formatPercentage(value: number): string {
    const rounded = Math.round(value * 10) / 10;
    return Number.isInteger(rounded)
      ? `${rounded.toFixed(0)}%`
      : `${rounded.toFixed(1)}%`;
  }
}
