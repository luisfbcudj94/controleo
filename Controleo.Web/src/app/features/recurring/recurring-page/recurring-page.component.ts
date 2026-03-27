import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { RecurringExpenseItem, RecurringExpenseUpsertRequest } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import { SelectorModalComponent } from '../../../shared/ui/selector-modal/selector-modal.component';
import { CustomDateInputComponent } from '../../../shared/ui/custom-date-input/custom-date-input.component';

@Component({
  selector: 'app-recurring-page',
  imports: [CommonModule, ReactiveFormsModule, ConfirmModalComponent, SelectorModalComponent, CustomDateInputComponent],
  templateUrl: './recurring-page.component.html',
  styleUrl: './recurring-page.component.scss'
})
export class RecurringPageComponent {
  isLoading = false;
  isSaving = false;
  statusMessage = '';
  items: RecurringExpenseItem[] = [];
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  editingId: string | null = null;
  confirmDeleteOpen = false;
  pendingDelete: RecurringExpenseItem | null = null;
  selectorOpen = false;
  selectorTitle = '';
  selectorOptions: string[] = [];
  selectorValue = '';
  selectorContext: 'movementType' | 'paymentMethod' | null = null;

  readonly form;

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder
  ) {
    this.form = this.fb.nonNullable.group({
      description: ['', Validators.required],
      amount: [0, [Validators.required, Validators.min(1)]],
      movementType: ['', Validators.required],
      paymentMethod: ['', Validators.required],
      dayOfMonth: [1, [Validators.required, Validators.min(1), Validators.max(31)]],
      startDate: [this.toInputDate(new Date()), Validators.required],
      isActive: [true]
    });

    this.loadCatalogs();
    this.loadRecurring();
  }

  edit(item: RecurringExpenseItem): void {
    this.editingId = item.id;
    this.form.patchValue({
      description: item.description,
      amount: item.amount,
      movementType: item.movementType,
      paymentMethod: item.paymentMethod,
      dayOfMonth: item.dayOfMonth,
      startDate: this.resolveStartDate(item),
      isActive: item.isActive
    });
  }

  clearForm(): void {
    this.editingId = null;
    this.form.reset({
      description: '',
      amount: 0,
      movementType: this.movementTypes[0] ?? '',
      paymentMethod: this.paymentMethods[0] ?? '',
      dayOfMonth: 1,
      startDate: this.toInputDate(new Date()),
      isActive: true
    });
  }

  save(): void {
    if (this.form.invalid || this.isSaving) {
      this.statusMessage = 'Completa todos los campos requeridos.';
      return;
    }

    const raw = this.form.getRawValue();
    const payload: RecurringExpenseUpsertRequest = {
      description: raw.description.trim(),
      amount: Number(raw.amount),
      movementType: raw.movementType,
      paymentMethod: raw.paymentMethod,
      dayOfMonth: Number(raw.dayOfMonth),
      startDate: raw.startDate,
      isActive: raw.isActive
    };

    this.isSaving = true;
    const request = this.editingId
      ? this.api.updateRecurringExpense(this.editingId, payload)
      : this.api.createRecurringExpense(payload);

    request.pipe(finalize(() => (this.isSaving = false))).subscribe({
      next: (result) => {
        this.statusMessage = result.message;
        this.clearForm();
        this.loadRecurring();
      },
      error: () => {
        this.statusMessage = 'No fue posible guardar el gasto recurrente.';
      }
    });
  }

  remove(item: RecurringExpenseItem): void {
    this.pendingDelete = item;
    this.confirmDeleteOpen = true;
  }

  confirmRemove(): void {
    if (!this.pendingDelete) {
      this.confirmDeleteOpen = false;
      return;
    }

    const item = this.pendingDelete;
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;

    this.api.deleteRecurringExpense(item.id).subscribe({
      next: (result) => {
        this.statusMessage = result.message;
        this.loadRecurring();
      },
      error: () => {
        this.statusMessage = 'No fue posible eliminar el gasto recurrente.';
      }
    });
  }

  cancelRemove(): void {
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;
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

  trackById(_: number, item: RecurringExpenseItem): string {
    return item.id;
  }

  private loadCatalogs(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.paymentMethods = catalog.paymentMethods;
        if (!this.form.value.movementType && this.movementTypes.length) {
          this.form.patchValue({ movementType: this.movementTypes[0] });
        }
        if (!this.form.value.paymentMethod && this.paymentMethods.length) {
          this.form.patchValue({ paymentMethod: this.paymentMethods[0] });
        }
      }
    });
  }

  private loadRecurring(): void {
    this.isLoading = true;
    this.api
      .getRecurringExpenses()
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (items) => {
          this.items = items.map((item) => ({
            ...item,
            startDate: this.resolveStartDate(item)
          }));
        },
        error: () => {
          this.statusMessage = 'No fue posible cargar los recurrentes.';
        }
      });
  }

  private toInputDate(date: Date): string {
    const yyyy = date.getFullYear();
    const mm = `${date.getMonth() + 1}`.padStart(2, '0');
    const dd = `${date.getDate()}`.padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  private resolveStartDate(item: RecurringExpenseItem): string {
    const startDate = item.startDate?.trim();
    if (startDate && /^\d{4}-\d{2}-\d{2}$/.test(startDate)) {
      return startDate;
    }

    const startMonth = item.startMonth?.trim() ?? startDate;
    if (startMonth && /^\d{4}-\d{2}$/.test(startMonth)) {
      return `${startMonth}-01`;
    }

    return this.toInputDate(new Date());
  }

  private openSelector(
    context: 'movementType' | 'paymentMethod',
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
