import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { finalize } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import { MovementTypeConfig, PaymentMethodConfig } from '../../../core/models/api.models';
import { SelectorModalComponent, SelectorOptionItem } from '../../../shared/ui/selector-modal/selector-modal.component';
import {
  MOVEMENT_COLOR_OPTIONS,
  MOVEMENT_ICON_OPTIONS,
  PAYMENT_ICON_OPTIONS,
  normalizeCatalogKey,
  resolveMovementColor,
  resolveMovementIcon,
  resolvePaymentIcon,
  sanitizeMovementIcon,
  sanitizePaymentIcon
} from '../../../core/utils/catalog-visual.utils';

@Component({
  selector: 'app-settings-page',
  imports: [CommonModule, FormsModule, ConfirmModalComponent, SelectorModalComponent],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.scss'
})
export class SettingsPageComponent {
  isLoading = false;
  isSaving = false;
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  paymentMethodConfigs: PaymentMethodConfig[] = [];
  newMovementType = '';
  newMovementIcon = MOVEMENT_ICON_OPTIONS[0].icon;
  newMovementColor = MOVEMENT_COLOR_OPTIONS[0].hex;
  newPaymentMethod = '';
  newPaymentIcon = '💳';

  createOpen = false;
  createTarget: 'movement' | 'payment' | null = null;

  editOpen = false;
  editTitle = '';
  editValue = '';
  editMovementIcon = MOVEMENT_ICON_OPTIONS[0].icon;
  editMovementColor = MOVEMENT_COLOR_OPTIONS[0].hex;
  editPaymentIcon = '💳';
  private editOriginalValue = '';
  editTarget: 'movement' | 'payment' | null = null;

  confirmDeleteOpen = false;
  private deleteTarget: 'movement' | 'payment' | null = null;
  private deleteValue = '';

  iconSelectorOpen = false;
  iconSelectorTarget: 'newMovement' | 'editMovement' | 'newPayment' | 'editPayment' | null = null;

  readonly movementIconOptions = MOVEMENT_ICON_OPTIONS;
  readonly movementColorOptions = MOVEMENT_COLOR_OPTIONS;
  readonly paymentIconOptions = PAYMENT_ICON_OPTIONS;

  constructor(
    private readonly api: ApiService,
    private readonly notify: NotificationService
  ) {
    this.loadCatalogs();
  }

  get iconSelectorItems(): SelectorOptionItem[] {
    const source = this.iconSelectorTarget === 'newPayment' || this.iconSelectorTarget === 'editPayment'
      ? this.paymentIconOptions
      : this.movementIconOptions;

    return source.map((option) => ({
      value: option.icon,
      label: option.label,
      icon: option.icon,
      color: '#F2F5F3'
    }));
  }

  get iconSelectorSelectedValue(): string {
    if (this.iconSelectorTarget === 'newMovement') {
      return this.newMovementIcon;
    }

    if (this.iconSelectorTarget === 'editMovement') {
      return this.editMovementIcon;
    }

    if (this.iconSelectorTarget === 'newPayment') {
      return this.newPaymentIcon;
    }

    if (this.iconSelectorTarget === 'editPayment') {
      return this.editPaymentIcon;
    }

    return '';
  }

  get deleteMessage(): string {
    if (!this.deleteValue) {
      return '';
    }

    return this.deleteTarget === 'movement'
      ? `¿Deseas eliminar la sección '${this.deleteValue}'?`
      : `¿Deseas eliminar el medio de pago '${this.deleteValue}'?`;
  }

  get canConfirmCreate(): boolean {
    if (this.createTarget === 'movement') {
      return this.newMovementType.trim().length > 0;
    }

    if (this.createTarget === 'payment') {
      return this.newPaymentMethod.trim().length > 0;
    }

    return false;
  }

  movementMeta(item: string): { icon: string; color: string } {
    return {
      icon: resolveMovementIcon(item, this.movementTypeConfigs),
      color: resolveMovementColor(item, this.movementTypeConfigs)
    };
  }

  paymentIcon(item: string): string {
    return resolvePaymentIcon(item, this.paymentMethodConfigs);
  }

  isMovementColorSelected(color: string, target: 'new' | 'edit'): boolean {
    return target === 'new' ? this.newMovementColor === color : this.editMovementColor === color;
  }

