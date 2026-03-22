import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { BudgetItem } from '../../../core/models/api.models';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';

@Component({
  selector: 'app-budgets-page',
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './budgets-page.component.html',
  styleUrl: './budgets-page.component.scss'
})
export class BudgetsPageComponent {
  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  movementTypes: string[] = [];
  budgets: BudgetItem[] = [];
  message = '';

  readonly form;

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder
  ) {
    this.form = this.fb.nonNullable.group({
      movementType: ['', Validators.required],
      amount: [0, Validators.required]
    });

    this.loadData();
  }

  get monthLabelText(): string {
    return monthLabel(this.selectedMonth);
  }

  previousMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index > 0) {
      this.selectedMonth = this.months[index - 1];
    }
  }

  nextMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index >= 0 && index < this.months.length - 1) {
      this.selectedMonth = this.months[index + 1];
    }
  }

  save(): void {
    if (this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    this.api.upsertBudget(value.movementType, { amount: Number(value.amount) }).subscribe({
      next: (result) => {
        this.message = result.message;
        this.form.patchValue({ amount: 0 });
        this.refreshBudgets();
      },
      error: () => (this.message = 'No fue posible guardar el presupuesto.')
    });
  }

  remove(item: BudgetItem): void {
    if (!confirm(`¿Eliminar presupuesto de '${item.movementType}'?`)) {
      return;
    }

    this.api.deleteBudget(item.movementType).subscribe({
      next: (result) => {
        this.message = result.message;
        this.refreshBudgets();
      },
      error: () => (this.message = 'No fue posible eliminar el presupuesto.')
    });
  }

  private loadData(): void {
    this.api.getAvailableMonths().subscribe({
      next: (months) => {
        this.months = months;
        this.selectedMonth = firstMonthOfAvailable(months);
      },
      error: () => {
        this.months = [monthKeyFromDate(new Date())];
        this.selectedMonth = this.months[0];
      }
    });

    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.form.patchValue({ movementType: catalog.movementTypes[0] ?? '' });
      }
    });

    this.refreshBudgets();
  }

  private refreshBudgets(): void {
    this.api.getBudgets().subscribe({
      next: (items) => (this.budgets = items),
      error: () => (this.message = 'No fue posible cargar presupuestos.')
    });
  }

}
