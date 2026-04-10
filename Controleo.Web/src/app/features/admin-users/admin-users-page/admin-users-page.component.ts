import { HttpErrorResponse } from '@angular/common/http';
import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { finalize } from 'rxjs';
import { AdminUserItem, AdminUserUpdateRequest } from '../../../core/models/api.models';
import { ApiService } from '../../../core/services/api.service';
import { AuthService } from '../../../core/services/auth.service';
import { NotificationService } from '../../../core/services/notification.service';
import { ConfirmModalComponent } from '../../../shared/ui/confirm-modal/confirm-modal.component';

interface EditableAdminUser extends AdminUserItem {
  draftIsPremium: boolean;
  draftIsAdmin: boolean;
  draftIsDisabled: boolean;
  isSaving: boolean;
  isDeleting: boolean;
}

@Component({
  selector: 'app-admin-users-page',
  imports: [CommonModule, FormsModule, ConfirmModalComponent],
  templateUrl: './admin-users-page.component.html',
  styleUrl: './admin-users-page.component.scss'
})
export class AdminUsersPageComponent {
  readonly allowedPageSizes = [5, 10, 20];

  isLoading = false;
  isRefreshing = false;
  searchTerm = '';
  pageNumber = 1;
  pageSize = 5;
  totalPages = 1;
  totalCount = 0;
  users: EditableAdminUser[] = [];
  confirmDeleteOpen = false;
  pendingDelete: EditableAdminUser | null = null;
  impersonatingUserId: string | null = null;

  constructor(
    private readonly api: ApiService,
    private readonly auth: AuthService,
    private readonly router: Router,
    private readonly notify: NotificationService
  ) {
    this.loadUsers();
  }

  get paginationText(): string {
    return `Página ${this.pageNumber}/${this.totalPages} · ${this.totalCount} registros`;
  }

  trackByUserId(_: number, item: EditableAdminUser): string {
    return item.userId;
  }

  applySearch(): void {
    this.pageNumber = 1;
    this.loadUsers(true);
  }

  clearSearch(): void {
    if (!this.searchTerm.trim()) {
      return;
    }

    this.searchTerm = '';
    this.pageNumber = 1;
    this.loadUsers(true);
  }

  refresh(): void {
    this.loadUsers(true);
  }

  setPageSize(size: number): void {
    const safeSize = this.allowedPageSizes.includes(size) ? size : 5;
    if (safeSize === this.pageSize) {
      return;
    }

    this.pageSize = safeSize;
    this.pageNumber = 1;
    this.loadUsers(true);
  }

  prevPage(): void {
    if (this.pageNumber <= 1 || this.isRefreshing) {
      return;
    }

    this.pageNumber -= 1;
    this.loadUsers(true);
  }

  nextPage(): void {
    if (this.pageNumber >= this.totalPages || this.isRefreshing) {
      return;
    }

    this.pageNumber += 1;
    this.loadUsers(true);
  }

  onAdminToggle(user: EditableAdminUser): void {
    if (user.draftIsAdmin) {
      user.draftIsPremium = true;
    }
  }

  hasChanges(user: EditableAdminUser): boolean {
    return user.draftIsAdmin !== user.isAdmin ||
      user.draftIsPremium !== user.isPremium ||
      user.draftIsDisabled !== user.isDisabled;
  }

  save(user: EditableAdminUser): void {
    if (user.isSaving || user.isDeleting || !this.hasChanges(user)) {
      return;
    }

    const payload: AdminUserUpdateRequest = {
      isAdmin: user.draftIsAdmin,
      isPremium: user.draftIsPremium || user.draftIsAdmin,
      isDisabled: user.draftIsDisabled
    };

    user.isSaving = true;
    this.api.updateAdminUser(user.userId, payload)
      .pipe(finalize(() => (user.isSaving = false)))
      .subscribe({
        next: (result) => {
          if (!result.isSuccess) {
            this.notify.warning(result.message || 'No fue posible actualizar el usuario.');
            return;
          }

          user.isAdmin = payload.isAdmin;
          user.isPremium = payload.isPremium;
          user.isDisabled = payload.isDisabled;
          user.draftIsAdmin = user.isAdmin;
          user.draftIsPremium = user.isPremium;
          user.draftIsDisabled = user.isDisabled;
          this.notify.success(result.message || 'Usuario actualizado correctamente.');
        },
        error: (error: unknown) => {
          this.notify.error(this.resolveErrorMessage(error, 'No fue posible actualizar el usuario.'));
        }
      });
  }

