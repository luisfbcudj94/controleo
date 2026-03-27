import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';
import { AuthSessionResponse, LoginRequest, RegisterRequest } from '../models/api.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private static readonly storageKey = 'controleo.auth.session';

  private readonly baseUrl = environment.apiBaseUrl;

  private initialized = false;
  private initializePromise: Promise<void> | null = null;
  private session: AuthSessionResponse | null = null;

  constructor(private readonly http: HttpClient) {}

  async initialize(): Promise<void> {
    if (this.initialized) {
      return;
    }

    if (this.initializePromise) {
      return this.initializePromise;
    }

    this.initializePromise = (async () => {
      const persisted = this.readPersistedSession();
      this.session = persisted && !this.isSessionExpired(persisted)
        ? persisted
        : null;

      if (!this.session) {
        this.clearPersistedSession();
      }

      this.initialized = true;
    })();

    return this.initializePromise;
  }

  async login(request: LoginRequest): Promise<void> {
    await this.initialize();

    const session = await firstValueFrom(
      this.http.post<AuthSessionResponse>(`${this.baseUrl}/api/auth/login`, request)
    );

    this.setSession(session);
  }

  async register(request: RegisterRequest): Promise<void> {
    await this.initialize();

    const session = await firstValueFrom(
      this.http.post<AuthSessionResponse>(`${this.baseUrl}/api/auth/register`, request)
    );

    this.setSession(session);
  }

  async logout(): Promise<void> {
    await this.initialize();
    this.session = null;
    this.clearPersistedSession();
  }

  get isAuthenticated(): boolean {
    return !!this.session && !this.isSessionExpired(this.session);
  }

  get currentUserName(): string {
    return this.session?.user.name || this.session?.user.email || 'Controleo';
  }

  get currentUserEmail(): string {
    return this.session?.user.email || 'sin-correo';
  }

  get userInitials(): string {
    const source = this.currentUserName.trim();
    if (!source) {
      return 'CT';
    }

    const parts = source.split(/\s+/).slice(0, 2);
    return parts.map((part) => part[0].toUpperCase()).join('');
  }

  async getAccessToken(): Promise<string | null> {
    await this.initialize();

    if (!this.session || this.isSessionExpired(this.session)) {
      this.session = null;
      this.clearPersistedSession();
      return null;
    }

    return this.session.accessToken;
  }

  private setSession(session: AuthSessionResponse): void {
    this.session = session;
    localStorage.setItem(AuthService.storageKey, JSON.stringify(session));
  }

  private readPersistedSession(): AuthSessionResponse | null {
    const raw = localStorage.getItem(AuthService.storageKey);
    if (!raw) {
      return null;
    }

    try {
      return JSON.parse(raw) as AuthSessionResponse;
    } catch {
      return null;
    }
  }

  private clearPersistedSession(): void {
    localStorage.removeItem(AuthService.storageKey);
  }

  private isSessionExpired(session: AuthSessionResponse): boolean {
    const expiresAt = Date.parse(session.expiresAt);
    if (Number.isNaN(expiresAt)) {
      return true;
    }

    return Date.now() >= expiresAt;
  }
}
