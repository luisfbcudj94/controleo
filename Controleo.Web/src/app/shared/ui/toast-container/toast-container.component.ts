import { CommonModule } from '@angular/common';
import { Component } from '@angular/core';
import { NotificationService, NotificationToast } from '../../../core/services/notification.service';

@Component({
  selector: 'app-toast-container',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './toast-container.component.html',
  styleUrl: './toast-container.component.scss'
})
export class ToastContainerComponent {
  constructor(readonly notifications: NotificationService) {}

  dismiss(id: number): void {
    this.notifications.dismiss(id);
  }

  trackById(_: number, toast: NotificationToast): number {
    return toast.id;
  }
}