  selectMovementColor(color: string, target: 'new' | 'edit'): void {
    if (target === 'new') {
      this.newMovementColor = color;
      return;
    }

    this.editMovementColor = color;
  }

  openCreateMovementType(): void {
    this.createTarget = 'movement';
    this.newMovementType = '';
    this.newMovementIcon = this.movementIconOptions[0].icon;
    this.newMovementColor = this.movementColorOptions[0].hex;
    this.createOpen = true;
  }

  openCreatePaymentMethod(): void {
    this.createTarget = 'payment';
    this.newPaymentMethod = '';
    this.newPaymentIcon = '💳';
    this.createOpen = true;
  }

  closeCreate(): void {
    this.createOpen = false;
    this.createTarget = null;
    this.newMovementType = '';
    this.newPaymentMethod = '';
  }

  confirmCreate(): void {
    if (this.createTarget === 'movement') {
      this.addMovementType();
      return;
    }

    if (this.createTarget === 'payment') {
      this.addPaymentMethod();
    }
  }

  addMovementType(): void {
    const value = this.newMovementType.trim();
    if (!value) {
      this.notify.warning('Ingresa una sección para agregar.');
      return;
    }

    if (this.exists(this.movementTypes, value)) {
      this.notify.warning('Esa sección ya existe.');
      return;
    }

    const nextMovementTypes = [...this.movementTypes, value];
    const nextMovementConfigs = this.upsertMovementConfig(this.movementTypeConfigs, value, this.newMovementIcon, this.newMovementColor);

    this.persistCatalogs(nextMovementTypes, this.paymentMethods, nextMovementConfigs, this.paymentMethodConfigs, () => {
      this.closeCreate();
      this.newMovementType = '';
      this.newMovementIcon = this.movementIconOptions[0].icon;
      this.newMovementColor = this.movementColorOptions[0].hex;
    });
  }

  addPaymentMethod(): void {
    const value = this.newPaymentMethod.trim();
    if (!value) {
      this.notify.warning('Ingresa un medio de pago para agregar.');
      return;
    }

    if (this.exists(this.paymentMethods, value)) {
      this.notify.warning('Ese medio de pago ya existe.');
      return;
    }

    const nextPaymentMethods = [...this.paymentMethods, value];
    const nextPaymentMethodConfigs = this.upsertPaymentConfig(this.paymentMethodConfigs, value, this.newPaymentIcon);
    this.persistCatalogs(this.movementTypes, nextPaymentMethods, this.movementTypeConfigs, nextPaymentMethodConfigs, () => {
      this.closeCreate();
      this.newPaymentMethod = '';
      this.newPaymentIcon = '💳';
    });
  }

  openMovementIconSelector(target: 'newMovement' | 'editMovement'): void {
    this.openIconSelector(target);
  }

  openPaymentIconSelector(target: 'newPayment' | 'editPayment'): void {
    this.openIconSelector(target);
  }

  closeIconSelector(): void {
    this.iconSelectorOpen = false;
    this.iconSelectorTarget = null;
  }

  selectIcon(icon: string): void {
    if (this.iconSelectorTarget === 'newMovement') {
      this.newMovementIcon = icon;
    }

    if (this.iconSelectorTarget === 'editMovement') {
      this.editMovementIcon = icon;
    }

    if (this.iconSelectorTarget === 'newPayment') {
      this.newPaymentIcon = icon;
    }

    if (this.iconSelectorTarget === 'editPayment') {
      this.editPaymentIcon = icon;
    }

    this.closeIconSelector();
  }

  openEditMovementType(value: string): void {
    const meta = this.movementMeta(value);
    this.editTarget = 'movement';
    this.editTitle = 'Editar sección';
    this.editOriginalValue = value;
    this.editValue = value;
    this.editMovementIcon = meta.icon;
    this.editMovementColor = meta.color;
    this.editOpen = true;
  }

  openEditPaymentMethod(value: string): void {
    this.openEdit('payment', 'Editar medio de pago', value);
    this.editPaymentIcon = sanitizePaymentIcon(this.paymentConfigFor(value)?.icon)
      ?? resolvePaymentIcon(value, this.paymentMethodConfigs);
  }

