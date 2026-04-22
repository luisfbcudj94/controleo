import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';
import {
  SavingsGoalItem,
  GoalContributionItem,
  GoalSimulationResponse,
  GoalAlertItem
} from '../../../core/models/api.models';

@Component({
  selector: 'app-goals-page',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './goals-page.component.html',
  styleUrl: './goals-page.component.scss'
})
export class GoalsPageComponent {
  goals: SavingsGoalItem[] = [];
  alerts: GoalAlertItem[] = [];
  loading = false;
  selectedGoal: SavingsGoalItem | null = null;

  // Create/Edit
  editorOpen = false;
  editingGoalId: string | null = null;
  goalName = '';
  goalIcon = '🎯';
  goalAmount = 0;
  goalDate = '';
  goalPriority = 'Media';

  // Contribution
  contributionOpen = false;
  contributionAmount = 0;
  contributionNote = '';

  // Simulator
  simAmount = 0;
  simResult: GoalSimulationResponse | null = null;

  // Delete
  confirmDeleteOpen = false;
  pendingDeleteGoalId: string | null = null;

  readonly iconOptions = ['🎯', '🏠', '✈️', '🚗', '🎓', '💎', '💰', '🏦'];
  readonly priorityOptions = ['Alta', 'Media', 'Baja'];
  readonly cardColors = ['#D8F3DC', '#D4EAFF', '#FFE5D4', '#E8DEFF', '#FFF0D4', '#DFEEF7'];

  constructor(
    private readonly api: ApiService,
    private readonly notify: NotificationService
  ) {
    this.loadGoals();
  }

  loadGoals(): void {
    this.loading = true;
    this.api.getGoals().subscribe({
      next: (data) => {
        this.goals = data;
        this.loading = false;
      },
      error: () => {
        this.notify.error('No fue posible cargar las metas.');
        this.loading = false;
      }
    });
    this.api.getGoalAlerts().subscribe({
      next: (data) => this.alerts = data,
      error: () => {}
    });
  }

  get totalSaved(): number {
    return this.goals.reduce((s, g) => s + g.currentAmount, 0);
  }

  get totalTarget(): number {
    return this.goals.reduce((s, g) => s + g.targetAmount, 0);
  }

  get overallPercent(): number {
    return this.totalTarget > 0 ? Math.min(100, Math.round(this.totalSaved / this.totalTarget * 100)) : 0;
  }

  get activeGoals(): SavingsGoalItem[] {
    return this.goals.filter(g => g.status === 'Active');
  }

  get onTrackCount(): number {
    return this.activeGoals.filter(g => g.isOnTrack).length;
  }

  cardColor(index: number): string {
    return this.cardColors[index % this.cardColors.length];
  }

  progressColor(percent: number): string {
    if (percent >= 75) return '#2D6A4F';
    if (percent >= 50) return '#40916C';
    if (percent >= 25) return '#E6A817';
    return '#D4765A';
  }

  statusBadge(g: SavingsGoalItem): string {
    if (g.status === 'Completed') return '✓ Completada';
    return g.isOnTrack ? 'En camino' : 'Atención';
  }

  statusBadgeClass(g: SavingsGoalItem): string {
    if (g.status === 'Completed') return 'badge-success';
    return g.isOnTrack ? 'badge-success' : 'badge-warning';
  }

  // ── Create/Edit ──

  openCreate(): void {
    this.editingGoalId = null;
    this.goalName = '';
    this.goalIcon = '🎯';
    this.goalAmount = 0;
    const sixMonths = new Date();
    sixMonths.setMonth(sixMonths.getMonth() + 6);
    this.goalDate = sixMonths.toISOString().split('T')[0];
    this.goalPriority = 'Media';
    this.editorOpen = true;
  }

  openEdit(g: SavingsGoalItem): void {
    this.editingGoalId = g.id;
    this.goalName = g.name;
    this.goalIcon = g.icon;
    this.goalAmount = g.targetAmount;
    this.goalDate = g.targetDate;
    this.goalPriority = g.priority;
    this.editorOpen = true;
  }

