import { Routes } from '@angular/router';
import { AppShellComponent } from './shared/layout/app-shell/app-shell.component';
import { RegisterPageComponent } from './features/register/register-page/register-page.component';
import { ExpensesPageComponent } from './features/expenses/expenses-page/expenses-page.component';
import { DashboardPageComponent } from './features/dashboard/dashboard-page/dashboard-page.component';
import { BudgetsPageComponent } from './features/budgets/budgets-page/budgets-page.component';
import { SettingsPageComponent } from './features/settings/settings-page/settings-page.component';

export const routes: Routes = [
	{
		path: '',
		component: AppShellComponent,
		children: [
			{ path: '', pathMatch: 'full', redirectTo: 'registrar' },
			{ path: 'registrar', component: RegisterPageComponent },
			{ path: 'gastos', component: ExpensesPageComponent },
			{ path: 'dashboard', component: DashboardPageComponent },
			{ path: 'presupuestos', component: BudgetsPageComponent },
			{ path: 'configuracion', component: SettingsPageComponent }
		]
	},
	{ path: '**', redirectTo: 'registrar' }
];
