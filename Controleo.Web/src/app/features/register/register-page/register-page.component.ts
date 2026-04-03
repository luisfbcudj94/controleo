import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { MovementTypeConfig, PaymentMethodConfig } from '../../../core/models/api.models';
import { SelectorModalComponent, SelectorOptionItem } from '../../../shared/ui/selector-modal/selector-modal.component';
import { CustomDateInputComponent } from '../../../shared/ui/custom-date-input/custom-date-input.component';
import {
  resolveMovementColor,
  resolveMovementIcon,
  resolvePaymentIcon,
  resolvePaymentStyle
} from '../../../core/utils/catalog-visual.utils';

@Component({
  selector: 'app-register-page',
  imports: [CommonModule, ReactiveFormsModule, SelectorModalComponent, CustomDateInputComponent],
  templateUrl: './register-page.component.html',
  styleUrl: './register-page.component.scss'
})
export class RegisterPageComponent {
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  paymentMethodConfigs: PaymentMethodConfig[] = [];
  isSaving = false;
  selectorOpen = false;
  selectorTitle = '';
  selectorItems: SelectorOptionItem[] = [];
  selectorValue = '';
  selectorContext: 'movementType' | 'paymentMethod' | null = null;

  readonly form;

  constructor(
    private readonly fb: FormBuilder,
    private readonly api: ApiService,
    private readonly notify: NotificationService
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
    const selectedType = this.form.controls.movementType.value;
    if (selectedType) {
      return this.colorForMovementType(selectedType);
    }

    return this.amountPreview > 0 ? 'var(--danger)' : 'var(--accent)';
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

  openMovementTypeSelector(): void {
    this.openSelector(
      'movementType',
      'Tipo de movimiento',
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

  save(): void {
    if (this.form.invalid || this.isSaving) {
      this.notify.warning('Completa todos los campos.');
      return;
    }

    const value = this.form.getRawValue();
    const parsedAmount = Number(value.amount);
    if (!Number.isFinite(parsedAmount) || parsedAmount === 0) {
      this.notify.warning('El valor debe ser numérico y diferente de cero.');
      return;
    }

    this.isSaving = true;

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
        next: () => {
          this.notify.success('Gasto guardado correctamente.');
          this.form.patchValue({ description: '', amount: 0 });
        },
        error: () => {
          this.notify.error('No fue posible guardar el gasto.');
        }
      });
  }

  private loadInitialData(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.paymentMethods = catalog.paymentMethods;
        this.movementTypeConfigs = catalog.movementTypeConfigs ?? [];
        this.paymentMethodConfigs = catalog.paymentMethodConfigs ?? [];
        this.form.patchValue({
          movementType: catalog.movementTypes[0] ?? '',
          paymentMethod: catalog.paymentMethods[0] ?? ''
        });
      },
      error: () => {
        this.notify.error('No fue posible cargar catálogos.');
      }
    });
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