  closeEditor(): void {
    this.editorOpen = false;
    this.editingGoalId = null;
  }

  saveGoal(): void {
    if (!this.goalName.trim() || this.goalAmount <= 0 || !this.goalDate) return;
    const request = {
      name: this.goalName.trim(),
      icon: this.goalIcon,
      targetAmount: this.goalAmount,
      targetDate: this.goalDate,
      priority: this.goalPriority
    };
    const obs = this.editingGoalId
      ? this.api.updateGoal(this.editingGoalId, request)
      : this.api.createGoal(request);
    obs.subscribe({
      next: () => {
        this.notify.success(this.editingGoalId ? 'Meta actualizada.' : '¡Meta creada!');
        this.closeEditor();
        this.loadGoals();
      },
      error: () => this.notify.error('No fue posible guardar la meta.')
    });
  }

  // ── Detail/Select ──

  selectGoal(g: SavingsGoalItem): void {
    if (this.selectedGoal?.id === g.id) {
      this.selectedGoal = null;
      return;
    }
    this.api.getGoalDetail(g.id).subscribe({
      next: (detail) => {
        this.selectedGoal = detail;
        this.simResult = null;
        this.simAmount = 0;
      },
      error: () => this.notify.error('No fue posible cargar el detalle.')
    });
  }

  // ── Contributions ──

  openContribution(): void {
    this.contributionAmount = 0;
    this.contributionNote = '';
    this.contributionOpen = true;
  }

  closeContribution(): void {
    this.contributionOpen = false;
  }

  saveContribution(): void {
    if (!this.selectedGoal || this.contributionAmount <= 0) return;
    const today = new Date().toISOString().split('T')[0];
    this.api.addGoalContribution(this.selectedGoal.id, {
      amount: this.contributionAmount,
      date: today,
      note: this.contributionNote.trim() || undefined
    }).subscribe({
      next: () => {
        this.notify.success('¡Aporte registrado!');
        this.closeContribution();
        this.loadGoals();
        if (this.selectedGoal) this.selectGoal(this.selectedGoal);
      },
      error: () => this.notify.error('No fue posible registrar el aporte.')
    });
  }

  deleteContribution(contributionId: string): void {
    if (!this.selectedGoal) return;
    this.api.deleteGoalContribution(this.selectedGoal.id, contributionId).subscribe({
      next: () => {
        this.notify.success('Aporte eliminado.');
        this.loadGoals();
        if (this.selectedGoal) this.selectGoal(this.selectedGoal);
      },
      error: () => this.notify.error('No fue posible eliminar el aporte.')
    });
  }

  // ── Simulator ──

  simulate(): void {
    if (!this.selectedGoal || this.simAmount <= 0) return;
    this.api.simulateGoal(this.selectedGoal.id, {
      scenarioType: 'ChangeAmount',
      newValue: this.simAmount
    }).subscribe({
      next: (res) => this.simResult = res,
      error: () => this.notify.error('No fue posible simular.')
    });
  }

  // ── Delete ──

  requestDelete(goalId: string): void {
    this.pendingDeleteGoalId = goalId;
    this.confirmDeleteOpen = true;
  }

  confirmDelete(): void {
    if (!this.pendingDeleteGoalId) return;
    this.api.deleteGoal(this.pendingDeleteGoalId).subscribe({
      next: () => {
        this.notify.success('Meta eliminada.');
        this.confirmDeleteOpen = false;
        this.pendingDeleteGoalId = null;
        if (this.selectedGoal?.id === this.pendingDeleteGoalId) this.selectedGoal = null;
        this.loadGoals();
      },
      error: () => this.notify.error('No fue posible eliminar la meta.')
    });
  }

  cancelDelete(): void {
    this.confirmDeleteOpen = false;
    this.pendingDeleteGoalId = null;
  }
}
