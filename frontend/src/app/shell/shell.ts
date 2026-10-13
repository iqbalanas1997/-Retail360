import { Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter, map, startWith } from 'rxjs';

import { SessionService } from '../core/session.service';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './shell.html',
})
export class Shell {
  private readonly router = inject(Router);
  protected readonly session = inject(SessionService);
  protected readonly title = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      startWith(null),
      map(() => routeTitle(this.router)),
    ),
    { initialValue: 'Retail360' },
  );

  protected signOut(): void {
    this.session.clear();
    void this.router.navigateByUrl('/login');
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
