import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { BudgetItem, MovementTypeConfig } from '../../../core/models/api.models';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import { normalizeCatalogKey, resolveMovementColor, resolveMovementIcon } from '../../../core/utils/catalog-visual.utils';

interface BudgetViewItem {
  movementType: string;
  amount: number;
  updatedAt: string | null;
  hasBudget: boolean;
  icon: string;
  color: string;
}

@Component({
  selector: 'app-budgets-page',
  imports: [CommonModule, ReactiveFormsModule, ConfirmModalComponent],
  templateUrl: './budgets-page.component.html',
  styleUrl: './budgets-page.component.scss'
})
export class BudgetsPageComponent {
  movementTypes: string[] = [];
  movementTypeConfigs: MovementTypeConfig[] = [];
  budgets: BudgetItem[] = [];
  items: BudgetViewItem[] = [];
  confirmDeleteOpen = false;
  pendingDelete: BudgetItem | null = null;
  editorOpen = false;
  editingItem: BudgetViewItem | null = null;

  readonly form;

  constructor(
    private readonly api: ApiService,
    private readonly fb: FormBuilder,
    private readonly notify: NotificationService
  ) {
    this.form = this.fb.nonNullable.group({
      amount: [0, Validators.required]
    });

    this.loadData();
  }

  openEditor(item: BudgetViewItem): void {
    this.editingItem = item;
    this.form.patchValue({ amount: item.amount || 0 });
    this.editorOpen = true;
  }

  closeEditor(): void {
    this.editorOpen = false;
    this.editingItem = null;
  }

  onEditorBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closeEditor();
    }
  }

  saveEditor(): void {
    if (!this.editingItem || this.form.invalid) {
      return;
    }

    const value = this.form.getRawValue();
    const parsedAmount = Number(value.amount);
    if (!Number.isFinite(parsedAmount) || parsedAmount < 0) {
      this.notify.warning('El presupuesto debe ser numérico y no negativo.');
      return;
    }
    const isUpdate = this.editingItem.hasBudget;

    this.api.upsertBudget(this.editingItem.movementType, { amount: parsedAmount }).subscribe({
      next: () => {
        this.notify.success(
          isUpdate
            ? 'Presupuesto actualizado correctamente.'
            : 'Presupuesto guardado correctamente.'
        );
        this.closeEditor();
        this.refreshBudgets();
      },
      error: () => {
        this.notify.error('No fue posible guardar el presupuesto.');
      }
    });
  }

  promptDelete(item: BudgetViewItem): void {
    if (!item.hasBudget) {
      return;
    }

    this.remove({ movementType: item.movementType, amount: item.amount, updatedAt: item.updatedAt ?? new Date().toISOString() });
  }

  private remove(item: BudgetItem): void {
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
    this.closeEditor();

    this.api.deleteBudget(item.movementType).subscribe({
      next: () => {
        this.notify.success('Presupuesto eliminado correctamente.');
        this.refreshBudgets();
      },
      error: () => {
        this.notify.error('No fue posible eliminar el presupuesto.');
      }
    });
  }

  cancelDelete(): void {
    this.confirmDeleteOpen = false;
    this.pendingDelete = null;
  }

  private loadData(): void {
    this.api.getCatalogs().subscribe({
      next: (catalog) => {
        this.movementTypes = catalog.movementTypes;
        this.movementTypeConfigs = catalog.movementTypeConfigs ?? [];
        this.buildItems();
      }
    });

    this.refreshBudgets();
  }

  private refreshBudgets(): void {
    this.api.getBudgets().subscribe({
      next: (items) => {
        this.budgets = items;
        this.buildItems();
      },
      error: () => {
        this.notify.error('No fue posible cargar presupuestos.');
      }
    });
  }

  private buildItems(): void {
    if (!this.movementTypes.length) {
      this.items = [];
      return;
    }

    const budgetMap = new Map(this.budgets.map((budget) => [this.normalize(budget.movementType), budget]));
    this.items = this.movementTypes
      .map((movementType) => {
        const budget = budgetMap.get(this.normalize(movementType));
        const hasBudget = !!budget && budget.amount > 0;

        return {
          movementType,
          amount: hasBudget ? Number(budget?.amount ?? 0) : 0,
          updatedAt: budget?.updatedAt ?? null,
          hasBudget,
          icon: resolveMovementIcon(movementType, this.movementTypeConfigs),
          color: resolveMovementColor(movementType, this.movementTypeConfigs)
        } as BudgetViewItem;
      })
      .sort((left, right) => {
        if (left.hasBudget !== right.hasBudget) {
          return left.hasBudget ? -1 : 1;
        }

        return right.amount - left.amount;
      });
  }

  private normalize(value: string): string {
    return normalizeCatalogKey(value);
  }

}
