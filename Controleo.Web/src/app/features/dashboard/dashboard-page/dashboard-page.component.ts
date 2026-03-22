import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ApiService } from '../../../core/services/api.service';
import { DashboardCategoryItem } from '../../../core/models/api.models';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';

@Component({
  selector: 'app-dashboard-page',
  imports: [CommonModule],
  templateUrl: './dashboard-page.component.html',
  styleUrl: './dashboard-page.component.scss'
})
export class DashboardPageComponent {
  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  rows: DashboardCategoryItem[] = [];
  loading = false;
  message = '';
  readonly donutCircumference = 2 * Math.PI * 56;

  private readonly palette = [
    { color: 'var(--accent)', bg: 'var(--ac2)', icon: '🛒' },
    { color: 'var(--purple)', bg: 'var(--pur2)', icon: '🏗️' },
    { color: 'var(--coral)', bg: 'var(--cor2)', icon: '💳' },
    { color: 'var(--amber)', bg: 'var(--amb2)', icon: '🍽️' },
    { color: 'var(--info)', bg: 'var(--inf2)', icon: '✈️' },
    { color: 'var(--teal)', bg: 'var(--tea2)', icon: '💊' }
  ];

  constructor(private readonly api: ApiService) {
    this.refreshMonthsAndData();
  }

  get monthLabelText(): string {
    return monthLabel(this.selectedMonth);
  }

  get totalBudget(): number {
    return this.rows.reduce((sum, row) => sum + row.budgetTotal, 0);
  }

  get totalSpentFromBudget(): number {
    return this.rows
      .filter((row) => row.budgetTotal > 0)
      .reduce((sum, row) => sum + row.expenseTotal, 0);
  }

  get totalSpent(): number {
    return this.rows.reduce((sum, row) => sum + row.expenseTotal, 0);
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

  get topRows(): DashboardCategoryItem[] {
    return [...this.rows]
      .sort((left, right) => right.expenseTotal - left.expenseTotal)
      .slice(0, 6);
  }

  colorAt(index: number): string {
    return this.palette[index % this.palette.length].color;
  }

  bgAt(index: number): string {
    return this.palette[index % this.palette.length].bg;
  }

  iconAt(index: number): string {
    return this.palette[index % this.palette.length].icon;
  }

  barWidth(row: DashboardCategoryItem): number {
    if (row.budgetTotal <= 0) {
      return row.expenseTotal > 0 ? 100 : 0;
    }

    return Math.max(0, Math.min(100, (row.expenseTotal / row.budgetTotal) * 100));
  }

  trackByMovementType(_: number, row: DashboardCategoryItem): string {
    return row.movementType;
  }

  previousMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index > 0) {
      this.selectedMonth = this.months[index - 1];
      this.loadData();
    }
  }

  nextMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index >= 0 && index < this.months.length - 1) {
      this.selectedMonth = this.months[index + 1];
      this.loadData();
    }
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
    this.api.getDashboardByCategory(this.selectedMonth).subscribe({
      next: (data) => {
        this.rows = data;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.message = 'No fue posible cargar el dashboard.';
      }
    });
  }

}
