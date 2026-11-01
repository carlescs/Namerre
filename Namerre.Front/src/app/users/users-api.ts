import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import type { LinkedLogin, UserRole } from '../auth/auth';

/** A local user as administrators see it (`/api/users`). */
export interface UserView {
  id: number;
  username: string;
  role: UserRole;
  disabled: boolean;
  hasPassword: boolean;
  /** The break-glass administrator from the server configuration; read-only. */
  managedByConfiguration: boolean;
  createdAt: string | null;
  lastSignInAt: string | null;
  logins: LinkedLogin[];
}

/**
 * What happens to the user an identity is moved away from: `keep` it, `delete` it, or `merge` it into the
 * target (all its identities and its run history move too, then it is deleted).
 */
export type MoveSource = 'keep' | 'delete' | 'merge';

export interface CreateUser {
  username: string;
  role: UserRole;
  password: string | null;
  /** The `sub` claim of a single sign-on identity to link right away. */
  subject: string | null;
  displayName: string | null;
}

/** HTTP client for the administrator-only user management endpoints. */
@Injectable({ providedIn: 'root' })
export class UsersApi {
  private readonly http = inject(HttpClient);

  list() {
    return this.http.get<UserView[]>('/api/users');
  }

  create(user: CreateUser) {
    return this.http.post<UserView>('/api/users', user);
  }

  update(id: number, change: { role?: UserRole; disabled?: boolean }) {
    return this.http.patch<UserView>(`/api/users/${id}`, change);
  }

  setPassword(id: number, password: string) {
    return this.http.put<UserView>(`/api/users/${id}/password`, { password });
  }

  removePassword(id: number) {
    return this.http.delete<UserView>(`/api/users/${id}/password`);
  }

  link(id: number, subject: string, displayName: string | null) {
    return this.http.post<UserView>(`/api/users/${id}/logins`, { subject, displayName });
  }

  /** Moves an identity linked to another user here, keeping or deleting the user it leaves. */
  moveLogin(id: number, loginId: number, deleteSource: boolean) {
    return this.http.post<UserView>(`/api/users/${id}/logins`, { loginId, deleteSource });
  }

  /** Merges another user into this one: its identities and run history move here, then it is deleted. */
  merge(id: number, sourceUserId: number) {
    return this.http.post<UserView>(`/api/users/${id}/merge`, { sourceUserId });
  }

  unlink(id: number, loginId: number) {
    return this.http.delete<UserView>(`/api/users/${id}/logins/${loginId}`);
  }

  delete(id: number) {
    return this.http.delete<void>(`/api/users/${id}`);
  }
}
