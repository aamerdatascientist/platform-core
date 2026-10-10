// The Executive Overview lives at its own address, outside the app's sidebar layout, so it
// can be opened full-window on a shared screen and by people who are not signed in.
//
// Everything a link needs rides in the address FRAGMENT (the part after "#"), which the
// browser keeps to itself: it is never sent to the web server, so the share key does not
// end up in access logs along the way. The page reads it and passes the key to the API in a
// request header instead.

export const DASHBOARD_PATH = '/dashboards/executive-overview';

export interface DashboardLinkOptions {
  /** Opens the dashboard without a sign-in. Null means "use the signed-in session". */
  shareKey: string | null;
  /** Language and colour mode to open in, so a shared link looks the way the sender saw it. */
  language?: string;
  mode?: string;
}

export function buildDashboardUrl({ shareKey, language, mode }: DashboardLinkOptions) {
  const params = new URLSearchParams();
  if (shareKey) params.set('k', shareKey);
  if (language) params.set('lang', language);
  if (mode) params.set('mode', mode);
  const fragment = params.toString();
  return `${window.location.origin}${DASHBOARD_PATH}${fragment ? `#${fragment}` : ''}`;
}

export function readDashboardLink(hash: string): DashboardLinkOptions {
  const params = new URLSearchParams(hash.startsWith('#') ? hash.slice(1) : hash);
  const language = params.get('lang');
  const mode = params.get('mode');
  return {
    shareKey: params.get('k') || null,
    language: language === 'ar' || language === 'en' ? language : undefined,
    mode: mode === 'dark' || mode === 'light' ? mode : undefined,
  };
}

export function isDashboardPath(pathname: string) {
  return pathname.replace(/\/+$/, '') === DASHBOARD_PATH;
}
