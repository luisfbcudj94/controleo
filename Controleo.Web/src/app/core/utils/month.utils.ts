export function monthKeyFromDate(date: Date): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  return `${year}-${month}`;
}

export function firstMonthOfAvailable(months: string[]): string {
  if (!months.length) {
    return monthKeyFromDate(new Date());
  }

  return months[months.length - 1];
}

export function monthLabel(monthKey: string): string {
  const [year, month] = monthKey.split('-').map(Number);
  const date = new Date(year, (month || 1) - 1, 1);
  return date.toLocaleDateString('es-CO', { month: 'long', year: 'numeric' });
}