  closeEdit(): void {
    this.editOpen = false;
    this.editTitle = '';
    this.editValue = '';
    this.editOriginalValue = '';
    this.editTarget = null;
  }

  confirmEdit(): void {
    const value = this.editValue.trim();
    if (!value || !this.editTarget || !this.editOriginalValue) {
      return;
    }

    if (this.editTarget === 'movement') {
      if (this.exists(this.movementTypes, value, this.editOriginalValue)) {
        this.notify.warning('Esa sección ya existe.');
        return;
      }

      const nextMovementTypes = this.replaceValue(this.movementTypes, this.editOriginalValue, value);
      const withoutCurrent = this.movementTypeConfigs.filter((item) => this.normalize(item.name) !== this.normalize(this.editOriginalValue));
      const nextMovementConfigs = this.upsertMovementConfig(withoutCurrent, value, this.editMovementIcon, this.editMovementColor);

      this.persistCatalogs(nextMovementTypes, this.paymentMethods, nextMovementConfigs, this.paymentMethodConfigs, () => {
        this.closeEdit();
      });
      return;
    }

    if (this.exists(this.paymentMethods, value, this.editOriginalValue)) {
      this.notify.warning('Ese medio de pago ya existe.');
      return;
    }

    const nextPaymentMethods = this.replaceValue(this.paymentMethods, this.editOriginalValue, value);
    const withoutCurrent = this.paymentMethodConfigs.filter((item) => this.normalize(item.name) !== this.normalize(this.editOriginalValue));
    const nextPaymentConfigs = this.upsertPaymentConfig(withoutCurrent, value, this.editPaymentIcon);

    this.persistCatalogs(this.movementTypes, nextPaymentMethods, this.movementTypeConfigs, nextPaymentConfigs, () => {
      this.closeEdit();
    });
  }

  openDeleteMovementType(value: string): void {
    this.deleteTarget = 'movement';
    this.deleteValue = value;
    this.confirmDeleteOpen = true;
  }

  openDeletePaymentMethod(value: string): void {
    this.deleteTarget = 'payment';
    this.deleteValue = value;
    this.confirmDeleteOpen = true;
  }

  cancelDelete(): void {
    this.confirmDeleteOpen = false;
    this.deleteTarget = null;
    this.deleteValue = '';
  }

  confirmDelete(): void {
    if (!this.deleteTarget || !this.deleteValue) {
      this.cancelDelete();
      return;
    }

    if (this.deleteTarget === 'movement') {
      const nextMovementTypes = this.movementTypes.filter((item) => item !== this.deleteValue);
      const nextMovementConfigs = this.movementTypeConfigs.filter((item) => this.normalize(item.name) !== this.normalize(this.deleteValue));

      this.persistCatalogs(nextMovementTypes, this.paymentMethods, nextMovementConfigs, this.paymentMethodConfigs, () => {
        this.cancelDelete();
      });
      return;
    }

    const nextPaymentMethods = this.paymentMethods.filter((item) => item !== this.deleteValue);
    const nextPaymentConfigs = this.paymentMethodConfigs.filter((item) => this.normalize(item.name) !== this.normalize(this.deleteValue));

    this.persistCatalogs(this.movementTypes, nextPaymentMethods, this.movementTypeConfigs, nextPaymentConfigs, () => {
      this.cancelDelete();
    });
  }

