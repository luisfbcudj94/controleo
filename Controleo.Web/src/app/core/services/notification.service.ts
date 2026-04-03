import { Injectable, computed, signal } from '@angular/core';

export type NotificationType = 'success' | 'error' | 'warning' | 'info';

export interface NotificationToast {
  id: number;
  type: NotificationType;
  title: string;
  message: string;
}

interface NotificationOptions {
  title?: string;
  durationMs?: number;
}

@Injectable({ providedIn: 'root' })
export class NotificationService {
  private readonly toastsSignal = signal<NotificationToast[]>([]);
  readonly toasts = computed(() => this.toastsSignal());

  private readonly timeoutHandles = new Map<number, ReturnType<typeof setTimeout>>();
  private readonly defaultDurationMs = 4200;
  private nextId = 1;

  success(message: string, title = 'Listo', durationMs?: number): void {
    this.push('success', message, { title, durationMs });
  }

  error(message: string, title = 'No se pudo completar', durationMs?: number): void {
    this.push('error', message, { title, durationMs });
  }

  warning(message: string, title = 'Revisa este dato', durationMs?: number): void {
    this.push('warning', message, { title, durationMs });
  }

  info(message: string, title = 'Dato importante', durationMs?: number): void {
    this.push('info', message, { title, durationMs });
  }

  dismiss(id: number): void {
    const handle = this.timeoutHandles.get(id);
    if (handle) {
      clearTimeout(handle);
      this.timeoutHandles.delete(id);
    }

    this.toastsSignal.update((current) => current.filter((toast) => toast.id !== id));
  }

  clear(): void {
    this.timeoutHandles.forEach((handle) => clearTimeout(handle));
    this.timeoutHandles.clear();
    this.toastsSignal.set([]);
  }

  private push(type: NotificationType, message: string, options: NotificationOptions = {}): void {
    const content = message.trim();
    if (!content) {
      return;
    }

    const id = this.nextId;
    this.nextId += 1;

    const toast: NotificationToast = {
      id,
      type,
      title: options.title?.trim() || this.defaultTitleFor(type),
      message: content
    };

    this.toastsSignal.update((current) => [...current, toast]);

    const duration = options.durationMs ?? this.defaultDurationMs;
    if (duration > 0) {
      const handle = setTimeout(() => this.dismiss(id), duration);
      this.timeoutHandles.set(id, handle);
    }
  }

  private defaultTitleFor(type: NotificationType): string {
    if (type === 'success') {
      return 'Listo';
    }

    if (type === 'error') {
      return 'No se pudo completar';
    }

    if (type === 'warning') {
      return 'Revisa este dato';
    }

    return 'Dato importante';
  }
}
