import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { finalize } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { FormsModule } from '@angular/forms';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';

@Component({
  selector: 'app-settings-page',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.scss'
})
export class SettingsPageComponent {
  isLoading = false;
  isSaving = false;
  statusMessage = '';
  movementTypes: string[] = [];
  paymentMethods: string[] = [];
  newMovementType = '';
  newPaymentMethod = '';
  selectedMovementType = '';
  selectedPaymentMethod = '';

  editOpen = false;
  editTitle = '';
  editValue = '';
  private editOriginalValue = '';
  private editTarget: 'movement' | 'payment' | null = null;

  confirmDeleteOpen = false;
  private deleteTarget: 'movement' | 'payment' | null = null;
  private deleteValue = '';

  constructor(private readonly api: ApiService) {
    this.loadCatalogs();
  }

  get deleteMessage(): string {
    if (!this.deleteValue) {
      return '';
    }

    return this.deleteTarget === 'movement'
      ? `¿Deseas eliminar la sección '${this.deleteValue}'?`
      : `¿Deseas eliminar el medio de pago '${this.deleteValue}'?`;
  }

  selectMovementType(item: string): void {
    this.selectedMovementType = item;
  }

  selectPaymentMethod(item: string): void {
    this.selectedPaymentMethod = item;
  }

  addMovementType(): void {
    const value = this.newMovementType.trim();
    if (!value) {
      this.statusMessage = 'Ingresa una sección para agregar.';
      return;
    }

    if (this.exists(this.movementTypes, value)) {
      this.statusMessage = 'Esa sección ya existe.';
      return;
    }

    const nextMovementTypes = [...this.movementTypes, value];
    this.persistCatalogs(nextMovementTypes, this.paymentMethods, () => {
      this.newMovementType = '';
      this.selectedMovementType = value;
    });
  }

  addPaymentMethod(): void {
    const value = this.newPaymentMethod.trim();
    if (!value) {
      this.statusMessage = 'Ingresa un medio de pago para agregar.';
      return;
    }

    if (this.exists(this.paymentMethods, value)) {
      this.statusMessage = 'Ese medio de pago ya existe.';
      return;
    }

    const nextPaymentMethods = [...this.paymentMethods, value];
    this.persistCatalogs(this.movementTypes, nextPaymentMethods, () => {
      this.newPaymentMethod = '';
      this.selectedPaymentMethod = value;
    });
  }

  openEditMovementType(): void {
    if (!this.selectedMovementType) {
      this.statusMessage = 'Selecciona una sección para editar.';
      return;
    }

    this.openEdit('movement', 'Editar sección', this.selectedMovementType);
  }

  openEditPaymentMethod(): void {
    if (!this.selectedPaymentMethod) {
      this.statusMessage = 'Selecciona un medio de pago para editar.';
      return;
    }

    this.openEdit('payment', 'Editar medio de pago', this.selectedPaymentMethod);
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
        this.statusMessage = 'Esa sección ya existe.';
        return;
      }

      const nextMovementTypes = this.replaceValue(this.movementTypes, this.editOriginalValue, value);
      this.persistCatalogs(nextMovementTypes, this.paymentMethods, () => {
        this.selectedMovementType = value;
        this.closeEdit();
      });
      return;
    }

    if (this.exists(this.paymentMethods, value, this.editOriginalValue)) {
      this.statusMessage = 'Ese medio de pago ya existe.';
      return;
    }

    const nextPaymentMethods = this.replaceValue(this.paymentMethods, this.editOriginalValue, value);
    this.persistCatalogs(this.movementTypes, nextPaymentMethods, () => {
      this.selectedPaymentMethod = value;
      this.closeEdit();
    });
  }

  openDeleteMovementType(): void {
    if (!this.selectedMovementType) {
      this.statusMessage = 'Selecciona una sección para eliminar.';
      return;
    }

    this.deleteTarget = 'movement';
    this.deleteValue = this.selectedMovementType;
    this.confirmDeleteOpen = true;
  }

  openDeletePaymentMethod(): void {
    if (!this.selectedPaymentMethod) {
      this.statusMessage = 'Selecciona un medio de pago para eliminar.';
      return;
    }

    this.deleteTarget = 'payment';
    this.deleteValue = this.selectedPaymentMethod;
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
      this.persistCatalogs(nextMovementTypes, this.paymentMethods, () => {
        this.selectedMovementType = '';
        this.cancelDelete();
      });
      return;
    }

    const nextPaymentMethods = this.paymentMethods.filter((item) => item !== this.deleteValue);
    this.persistCatalogs(this.movementTypes, nextPaymentMethods, () => {
      this.selectedPaymentMethod = '';
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
          this.selectedMovementType = '';
          this.selectedPaymentMethod = '';
          this.statusMessage = '';
        },
        error: () => {
          this.statusMessage = 'No fue posible cargar los catálogos.';
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

  private persistCatalogs(movementTypes: string[], paymentMethods: string[], onSuccess: () => void): void {
    if (!movementTypes.length || !paymentMethods.length) {
      this.statusMessage = 'Debe existir al menos una sección y un medio de pago.';
      return;
    }

    this.isSaving = true;
    this.statusMessage = 'Guardando catálogos...';

    this.api
      .updateCatalogs({ movementTypes, paymentMethods })
      .pipe(finalize(() => (this.isSaving = false)))
      .subscribe({
        next: (result) => {
          this.movementTypes = movementTypes;
          this.paymentMethods = paymentMethods;
          this.statusMessage = result.message;
          onSuccess();
        },
        error: () => {
          this.statusMessage = 'No fue posible actualizar los catálogos.';
        }
      });
  }

}
