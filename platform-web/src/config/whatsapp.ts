import { useCallback, useEffect, useState } from 'react';

/**
 * WhatsApp isn't connected yet (owner, 2026-11-07): every WhatsApp control is shown as it will
 * be once it is - in Settings > Reminders, the user drawer, the assign panel and "Send now" on
 * the reports page - but pressing one only says "WhatsApp isn't set up yet". Set this to true
 * when the WhatsApp templates are approved and connected; the controls then do the real thing.
 */
export const WHATSAPP_READY = false;

/** Runs `action` when WhatsApp is ready; otherwise shows the "not set up yet" note for a few seconds. */
export function useWhatsAppGate() {
  const [notice, setNotice] = useState(false);
  useEffect(() => {
    if (!notice) return;
    const timer = window.setTimeout(() => setNotice(false), 5000);
    return () => window.clearTimeout(timer);
  }, [notice]);
  const guard = useCallback(<T,>(action: () => T): T | undefined => {
    if (WHATSAPP_READY) return action();
    setNotice(true);
    return undefined;
  }, []);
  return { notice, guard, ready: WHATSAPP_READY };
}
