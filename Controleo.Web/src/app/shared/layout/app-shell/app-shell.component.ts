import { Component } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';

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

  readonly analysisNav = [
    { path: '/presupuestos', icon: '🎯', label: 'Presupuestos' },
    { path: '/configuracion', icon: '⚙️', label: 'Configuración' }
  ];

  currentMonthDate = new Date();
  isGirlMode = false;

  constructor(private readonly router: Router) {
    this.initializeTheme();
  }

  get pageTitle(): string {
    return this.getPageMeta().title;
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

    if (routePath.startsWith('/configuracion')) {
      return { title: 'Configuración', crumb: 'Catálogos y preferencias' };
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

}
