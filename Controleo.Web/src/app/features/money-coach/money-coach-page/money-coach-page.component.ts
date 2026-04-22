import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../../core/services/api.service';
import { ChatMessageItem } from '../../../core/models/api.models';

@Component({
  selector: 'app-money-coach-page',
  imports: [CommonModule, FormsModule],
  templateUrl: './money-coach-page.component.html',
  styleUrl: './money-coach-page.component.scss'
})
export class MoneyCoachPageComponent {
  messages: ChatMessageItem[] = [];
  suggestions: string[] = [];
  messageText = '';
  loading = false;
  sending = false;

  constructor(private readonly api: ApiService) {
    this.loadData();
  }

  loadData(): void {
    this.loading = true;
    this.api.getCoachHistory(30).subscribe({
      next: (data) => { this.messages = data.messages; this.loading = false; this.scrollToBottom(); },
      error: () => { this.loading = false; }
    });
    this.api.getCoachSuggestions().subscribe({
      next: (data) => this.suggestions = data.suggestions,
      error: () => {}
    });
  }

  sendMessage(content?: string): void {
    const text = (content || this.messageText).trim();
    if (!text || this.sending) return;
    this.messageText = '';
    this.messages.push({ role: 'user', content: text, createdAt: new Date().toISOString() });
    this.sending = true;
    this.scrollToBottom();
    this.api.sendCoachMessage(text).subscribe({
      next: (reply) => { this.messages.push(reply); this.sending = false; this.scrollToBottom(); },
      error: () => {
        this.messages.push({ role: 'assistant', content: 'Error de conexión. Intenta de nuevo.', createdAt: new Date().toISOString() });
        this.sending = false; this.scrollToBottom();
      }
    });
  }

  clearHistory(): void {
    if (!confirm('¿Eliminar toda la conversación?')) return;
    this.api.clearCoachHistory().subscribe({
      next: () => { this.messages = []; },
      error: () => {}
    });
  }

  isUser(msg: ChatMessageItem): boolean { return msg.role === 'user'; }

  private scrollToBottom(): void {
    setTimeout(() => {
      const el = document.querySelector('.chat-messages');
      if (el) el.scrollTop = el.scrollHeight;
    }, 50);
  }
}