  private loadCatalogs(): void {
    this.isLoading = true;
    this.api
      .getCatalogs()
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (catalogs) => {
          this.movementTypes = [...catalogs.movementTypes];
          this.paymentMethods = [...catalogs.paymentMethods];

          const incomingMovementConfigs = [...(catalogs.movementTypeConfigs ?? [])];
          this.movementTypeConfigs = this.buildNormalizedMovementConfigs(this.movementTypes, incomingMovementConfigs);

          const incomingPaymentConfigs = [...(catalogs.paymentMethodConfigs ?? [])];
          this.paymentMethodConfigs = this.buildNormalizedPaymentConfigs(this.paymentMethods, incomingPaymentConfigs);
        },
        error: () => {
          this.notify.error('No fue posible cargar los catálogos.');
        }
      });
  }

  private openEdit(target: 'movement' | 'payment', title: string, currentValue: string): void {
    this.editTarget = target;
    this.editTitle = title;
    this.editOriginalValue = currentValue;
    this.editValue = currentValue;
    this.editOpen = true;
  }

  private exists(items: string[], value: string, ignoreValue?: string): boolean {
    return items.some((item) => {
      if (ignoreValue && item === ignoreValue) {
        return false;
      }

      return item.localeCompare(value, 'es', { sensitivity: 'accent' }) === 0;
    });
  }

  private replaceValue(items: string[], oldValue: string, newValue: string): string[] {
    return items.map((item) => (item === oldValue ? newValue : item));
  }

  private persistCatalogs(
    movementTypes: string[],
    paymentMethods: string[],
    movementTypeConfigs: MovementTypeConfig[],
    paymentMethodConfigs: PaymentMethodConfig[],
    onSuccess: () => void
  ): void {
    if (!movementTypes.length || !paymentMethods.length) {
      this.notify.warning('Debe existir al menos una sección y un medio de pago.');
      return;
    }

    const normalizedMovementConfigs = this.buildNormalizedMovementConfigs(movementTypes, movementTypeConfigs);
    const normalizedPaymentConfigs = this.buildNormalizedPaymentConfigs(paymentMethods, paymentMethodConfigs);

    this.isSaving = true;

    this.api
      .updateCatalogs({
        movementTypes,
        paymentMethods,
        movementTypeConfigs: normalizedMovementConfigs,
        paymentMethodConfigs: normalizedPaymentConfigs
      })
      .pipe(finalize(() => (this.isSaving = false)))
      .subscribe({
        next: () => {
          this.movementTypes = movementTypes;
          this.paymentMethods = paymentMethods;
          this.movementTypeConfigs = normalizedMovementConfigs;
          this.paymentMethodConfigs = normalizedPaymentConfigs;
          this.notify.success('Catalogos actualizados correctamente.');
          onSuccess();
        },
        error: () => {
          this.notify.error('No fue posible actualizar los catálogos.');
        }
      });
  }

  private normalize(value: string): string {
    return normalizeCatalogKey(value);
  }

  private openIconSelector(target: 'newMovement' | 'editMovement' | 'newPayment' | 'editPayment'): void {
    this.iconSelectorTarget = target;
    this.iconSelectorOpen = true;
  }

  private paymentConfigFor(paymentMethod: string, source = this.paymentMethodConfigs): PaymentMethodConfig | undefined {
    return source.find((config) => this.normalize(config.name) === this.normalize(paymentMethod));
  }

  private upsertMovementConfig(configs: MovementTypeConfig[], name: string, icon: string, color: string): MovementTypeConfig[] {
    const next = configs.filter((item) => this.normalize(item.name) !== this.normalize(name));
    next.push({
      name,
      icon: sanitizeMovementIcon(icon) ?? resolveMovementIcon(name, configs),
      color: color.trim() || resolveMovementColor(name, configs)
    });
    return next;
  }

  private upsertPaymentConfig(configs: PaymentMethodConfig[], name: string, icon: string): PaymentMethodConfig[] {
    const next = configs.filter((item) => this.normalize(item.name) !== this.normalize(name));
    next.push({ name, icon: sanitizePaymentIcon(icon) ?? resolvePaymentIcon(name, configs) });
    return next;
  }

  private buildNormalizedMovementConfigs(movementTypes: string[], sourceConfigs: MovementTypeConfig[]): MovementTypeConfig[] {
    return movementTypes.map((movementType) => {
      const currentConfig = sourceConfigs.find((item) => this.normalize(item.name) === this.normalize(movementType));
      return {
        name: movementType,
        icon: sanitizeMovementIcon(currentConfig?.icon) ?? resolveMovementIcon(movementType, sourceConfigs),
        color: currentConfig?.color?.trim() || resolveMovementColor(movementType, sourceConfigs)
      };
    });
  }

  private buildNormalizedPaymentConfigs(paymentMethods: string[], sourceConfigs: PaymentMethodConfig[]): PaymentMethodConfig[] {
    return paymentMethods.map((paymentMethod) => {
      const currentConfig = sourceConfigs.find((item) => this.normalize(item.name) === this.normalize(paymentMethod));
      return {
        name: paymentMethod,
        icon: sanitizePaymentIcon(currentConfig?.icon) ?? resolvePaymentIcon(paymentMethod, sourceConfigs)
      };
    });
  }

}
