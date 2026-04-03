import { CommonModule } from '@angular/common';
import { Component, EventEmitter, Input, Output } from '@angular/core';

export interface SelectorOptionItem {
  value: string;
  label: string;
  icon?: string;
  color?: string;
  textColor?: string;
  description?: string;
}

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
  @Input() optionItems: SelectorOptionItem[] = [];
  @Input() selectedValue = '';
  @Input() emptyText = 'No hay opciones.';
  @Input() mode: 'list' | 'tiles' = 'list';
  @Input() closeVariant: 'text' | 'icon' = 'icon';
  @Input() showAddTile = false;
  @Input() addTileLabel = 'Agregar';
  @Input() addTileIcon = '+';
  @Input() addTileColor = '#EFF3F1';

  @Output() choose = new EventEmitter<string>();
  @Output() add = new EventEmitter<void>();
  @Output() close = new EventEmitter<void>();

  get renderedOptions(): SelectorOptionItem[] {
    if (this.optionItems.length) {
      return this.optionItems;
    }

    return this.options.map((value) => ({ value, label: value }));
  }

  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.close.emit();
    }
  }

  trackByValue(_: number, item: SelectorOptionItem): string {
    return item.value;
  }

  isSelected(item: SelectorOptionItem): boolean {
    return item.value === this.selectedValue;
  }

  select(value: string): void {
    this.choose.emit(value);
  }
}