  requestDelete(user: EditableAdminUser): void {
    if (user.isDeleting || user.isSuperAdmin || this.impersonatingUserId === user.userId) {
      return;
    }

    this.pendingDelete = user;
    this.confirmDeleteOpen = true;
  }

  confirmDelete(): void {
    if (!this.pendingDelete) {
      this.confirmDeleteOpen = false;
      return;
    }

    const target = this.pendingDelete;
    this.pendingDelete = null;
    this.confirmDeleteOpen = false;

    if (target.isDeleting) {
      return;
    }

    target.isDeleting = true;
    this.api.deleteAdminUser(target.userId)
      .pipe(finalize(() => (target.isDeleting = false)))
      .subscribe({
        next: (result) => {
          if (!result.isSuccess) {
            this.notify.warning(result.message || 'No fue posible eliminar el usuario.');
            return;
          }

          this.notify.success(result.message || 'Usuario eliminado correctamente.');
          this.loadUsers(true);
        },
        error: (error: unknown) => {
          this.notify.error(this.resolveErrorMessage(error, 'No fue posible eliminar el usuario.'));
        }
      });
  }

  cancelDelete(): void {
    this.pendingDelete = null;
    this.confirmDeleteOpen = false;
  }

  startImpersonation(user: EditableAdminUser): void {
    if (user.isSuperAdmin || user.isDisabled || this.impersonatingUserId) {
      return;
    }

    this.impersonatingUserId = user.userId;
    this.api.impersonateUser(user.userId)
      .pipe(finalize(() => (this.impersonatingUserId = null)))
      .subscribe({
        next: async (session) => {
          try {
            await this.auth.activateImpersonationSession(session);
            this.notify.success(`Ahora estás actuando como ${user.email}.`);
            await this.router.navigateByUrl('/registrar');
          } catch (error: unknown) {
            this.notify.error(this.resolveErrorMessage(error, 'No fue posible activar la sesión de suplantación.'));
          }
        },
        error: (error: unknown) => {
          this.notify.error(this.resolveErrorMessage(error, 'No fue posible iniciar suplantación.'));
        }
      });
  }

  formatDate(value: string): string {
    const timestamp = Date.parse(value);
    if (Number.isNaN(timestamp)) {
      return 'Sin dato';
    }

    return new Date(timestamp).toLocaleString('es-CO', {
      dateStyle: 'medium',
      timeStyle: 'short'
    });
  }

  private loadUsers(notifyOnError = false): void {
    if (!this.isLoading) {
      this.isLoading = this.users.length === 0;
    }

    this.isRefreshing = true;
    this.api.getAdminUsersPage(this.pageNumber, this.pageSize, this.searchTerm)
      .pipe(finalize(() => {
        this.isLoading = false;
        this.isRefreshing = false;
      }))
      .subscribe({
        next: (page) => {
          this.pageNumber = page.pageNumber;
          this.pageSize = page.pageSize;
          this.totalPages = page.totalPages;
          this.totalCount = page.totalCount;
          this.users = page.items.map((item) => this.toEditable(item));
        },
        error: (error: unknown) => {
          if (notifyOnError || this.users.length === 0) {
            this.notify.error(this.resolveErrorMessage(error, 'No fue posible cargar usuarios.'));
          }
        }
      });
  }

  private toEditable(item: AdminUserItem): EditableAdminUser {
    return {
      ...item,
      draftIsPremium: item.isPremium,
      draftIsAdmin: item.isAdmin,
      draftIsDisabled: item.isDisabled,
      isSaving: false,
      isDeleting: false
    };
  }

  private resolveErrorMessage(error: unknown, fallback: string): string {
    if (error instanceof HttpErrorResponse) {
      const payload = error.error;
      if (payload && typeof payload === 'object' && 'message' in payload) {
        const message = (payload as { message?: unknown }).message;
        if (typeof message === 'string' && message.trim()) {
          return message;
        }
      }

      if (error.status === 0) {
        return 'No hay conexión con la API local. Verifica que Functions esté corriendo en http://localhost:5051.';
      }

      if (error.status === 401) {
        return 'Tu sesión no es válida. Inicia sesión nuevamente.';
      }

      if (error.status === 403) {
        return 'No tienes permisos para ejecutar esta operación.';
      }

      if (error.status === 404) {
        return 'El usuario ya no existe o fue eliminado por otro administrador.';
      }

      if (error.status >= 500) {
        return 'El servidor tuvo un error. Intenta de nuevo en unos minutos.';
      }

      if (error.status >= 400) {
        return fallback;
      }
    }

    return fallback;
  }
}
