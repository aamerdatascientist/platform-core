import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { NavLink, Outlet, useLocation } from 'react-router-dom';
import { api } from '../api/client';
import { getTokens, setTokens } from '../auth/tokenStore';
import { FormPicker } from '../components/FormPicker';
import { LanguageToggle } from '../components/LanguageToggle';
import { Logo } from '../components/Logo';
import { ModeToggle } from '../components/ModeToggle';
import { buildDashboardUrl } from '../dashboardLink';
import { getMode } from '../theme/mode';

interface LayoutProps {
  token: string;
}

export function Layout({ token }: LayoutProps) {
  const { t, i18n } = useTranslation();
  const [isAdmin, setIsAdmin] = useState(false);
  const [dashboardKey, setDashboardKey] = useState<string | null>(null);
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);
  const location = useLocation();

  useEffect(() => {
    api.auth
      .me(token)
      .then((me) => setIsAdmin(me.roles.includes('Administrator')))
      .catch(() => setIsAdmin(false));
  }, [token]);

  // Fetched up front, not on click: the dashboard opens in a new tab, and a browser only
  // allows that directly inside the click itself - waiting on a request first would get the
  // new tab blocked as a pop-up. Null (public viewing switched off on the server) is fine;
  // the tab then opens on the signed-in session instead.
  useEffect(() => {
    api.analytics
      .executiveOverviewShareKey(token)
      .then((result) => setDashboardKey(result.key))
      .catch(() => setDashboardKey(null));
  }, [token]);

  /**
   * Opens the Executive Overview in its own tab, as a full-window page. With a share key the
   * tab needs nothing from this one. Without it, the tab relies on being opened by this
   * window: a tab opened this way starts with a copy of this tab's sign-in, which is why
   * this is window.open and not a plain link (a plain new-tab link starts signed out).
   */
  function openDashboard() {
    setMobileMenuOpen(false);
    window.open(buildDashboardUrl({ shareKey: dashboardKey, language: i18n.language, mode: getMode() }), '_blank');
  }

  // Close the drawer automatically on navigation - otherwise picking a form from it on
  // mobile would leave the drawer covering the screen instead of showing the new page.
  useEffect(() => {
    setMobileMenuOpen(false);
  }, [location.pathname]);

  function handleSignOut() {
    const stored = getTokens();
    // Best-effort - don't block signing out locally on a network round-trip.
    if (stored) api.auth.logout(stored.refreshToken).catch(() => {});
    setTokens(null);
  }

  const navLinkClass = ({ isActive }: { isActive: boolean }) =>
    `block text-xs font-medium uppercase tracking-wide ${isActive ? 'text-sidebar-ink-strong' : 'text-sidebar-ink hover:text-sidebar-ink-strong'}`;

  return (
    // h-screen + overflow-hidden here, not min-h-screen, is what actually makes the
    // sidebar "stick" - the page itself never scrolls; each column below scrolls
    // independently within its own fixed-height box instead.
    <div className="flex h-screen overflow-hidden bg-bg">
      {/* Mobile-only top bar - just branding and a toggle, shown when the drawer is closed.
          relative + the rivet-strip as its last child replaces the old plain border-b -
          the rivet-strip IS the divider here, not an addition on top of one. */}
      <div className="fixed inset-x-0 top-0 z-20 flex items-center justify-between bg-sidebar px-3 py-3 lg:hidden">
        <div className="flex items-center gap-2">
          <Logo size="sm" />
          <span className="font-display text-sm font-semibold uppercase tracking-[0.07em] text-sidebar-ink-strong">ASAS</span>
        </div>
        <button
          onClick={() => setMobileMenuOpen(true)}
          className="px-2 text-xl leading-none text-sidebar-ink-strong"
          aria-label={t('sidebar.openMenu')}
        >
          ☰
        </button>
        <div className="rivet-strip absolute inset-x-0 bottom-0" />
      </div>

      {mobileMenuOpen && (
        <div className="fixed inset-0 z-30 bg-black/40 lg:hidden" onClick={() => setMobileMenuOpen(false)} />
      )}

      {/* Closed-state width collapse (max-lg:w-0 + max-lg:overflow-hidden) is what actually
          stops the page panning sideways on mobile: translating a fixed element off-canvas
          leaves its 256px box counted in the document's scrollable width on mobile
          WebKit/Blink, so the browser hands the user 256px of empty space to drag into.
          A zero-width box has nothing to miscount. index.css's html/body overflow-x is the
          backstop for anything else; this removes this particular cause.

          Three details that are load-bearing, not incidental:
          - max-lg: scoping. mobileMenuOpen is false on desktop too, so an unscoped w-0
            would collapse the static desktop sidebar, and an unscoped overflow-hidden
            would override its overflow-y-auto and kill its vertical scrolling.
          - w-0 replaces w-64 rather than sitting beside it. Both are plain utilities at
            equal specificity, so which one won would depend on Tailwind's emission order,
            not on the order they appear in this string.
          - px-0 goes with w-0. Tailwind sets box-sizing: border-box, so w-0 alone still
            leaves a 24px box here (px-3 padding floors it) - measured, not assumed. The
            padding has to collapse too for the box to actually reach zero.
          - the arbitrary transition delays *only* the width/padding changes by the slide
            Collapsing the width the instant the class flips would make the drawer vanish
            rather than slide out; this way transform animates for 200ms and the box
            collapses after it has finished leaving. Opening is unaffected - these classes
            are gone by then, so width snaps to 64 and the base transition-transform runs. */}
      <aside
        className={`fixed inset-y-0 start-0 z-40 flex shrink-0 flex-col overflow-y-auto bg-sidebar px-3 py-5 transition-transform duration-200 lg:static lg:z-auto lg:w-56 lg:translate-x-0 ${
          mobileMenuOpen
            ? 'w-64 translate-x-0'
            : 'w-64 -translate-x-full max-lg:w-0 max-lg:overflow-hidden max-lg:px-0 max-lg:[transition:transform_200ms_ease-in-out,width_0s_200ms,padding_0s_200ms] max-lg:rtl:translate-x-full'
        }`}
      >
        <div className="mb-3 flex items-center justify-between px-1">
          <div className="flex items-center gap-2">
            <Logo size="sm" />
            <span className="font-display text-sm font-semibold uppercase tracking-[0.07em] text-sidebar-ink-strong">ASAS</span>
          </div>
          <button
            onClick={handleSignOut}
            className="text-label uppercase tracking-wide text-sidebar-ink hover:text-sidebar-ink-strong"
          >
            {t('sidebar.signOut')}
          </button>
        </div>
        <div className="rivet-strip mb-4" />
        {/* dir="ltr" here, not just on each Switch individually - otherwise this row
            itself reorders under an inherited RTL context even though each toggle's
            own internal layout stays correct on its own. flex-wrap is a safety net,
            not the primary fix - a fixed physical position matters more than a tight
            one-line fit if the sidebar is ever narrower than both switches combined. */}
        <div dir="ltr" className="mb-4 flex flex-wrap items-center gap-x-3 gap-y-2 px-1">
          <LanguageToggle />
          <ModeToggle />
        </div>
        <FormPicker token={token} />

        <div className="mt-6 space-y-2 border-t border-border rounded pt-3">
          <button type="button" onClick={openDashboard} className={`${navLinkClass({ isActive: false })} w-full text-start`}>
            {t('sidebar.executiveOverview')} <span aria-hidden="true">↗</span>
          </button>
          {isAdmin && (
            <>
              <NavLink to="/builder" className={navLinkClass}>
                {t('sidebar.buildForms')}
              </NavLink>
              <NavLink to="/admin/users" className={navLinkClass}>
                {t('sidebar.users')}
              </NavLink>
            </>
          )}
        </div>
      </aside>

      {/* overflow-x-hidden / overscroll-x-none / touch-pan-y here, not just on html/body in
          index.css: THIS element, not body, is the app's real scrolling box (the root div
          two levels up is h-screen + overflow-hidden specifically so body/html never scroll
          at all - see that comment above). Every earlier attempt at this bug (overflow-x on
          html/body, overscroll-behavior-x, touch-action: pan-y) targeted a box that was
          already static and never scrolling in the first place, so none of it could have
          touched the actual symptom - confirmed by the original screen recording, which
          shows the horizontal drift correlated with vertical scroll position (drifts left
          while actively scrolling a long form, snaps back to 0 at rest), i.e. diagonal
          bleed on this container's own touch-scroll, not a document-level overflow or
          bounce. Same three properties, right element this time. */}
      <main className="flex-1 overflow-x-hidden overflow-y-auto overscroll-x-none touch-pan-y px-4 pb-6 pt-20 sm:px-6 sm:pb-8 lg:px-6 lg:py-8 lg:pt-8">
        <Outlet />
      </main>
    </div>
  );
}
