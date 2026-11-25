import { Component, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';

import { SessionService } from '../core/session.service';

export const NAV_COLLAPSED_KEY = 'retail360.shell.navCollapsed';
export const NAV_GROUPS_KEY = 'retail360.shell.navGroups';

export type NavGroupId = 'daily' | 'catalog' | 'people' | 'management';

const DEFAULT_NAV_GROUPS: Record<NavGroupId, boolean> = {
  daily: true,
  catalog: true,
  people: true,
  management: true,
};

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);
  protected readonly navOpen = signal(false);
  protected readonly navCollapsed = signal(readNavCollapsed());
  protected readonly navGroups = signal(readNavGroups());
  private readonly leaveWhenSignedOut = effect(() => {
    if (!this.session.hasSession()) {
      void this.router.navigateByUrl('/login');
    }
  });

  protected readonly title = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => routeTitle(this.router)),
    ),
    { initialValue: 'Retail360' },
  );

  private readonly currentUrl = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => this.router.url),
    ),
    { initialValue: this.router.url },
  );

  private readonly onNavigate = effect(() => {
    const url = this.currentUrl();
    this.navOpen.set(false);
    const group = groupForUrl(url);
    if (group) {
      untracked(() => this.ensureGroupOpen(group));
    }
  });

  protected toggleNav(): void {
    this.navOpen.update((open) => !open);
  }

  protected closeNav(): void {
    this.navOpen.set(false);
  }

  protected toggleNavCollapsed(): void {
    this.navCollapsed.update((collapsed) => {
      const next = !collapsed;
      writeNavCollapsed(next);
      return next;
    });
  }

  protected groupOpen(id: NavGroupId): boolean {
    return this.navGroups()[id] !== false;
  }

  protected linksVisible(id: NavGroupId): boolean {
    return this.navCollapsed() || this.groupOpen(id);
  }

  protected toggleGroup(id: NavGroupId): void {
    this.navGroups.update((groups) => {
      const next = { ...groups, [id]: !this.groupOpen(id) };
      writeNavGroups(next);
      return next;
    });
  }

  protected signOut(): void {
    this.session.clear();
    void this.router.navigateByUrl('/login');
  }

  private ensureGroupOpen(id: NavGroupId): void {
    if (this.groupOpen(id)) {
      return;
    }

    this.navGroups.update((groups) => {
      const next = { ...groups, [id]: true };
      writeNavGroups(next);
      return next;
    });
  }
}

function routeTitle(router: Router): string {
  let route = router.routerState.snapshot.root;
  let title = 'Retail360';
  while (route.firstChild) {
    route = route.firstChild;
    const next = route.data['title'];
    if (typeof next === 'string' && next.length > 0) {
      title = next;
    }
  }

  return title;
}

export function groupForUrl(url: string): NavGroupId | null {
  const path = url.split('?')[0] ?? url;
  if (
    path === '/pos' ||
    path === '/sales' ||
    path.startsWith('/sales/') ||
    path === '/purchases' ||
    path.startsWith('/purchases/') ||
    path === '/expenses' ||
    path.startsWith('/expenses/')
  ) {
    return 'daily';
  }

  if (path.startsWith('/catalog/') || path.startsWith('/inventory/')) {
    return 'catalog';
  }

  if (path === '/suppliers' || path.startsWith('/suppliers/') || path === '/customers' || path.startsWith('/customers/')) {
    return 'people';
  }

  if (path === '/dashboard' || path === '/users' || path.startsWith('/users/') || path === '/audit') {
    return 'management';
  }

  return null;
}

function readNavCollapsed(): boolean {
  try {
    return localStorage.getItem(NAV_COLLAPSED_KEY) === '1';
  } catch {
    return false;
  }
}

function writeNavCollapsed(collapsed: boolean): void {
  try {
    localStorage.setItem(NAV_COLLAPSED_KEY, collapsed ? '1' : '0');
  } catch {
    // Preference is optional when storage is unavailable.
  }
}

function readNavGroups(): Record<NavGroupId, boolean> {
  try {
    const raw = localStorage.getItem(NAV_GROUPS_KEY);
    if (!raw) {
      return { ...DEFAULT_NAV_GROUPS };
    }

    const parsed = JSON.parse(raw) as Partial<Record<NavGroupId, boolean>>;
    return {
      daily: parsed.daily !== false,
      catalog: parsed.catalog !== false,
      people: parsed.people !== false,
      management: parsed.management !== false,
    };
  } catch {
    return { ...DEFAULT_NAV_GROUPS };
  }
}

function writeNavGroups(groups: Record<NavGroupId, boolean>): void {
  try {
    localStorage.setItem(NAV_GROUPS_KEY, JSON.stringify(groups));
  } catch {
    // Preference is optional when storage is unavailable.
  }
}
