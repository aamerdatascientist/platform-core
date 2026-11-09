import { useCallback, useEffect, useState } from 'react';

/**
 * WhatsApp is live (owner, 2026-11-07: Meta approved the templates form_assigned, form_reminder
 * and stakeholder_report). Messages go to the number on each person's account, so there is
 * nothing to "connect": Settings and the user drawer show where messages go. Set this back to
 * false to grey WhatsApp out again - every control then only says "WhatsApp isn't set up yet".
 */
export const WHATSAPP_READY = true;

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
