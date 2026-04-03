import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { MovementTypeConfig, PaymentMethodConfig, RecurringExpenseItem, RecurringExpenseUpsertRequest } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
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
  selector: 'app-recurring-page',
  imports: [CommonModule, ReactiveFormsModule, ConfirmModalComponent, SelectorModalComponent, CustomDateInputComponent],
  templateUrl: './recurring-page.component.html',
  styleUrl: './recurring-page.component.scss'
})
export class RecurringPageComponent {
  isLoading = false;
  isSaving = false;
  items: RecurringExpenseItem[] = [];
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  paymentMethodConfigs: PaymentMethodConfig[] = [];
  editingId: string | null = null;
  confirmDeleteOpen = false;
  pendingDelete: RecurringExpenseItem | null = null;
  selectorOpen = false;
  selectorTitle = '';
  selectorItems: SelectorOptionItem[] = [];
  selectorValue = '';
  selectorContext: 'movementType' | 'paymentMethod' | null = null;

  readonly form;

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder,
    private readonly notify: NotificationService
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

  get movementTypeSelectionLabel(): string {
    const value = this.form.controls.movementType.value;
    if (!value) {
      return 'Seleccionar tipo';
    }

    return `${this.iconForMovementType(value)} ${value}`;
  }

  get paymentMethodSelectionLabel(): string {
    const value = this.form.controls.paymentMethod.value;
    if (!value) {
      return 'Seleccionar medio';
    }

    return `${this.iconForPaymentMethod(value)} ${value}`;
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
      this.notify.warning('Completa todos los campos requeridos.');
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
    const isEditing = !!this.editingId;
    const request = this.editingId
      ? this.api.updateRecurringExpense(this.editingId, payload)
      : this.api.createRecurringExpense(payload);

    request.pipe(finalize(() => (this.isSaving = false))).subscribe({
      next: () => {
        this.notify.success(
          isEditing
            ? 'Gasto recurrente actualizado correctamente.'
            : 'Gasto recurrente guardado correctamente.'
        );
        this.clearForm();
        this.loadRecurring();
      },
      error: () => {
        this.notify.error('No fue posible guardar el gasto recurrente.');
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
      next: () => {
        this.notify.success('Gasto recurrente eliminado correctamente.');
        this.loadRecurring();
      },
      error: () => {
        this.notify.error('No fue posible eliminar el gasto recurrente.');
      }
    });
  }

  cancelRemove(): void {
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;
  }

  openMovementTypeSelector(): void {
    this.openSelector(
      'movementType',
      'Tipo de gasto',
      this.buildMovementTypeSelectorItems(),
      this.form.controls.movementType.value || ''
    );
  }

  openPaymentMethodSelector(): void {
    this.openSelector(
      'paymentMethod',
      'Medio de pago',
      this.buildPaymentMethodSelectorItems(),
      this.form.controls.paymentMethod.value || ''
    );
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
        this.movementTypeConfigs = catalog.movementTypeConfigs ?? [];
        this.paymentMethodConfigs = catalog.paymentMethodConfigs ?? [];
        if (!this.form.value.movementType && this.movementTypes.length) {
          this.form.patchValue({ movementType: this.movementTypes[0] });
        }
        if (!this.form.value.paymentMethod && this.paymentMethods.length) {
          this.form.patchValue({ paymentMethod: this.paymentMethods[0] });
        }
      },
      error: () => {
        this.notify.error('No fue posible cargar catálogos.');
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
          this.notify.error('No fue posible cargar los recurrentes.');
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
    optionItems: SelectorOptionItem[],
    currentValue: string
  ): void {
    this.selectorContext = context;
    this.selectorTitle = title;
    this.selectorItems = optionItems;
    this.selectorValue = currentValue;
    this.selectorOpen = true;
  }

  private buildMovementTypeSelectorItems(): SelectorOptionItem[] {
    return this.movementTypes.map((movementType) => ({
      value: movementType,
      label: movementType,
      icon: this.iconForMovementType(movementType),
      color: this.colorForMovementType(movementType)
    }));
  }

  private buildPaymentMethodSelectorItems(): SelectorOptionItem[] {
    return this.paymentMethods.map((paymentMethod) => {
      const style = this.styleForPaymentMethod(paymentMethod);

      return {
        value: paymentMethod,
        label: paymentMethod,
        icon: this.iconForPaymentMethod(paymentMethod),
        color: style.background,
        textColor: style.foreground
      };
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
}
