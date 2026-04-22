import { Injectable, signal, computed, NgZone } from '@angular/core';

@Injectable({ providedIn: 'root' })
export class PushNotificationService {
  private swRegistration: ServiceWorkerRegistration | null = null;

  private readonly activeSignal = signal(false);
  private readonly supportedSignal = signal(false);
  private readonly permissionSignal = signal<NotificationPermission>('default');

  readonly isActive = computed(() => this.activeSignal());
  readonly isSupported = computed(() => this.supportedSignal());
  readonly permission = computed(() => this.permissionSignal());

  constructor(private readonly zone: NgZone) {
    this.supportedSignal.set('Notification' in window && 'serviceWorker' in navigator);
    if (this.isSupported()) {
      this.permissionSignal.set(Notification.permission);
      this.listenToServiceWorker();
    }
  }

  async start(): Promise<void> {
    if (!this.isSupported()) return;

    const perm = await Notification.requestPermission();
    this.permissionSignal.set(perm);
    if (perm !== 'granted') return;

    await this.ensureServiceWorker();
    this.sendMessage({ type: 'START_NOTIFICATIONS' });
    this.activeSignal.set(true);
  }

  stop(): void {
    this.sendMessage({ type: 'STOP_NOTIFICATIONS' });
    this.activeSignal.set(false);
  }

  private listenToServiceWorker(): void {
    navigator.serviceWorker.addEventListener('message', (event) => {
      if (event.data?.type === 'NOTIF_STATE') {
        this.zone.run(() => this.activeSignal.set(event.data.active));
      }
    });
  }

  private async ensureServiceWorker(): Promise<void> {
    if (this.swRegistration) return;
    try {
      this.swRegistration = await navigator.serviceWorker.register('/sw-notifications.js');
      await navigator.serviceWorker.ready;
    } catch (err) {
      console.error('SW registration failed:', err);
    }
  }

  private sendMessage(msg: { type: string }): void {
    navigator.serviceWorker.controller?.postMessage(msg);
  }
}
