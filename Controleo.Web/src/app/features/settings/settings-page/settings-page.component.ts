import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { ApiService } from '../../../core/services/api.service';

@Component({
  selector: 'app-settings-page',
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './settings-page.component.html',
  styleUrl: './settings-page.component.scss'
})
export class SettingsPageComponent {
  isLoading = false;
  isSaving = false;
  statusMessage = '';

  readonly form;

  constructor(
    private readonly fb: FormBuilder,
    private readonly api: ApiService
  ) {
    this.form = this.fb.nonNullable.group({
      movementTypesText: ['', Validators.required],
      paymentMethodsText: ['', Validators.required]
    });

    this.loadCatalogs();
  }

  save(): void {
    if (this.form.invalid || this.isSaving) {
      this.statusMessage = 'Completa ambas listas antes de guardar.';
      return;
    }

    const value = this.form.getRawValue();
    const movementTypes = this.parseLines(value.movementTypesText);
    const paymentMethods = this.parseLines(value.paymentMethodsText);

    if (!movementTypes.length || !paymentMethods.length) {
      this.statusMessage = 'Cada catálogo debe tener al menos un valor válido.';
      return;
    }

    this.isSaving = true;
    this.statusMessage = 'Guardando catálogos...';

    this.api
      .updateCatalogs({ movementTypes, paymentMethods })
      .pipe(finalize(() => (this.isSaving = false)))
      .subscribe({
        next: (result) => {
          this.statusMessage = result.message;
          this.form.patchValue({
            movementTypesText: movementTypes.join('\n'),
            paymentMethodsText: paymentMethods.join('\n')
          });
        },
        error: () => {
          this.statusMessage = 'No fue posible actualizar los catálogos.';
        }
      });
  }

  private loadCatalogs(): void {
    this.isLoading = true;
    this.api
      .getCatalogs()
      .pipe(finalize(() => (this.isLoading = false)))
      .subscribe({
        next: (catalogs) => {
          this.form.patchValue({
            movementTypesText: catalogs.movementTypes.join('\n'),
            paymentMethodsText: catalogs.paymentMethods.join('\n')
          });
        },
        error: () => {
          this.statusMessage = 'No fue posible cargar los catálogos.';
        }
      });
  }

  private parseLines(value: string): string[] {
    return Array.from(
      new Set(
        value
          .split(/[\n,;]/g)
          .map((item) => item.trim())
          .filter((item) => item.length > 0)
      )
    );
  }

}
