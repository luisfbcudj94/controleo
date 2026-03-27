import { CommonModule } from '@angular/common';
import { Component, EventEmitter, forwardRef, Input, Output } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'app-custom-date-input',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './custom-date-input.component.html',
  styleUrl: './custom-date-input.component.scss',
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => CustomDateInputComponent),
      multi: true
    }
  ]
})
export class CustomDateInputComponent implements ControlValueAccessor {
  @Input() placeholder = 'Seleccionar fecha';
  @Input() disabled = false;
  @Input() min?: string;
  @Input() max?: string;
  @Output() dateChange = new EventEmitter<string>();

  calendarOpen = false;
  viewYear = 0;
  viewMonth = 0;
  readonly weekdays = ['L', 'M', 'X', 'J', 'V', 'S', 'D'];

  private selectedDate: Date | null = null;
  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  get displayValue(): string {
    if (!this.selectedDate) {
      return this.placeholder;
    }

    const dd = `${this.selectedDate.getDate()}`.padStart(2, '0');
    const mm = `${this.selectedDate.getMonth() + 1}`.padStart(2, '0');
    const yyyy = this.selectedDate.getFullYear();
    return `${dd}/${mm}/${yyyy}`;
  }

  get hasValue(): boolean {
    return this.selectedDate !== null;
  }

  get monthTitle(): string {
    if (!this.viewYear && this.viewMonth === 0) {
      return '';
    }

    return new Date(this.viewYear, this.viewMonth, 1)
      .toLocaleDateString('es-CO', { month: 'long', year: 'numeric' });
  }

  get calendarCells(): Array<number | null> {
    if (!this.viewYear && this.viewMonth === 0) {
      return [];
    }

    const firstDay = new Date(this.viewYear, this.viewMonth, 1);
    const leading = (firstDay.getDay() + 6) % 7;
    const daysInMonth = new Date(this.viewYear, this.viewMonth + 1, 0).getDate();

    const cells: Array<number | null> = [];
    for (let index = 0; index < leading; index += 1) {
      cells.push(null);
    }

    for (let day = 1; day <= daysInMonth; day += 1) {
      cells.push(day);
    }

    return cells;
  }

  get canGoPreviousMonth(): boolean {
    const minDate = this.min ? this.fromInputDate(this.min) : null;
    if (!minDate) {
      return true;
    }

    return this.viewYear > minDate.getFullYear()
      || (this.viewYear === minDate.getFullYear() && this.viewMonth > minDate.getMonth());
  }

  get canGoNextMonth(): boolean {
    const maxDate = this.max ? this.fromInputDate(this.max) : null;
    if (!maxDate) {
      return true;
    }

    return this.viewYear < maxDate.getFullYear()
      || (this.viewYear === maxDate.getFullYear() && this.viewMonth < maxDate.getMonth());
  }

  writeValue(value: string | null): void {
    if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
      this.selectedDate = null;
      return;
    }

    const parsed = this.fromInputDate(value);
    this.selectedDate = parsed && this.isWithinRange(parsed) ? parsed : null;
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled = isDisabled;
  }

  openDateSelector(): void {
    if (this.disabled) {
      return;
    }

    this.onTouched();

    const base = this.selectedDate ?? this.safeDefaultDate();
    this.viewYear = base.getFullYear();
    this.viewMonth = base.getMonth();
    this.calendarOpen = true;
  }

  clearValue(event: MouseEvent): void {
    event.stopPropagation();
    if (this.disabled) {
      return;
    }

    this.selectedDate = null;
    this.emitValue();
  }

  closeCalendar(): void {
    this.calendarOpen = false;
  }

  onBackdropClick(event: MouseEvent): void {
    if (event.target === event.currentTarget) {
      this.closeCalendar();
    }
  }

  previousMonth(): void {
    if (!this.canGoPreviousMonth) {
      return;
    }

    if (this.viewMonth === 0) {
      this.viewMonth = 11;
      this.viewYear -= 1;
      return;
    }

    this.viewMonth -= 1;
  }

  nextMonth(): void {
    if (!this.canGoNextMonth) {
      return;
    }

    if (this.viewMonth === 11) {
      this.viewMonth = 0;
      this.viewYear += 1;
      return;
    }

    this.viewMonth += 1;
  }

  selectDay(day: number): void {
    if (this.isDayDisabled(day)) {
      return;
    }

    const selected = new Date(this.viewYear, this.viewMonth, day);
    this.selectedDate = selected;
    this.emitValue();
    this.closeCalendar();
  }

  isDaySelected(day: number): boolean {
    if (!this.selectedDate) {
      return false;
    }

    return this.selectedDate.getFullYear() === this.viewYear
      && this.selectedDate.getMonth() === this.viewMonth
      && this.selectedDate.getDate() === day;
  }

  isDayDisabled(day: number): boolean {
    const target = new Date(this.viewYear, this.viewMonth, day);
    return !this.isWithinRange(target);
  }

  private safeDefaultDate(): Date {
    const now = new Date();
    return this.clampDate(new Date(now.getFullYear(), now.getMonth(), now.getDate()));
  }

  private emitValue(): void {
    const value = this.selectedDate ? this.toInputDate(this.selectedDate) : '';
    this.onChange(value);
    this.dateChange.emit(value);
  }

  private toInputDate(date: Date): string {
    const yyyy = date.getFullYear();
    const mm = `${date.getMonth() + 1}`.padStart(2, '0');
    const dd = `${date.getDate()}`.padStart(2, '0');
    return `${yyyy}-${mm}-${dd}`;
  }

  private fromInputDate(value: string): Date | null {
    const [year, month, day] = value.split('-').map(Number);
    if (!year || !month || !day) {
      return null;
    }

    const parsed = new Date(year, month - 1, day);
    if (Number.isNaN(parsed.getTime())) {
      return null;
    }

    return new Date(parsed.getFullYear(), parsed.getMonth(), parsed.getDate());
  }

  private clampDate(date: Date): Date {
    const minDate = this.min ? this.fromInputDate(this.min) : null;
    const maxDate = this.max ? this.fromInputDate(this.max) : null;

    if (minDate && date < minDate) {
      return new Date(minDate);
    }

    if (maxDate && date > maxDate) {
      return new Date(maxDate);
    }

    return date;
  }

  private isWithinRange(date: Date): boolean {
    const minDate = this.min ? this.fromInputDate(this.min) : null;
    const maxDate = this.max ? this.fromInputDate(this.max) : null;

    if (minDate && date < minDate) {
      return false;
    }

    if (maxDate && date > maxDate) {
      return false;
    }

    return true;
  }
}