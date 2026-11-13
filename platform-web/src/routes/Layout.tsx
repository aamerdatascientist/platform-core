import { useCallback, useEffect, useState } from 'react';
import { Outlet, useLocation } from 'react-router-dom';
import { CommandPalette, useCommandPaletteShortcut } from '../components/shell/CommandPalette';
import { FULL_SCREEN_ATTRIBUTE } from '../components/shell/fullScreen';
import { ShellProvider } from '../components/shell/ShellData';
import { OfflineBanner, QueueProvider } from '../components/shell/OfflineQueue';
import { SideMenu } from '../components/shell/SideMenu';
import { TabBar } from '../components/shell/TabBar';
import { useIsDesktop } from '../components/ui';
import { useAppUpdate } from '../components/AppUpdate';

interface LayoutProps {
  token: string;
}

/**
 * The app shell (HANDOFF §5): below 1024px the phone shell - content plus a bottom tab bar, no
 * side menu and no hamburger; from 1024px the desktop shell - a 264px side menu on the
 * inline-start side (right in Arabic) that scrolls with the page, plus a fluid content area.
 * The admin pages are back in the side menu (decision 1, 2026-11-06), and in Settings on a phone.
 */
export function Layout({ token }: LayoutProps) {
  return (
    <ShellProvider token={token}>
      <QueueProvider>
        <Shell />
      </QueueProvider>
    </ShellProvider>
  );
}

function Shell() {
  const desktop = useIsDesktop();
  const { pathname } = useLocation();
  useAppUpdate(pathname);
  const [paletteOpen, setPaletteOpen] = useState(false);
  const openPalette = useCallback(() => setPaletteOpen(true), []);
  useCommandPaletteShortcut(openPalette, desktop);
  // A page can ask for the tab bar to be hidden while it is shown (see FULL_SCREEN_ATTRIBUTE).
  const [fullScreen, setFullScreen] = useState(false);
  useEffect(() => {
    const read = () => setFullScreen(document.documentElement.hasAttribute(FULL_SCREEN_ATTRIBUTE));
    read();
    const observer = new MutationObserver(read);
    observer.observe(document.documentElement, { attributes: true, attributeFilter: [FULL_SCREEN_ATTRIBUTE] });
    return () => observer.disconnect();
  }, []);

  // A new page starts at the top.
  useEffect(() => {
    window.scrollTo(0, 0);
  }, [pathname]);

  if (desktop) {
    return (
      <div className="flex min-h-screen bg-bg">
        <SideMenu onSearch={openPalette} />
        <main className="min-w-0 flex-[999_1_560px] pb-8">
          <OfflineBanner />
          <div className="px-8 pt-6">
            <Outlet />
          </div>
        </main>
        <CommandPalette open={paletteOpen} onClose={() => setPaletteOpen(false)} />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-bg">
      <OfflineBanner />
      <main
        className="px-5 pt-5"
        style={{ paddingBottom: fullScreen ? undefined : 'calc(var(--tabbar-h) + env(safe-area-inset-bottom) + 20px)' }}
      >
        <Outlet />
      </main>
      {!fullScreen && <TabBar />}
    </div>
  );
}
