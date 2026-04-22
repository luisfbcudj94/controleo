import { Component, HostListener } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../../../core/services/auth.service';
import { filter, firstValueFrom } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';
import { NotificationService } from '../../../core/services/notification.service';
import { PushNotificationService } from '../../../core/services/push-notification.service';

@Component({
  selector: 'app-app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app-shell.component.html',
  styleUrl: './app-shell.component.scss'
})
export class AppShellComponent {
  readonly primaryNav = [
    { path: '/dashboard', icon: '📊', label: 'Dashboard' },
    { path: '/gastos', icon: '📋', label: 'Mis gastos' },
    { path: '/registrar', icon: '➕', label: 'Registrar gasto' }
  ];

  readonly mobileTabs = [
    { path: '/registrar', icon: '🏠', label: 'Registro' },
    { path: '/gastos', icon: '📋', label: 'Gastos' },
    { path: '/dashboard', icon: '📊', label: 'Dashboard' }
  ];

  readonly analysisNav = [
    { path: '/presupuestos', icon: '🎯', label: 'Presupuestos' },
    { path: '/recurrentes', icon: '🔁', label: 'Recurrentes' },
    { path: '/metas', icon: '🏦', label: 'Metas de Ahorro' },
    { path: '/coach', icon: '🤖', label: 'Coach IA' },
    { path: '/configuracion', icon: '⚙️', label: 'Configuración' }
  ];

  readonly adminNav = [
    { path: '/admin/usuarios', icon: '🛡️', label: 'Usuarios' }
  ];

  currentMonthDate = new Date();
  isGirlMode = false;
  isSidebarOpen = false;
  isEndingImpersonation = false;

  constructor(
    private readonly router: Router,
    private readonly auth: AuthService,
    private readonly api: ApiService,
    private readonly notify: NotificationService,
    readonly pushNotif: PushNotificationService
  ) {
    this.initializeTheme();

    this.router.events
      .pipe(filter((event): event is NavigationEnd => event instanceof NavigationEnd))
      .subscribe(() => {
        this.isSidebarOpen = false;
      });
  }

  get userInitials(): string {
    return this.auth.userInitials;
  }

  get userName(): string {
    return this.auth.currentUserName;
  }

  get userEmail(): string {
    return this.auth.currentUserEmail;
  }

  get pageTitle(): string {
    return this.getPageMeta().title;
  }

  get isAdmin(): boolean {
    return this.auth.isAdmin;
  }

  get isImpersonating(): boolean {
    return this.auth.isImpersonating;
  }

  get impersonationActorName(): string {
    return this.auth.impersonationActorName || 'Administrador';
  }

  get pageCrumb(): string {
    return this.getPageMeta().crumb;
  }

  get monthLabel(): string {
    return this.currentMonthDate.toLocaleDateString('es-CO', { month: 'long', year: 'numeric' });
  }

  isActive(path: string): boolean {
    return this.router.url === path || this.router.url.startsWith(`${path}/`);
  }

  previousMonth(): void {
    this.currentMonthDate = new Date(this.currentMonthDate.getFullYear(), this.currentMonthDate.getMonth() - 1, 1);
  }

  nextMonth(): void {
    this.currentMonthDate = new Date(this.currentMonthDate.getFullYear(), this.currentMonthDate.getMonth() + 1, 1);
  }

  toggleSidebar(): void {
    this.isSidebarOpen = !this.isSidebarOpen;
  }

  closeSidebar(): void {
    this.isSidebarOpen = false;
  }

  openMenuFromTab(): void {
    this.isSidebarOpen = true;
  }

  @HostListener('window:resize')
  onResize(): void {
    if (window.innerWidth >= 1024 && this.isSidebarOpen) {
      this.isSidebarOpen = false;
    }
  }

  togglePushNotifications(): void {
    if (this.pushNotif.isActive()) {
      this.pushNotif.stop();
    } else {
      this.pushNotif.start();
    }
  }

  toggleTheme(): void {
    this.isGirlMode = !this.isGirlMode;
    this.applyTheme();
  }

  private getPageMeta(): { title: string; crumb: string } {
    const routePath = this.router.url.split('?')[0];

    if (routePath.startsWith('/dashboard')) {
      return { title: 'Dashboard', crumb: 'Resumen general' };
    }

    if (routePath.startsWith('/gastos')) {
      return { title: 'Mis gastos', crumb: 'Listado de movimientos' };
    }

    if (routePath.startsWith('/presupuestos')) {
      return { title: 'Presupuestos', crumb: 'Metas por categoría' };
    }

    if (routePath.startsWith('/recurrentes')) {
      return { title: 'Recurrentes', crumb: 'Programación mensual' };
    }

    if (routePath.startsWith('/configuracion')) {
      return { title: 'Configuración', crumb: 'Catálogos y preferencias' };
    }

    if (routePath.startsWith('/admin/usuarios')) {
      return { title: 'Administración', crumb: 'Usuarios y permisos' };
    }

    return { title: 'Registrar gasto', crumb: 'Nuevo movimiento' };
  }

  private initializeTheme(): void {
    const savedTheme = localStorage.getItem('controleo-theme');
    this.isGirlMode = savedTheme === 'girl';
    this.applyTheme();
  }

  private applyTheme(): void {
    document.body.setAttribute('data-theme', this.isGirlMode ? 'girl' : 'dark');
    localStorage.setItem('controleo-theme', this.isGirlMode ? 'girl' : 'dark');
  }

  async logout(): Promise<void> {
    await this.auth.logout();
    await this.router.navigateByUrl('/login');
  }

  async exitImpersonation(): Promise<void> {
    if (this.isEndingImpersonation) {
      return;
    }

    this.isEndingImpersonation = true;
    let auditErrorMessage = '';

    try {
      try {
        const result = await firstValueFrom(this.api.endImpersonation());
        if (!result.isSuccess) {
          auditErrorMessage = result.message || 'No fue posible registrar el cierre de suplantación.';
        }
      } catch {
        auditErrorMessage = 'No fue posible registrar el cierre de suplantación en backend.';
      }

      const restored = await this.auth.restoreOriginalSession();
      if (!restored) {
        await this.auth.logout();
        await this.router.navigateByUrl('/login');
        return;
      }

      if (auditErrorMessage) {
        this.notify.warning(auditErrorMessage);
      } else {
        this.notify.success('Volviste a tu sesión de administrador.');
      }

      await this.router.navigateByUrl('/admin/usuarios');
    } finally {
      this.isEndingImpersonation = false;
    }
  }

}
