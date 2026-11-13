import { useEffect, useRef } from 'react';

const CHECK_EVERY_MS = 5 * 60_000;
/** Pages where someone may be typing: never reload under them, wait for the next page change. */
const BUSY_PAGES = /^\/(forms|entries|builder)\//;
/** The build this tab last reloaded for - never reload twice for the same one (a cached page could otherwise loop). */
const RELOADED_FOR = 'asas-reloaded-for';

function reloadOnce(buildId: string) {
  try {
    if (sessionStorage.getItem(RELOADED_FOR) === buildId) return;
    sessionStorage.setItem(RELOADED_FOR, buildId);
  } catch {
    return; // storage blocked: no way to tell a loop apart, so don't reload at all
  }
  window.location.reload();
}

async function deployedBuildId(): Promise<string | null> {
  try {
    const res = await fetch('/version.json', { cache: 'no-store' });
    if (!res.ok) return null;
    const body = (await res.json()) as { buildId?: unknown };
    return typeof body.buildId === 'string' ? body.buildId : null;
  } catch {
    return null; // offline, or a dev server without the file
  }
}

/**
 * Keeps an open tab on the deployed version (owner, 2026-11-13: a phone tab left open across a
 * deploy kept showing the old page). Checks /version.json every few minutes and whenever the tab
 * comes back into view; once a newer build is out, the app reloads - straight away when it comes
 * back into view on a page nobody types into, otherwise on the next page change.
 */
export function useAppUpdate(pathname: string) {
  const stale = useRef<string | null>(null);
  const path = useRef(pathname);
  path.current = pathname;

  useEffect(() => {
    let cancelled = false;
    const check = async (fromVisibility: boolean) => {
      if (stale.current) return;
      const id = await deployedBuildId();
      if (cancelled || !id || id === __BUILD_ID__) return;
      stale.current = id;
      if (fromVisibility && !BUSY_PAGES.test(path.current)) reloadOnce(id);
    };
    const onVisible = () => {
      if (document.visibilityState === 'visible') void check(true);
    };
    const timer = window.setInterval(() => void check(false), CHECK_EVERY_MS);
    document.addEventListener('visibilitychange', onVisible);
    return () => {
      cancelled = true;
      window.clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    };
  }, []);

  // A page change on a stale tab loads the new version (the address already shows the new page).
  useEffect(() => {
    if (stale.current) reloadOnce(stale.current);
  }, [pathname]);
}
