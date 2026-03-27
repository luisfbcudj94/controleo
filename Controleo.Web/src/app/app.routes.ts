import { Routes } from '@angular/router';
import { AppShellComponent } from './shared/layout/app-shell/app-shell.component';
import { RegisterPageComponent } from './features/register/register-page/register-page.component';
import { ExpensesPageComponent } from './features/expenses/expenses-page/expenses-page.component';
import { DashboardPageComponent } from './features/dashboard/dashboard-page/dashboard-page.component';
import { BudgetsPageComponent } from './features/budgets/budgets-page/budgets-page.component';
import { SettingsPageComponent } from './features/settings/settings-page/settings-page.component';
import { LoginPageComponent } from './features/auth/login-page/login-page.component';
import { RecurringPageComponent } from './features/recurring/recurring-page/recurring-page.component';
import { authChildGuard } from './core/guards/auth.guard';

export const routes: Routes = [
	{ path: 'login', component: LoginPageComponent },
	{
		path: '',
		component: AppShellComponent,
		canActivateChild: [authChildGuard],
		children: [
			{ path: '', pathMatch: 'full', redirectTo: 'registrar' },
			{ path: 'registrar', component: RegisterPageComponent },
			{ path: 'gastos', component: ExpensesPageComponent },
			{ path: 'dashboard', component: DashboardPageComponent },
			{ path: 'presupuestos', component: BudgetsPageComponent },
			{ path: 'recurrentes', component: RecurringPageComponent },
			{ path: 'configuracion', component: SettingsPageComponent }
		]
	},
	{ path: '**', redirectTo: 'login' }
];
