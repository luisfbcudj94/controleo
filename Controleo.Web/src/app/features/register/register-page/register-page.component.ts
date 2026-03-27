import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { firstMonthOfAvailable, monthKeyFromDate, monthLabel } from '../../../core/utils/month.utils';
import { SelectorModalComponent } from '../../../shared/ui/selector-modal/selector-modal.component';
import { CustomDateInputComponent } from '../../../shared/ui/custom-date-input/custom-date-input.component';

@Component({
  selector: 'app-register-page',
  imports: [CommonModule, ReactiveFormsModule, SelectorModalComponent, CustomDateInputComponent],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.scss'
})
export class RegisterPageComponent {
  months: string[] = [];
  selectedMonth = monthKeyFromDate(new Date());
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  isSaving = false;
  statusMessage = '';
  selectorOpen = false;
  selectorTitle = '';
  selectorOptions: string[] = [];
  selectorValue = '';
  selectorContext: 'month' | 'movementType' | 'paymentMethod' | null = null;

  readonly form;

  constructor(
    private readonly fb: FormBuilder,
    private readonly api: ApiService
  ) {
    this.form = this.fb.nonNullable.group({
      date: [new Date().toISOString().slice(0, 10), Validators.required],
      description: ['', [Validators.required, Validators.maxLength(120)]],
      amount: [0, [Validators.required]],
      movementType: ['', Validators.required],
      paymentMethod: ['', Validators.required]
    });

    this.loadInitialData();
  }

  get monthLabelText(): string {
    return monthLabel(this.selectedMonth);
  }

  get amountPreview(): number {
    return Number(this.form.controls.amount.value) || 0;
  }

  get descriptionPreview(): string {
    return this.form.controls.description.value?.trim() || '—';
  }

  get movementTypePreview(): string {
    return this.form.controls.movementType.value || '—';
  }

  get paymentMethodPreview(): string {
    return this.form.controls.paymentMethod.value || '—';
  }

  get movementTypeColor(): string {
    return this.amountPreview > 0 ? 'var(--danger)' : 'var(--accent)';
  }

  previousMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index > 0) {
      this.selectedMonth = this.months[index - 1];
      this.syncDateToMonth();
    }
  }

  nextMonth(): void {
    const index = this.months.indexOf(this.selectedMonth);
    if (index >= 0 && index < this.months.length - 1) {
      this.selectedMonth = this.months[index + 1];
      this.syncDateToMonth();
    }
  }

  onMonthSelected(month: string): void {
    this.selectedMonth = month;
    this.syncDateToMonth();
  }

  openMonthSelector(): void {
    this.openSelector('month', 'Selecciona mes del gasto', this.months, this.selectedMonth);
  }

  openMovementTypeSelector(): void {
    this.openSelector('movementType', 'Selecciona tipo de movimiento', this.movementTypes, this.form.controls.movementType.value || '');
  }

  openPaymentMethodSelector(): void {
    this.openSelector('paymentMethod', 'Selecciona medio de pago', this.paymentMethods, this.form.controls.paymentMethod.value || '');
  }

  closeSelector(): void {
    this.selectorOpen = false;
    this.selectorContext = null;
  }

  selectOption(value: string): void {
    if (this.selectorContext === 'month') {
      this.onMonthSelected(value);
    }

    if (this.selectorContext === 'movementType') {
      this.form.patchValue({ movementType: value });
    }

    if (this.selectorContext === 'paymentMethod') {
      this.form.patchValue({ paymentMethod: value });
    }

    this.closeSelector();
  }

  onAmountFocus(event: FocusEvent): void {
    const target = event.target as HTMLInputElement | null;
    if (!target) {
      return;
    }

    if (target.value === '0') {
      target.value = '';
      target.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  onAmountBlur(event: FocusEvent): void {
    const target = event.target as HTMLInputElement | null;
    if (!target) {
      return;
    }

    if (!target.value.trim()) {
      target.value = '0';
      target.dispatchEvent(new Event('input', { bubbles: true }));
    }
  }

  save(): void {
    if (this.form.invalid || this.isSaving) {
      this.statusMessage = 'Completa todos los campos.';
      return;
    }

    const value = this.form.getRawValue();
    const parsedAmount = Number(value.amount);
    if (!Number.isFinite(parsedAmount) || parsedAmount === 0) {
      this.statusMessage = 'El valor debe ser numérico y diferente de cero.';
      return;
    }

    this.isSaving = true;
    this.statusMessage = 'Guardando...';

    this.api
      .saveExpense({
        date: value.date,
        description: value.description.trim(),
        amount: parsedAmount,
        movementType: value.movementType,
        paymentMethod: value.paymentMethod
      })
      .pipe(finalize(() => (this.isSaving = false)))
      .subscribe({
        next: (result) => {
          this.statusMessage = result.message;
          this.form.patchValue({ description: '', amount: 0 });
          this.refreshMonths();
        },
        error: () => {
          this.statusMessage = 'No fue posible guardar el gasto.';
        }
      });
  }

  private loadInitialData(): void {
    this.refreshMonths();

    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.paymentMethods = catalog.paymentMethods;
        this.form.patchValue({
          movementType: catalog.movementTypes[0] ?? '',
          paymentMethod: catalog.paymentMethods[0] ?? ''
        });
      },
      error: () => {
        this.statusMessage = 'No fue posible cargar catálogos.';
      }
    });
  }

  private refreshMonths(): void {
    this.api.getAvailableMonths().subscribe({
      next: (months) => {
        this.months = months;
        this.selectedMonth = firstMonthOfAvailable(months);
        this.syncDateToMonth();
      },
      error: () => {
        this.months = [monthKeyFromDate(new Date())];
        this.selectedMonth = this.months[0];
        this.syncDateToMonth();
      }
    });
  }

  private syncDateToMonth(): void {
    const [year, month] = this.selectedMonth.split('-').map(Number);
    const day = 1;
    const date = new Date(year, (month || 1) - 1, day);
    this.form.patchValue({ date: date.toISOString().slice(0, 10) }, { emitEvent: false });
  }

  private openSelector(
    context: 'month' | 'movementType' | 'paymentMethod',
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
