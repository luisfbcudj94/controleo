import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

@Component({
  selector: 'app-selector-modal',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './selector-modal.component.html',
  styleUrl: './selector-modal.component.scss'
})
export class SelectorModalComponent {
  @Input() isOpen = false;
  @Input() title = 'Seleccionar';
  @Input() options: string[] = [];
  @Input() selectedValue = '';
  @Input() emptyText = 'No hay opciones.';

  @Output() choose = new EventEmitter<string>();
  @Output() close = new EventEmitter<void>();

  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close.emit();
    }
  }

  trackByValue(_: number, item: string): string {
    return item;
  }
}
