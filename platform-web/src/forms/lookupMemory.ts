/**
 * A person's recent choices per field, kept on this device only (HANDOFF §6 bottom sheet and
 * §9.7: "Recent" = the last 3 choices of this field, per user, in localStorage; a Lookup with a
 * remembered last choice is pre-selected and says "Your last choice"). Storage can be blocked
 * (private mode), so every read and write tolerates failure.
 */
const key = (userId: string, fieldId: string) => `lookup-recent:${userId}:${fieldId}`;

export function recentChoices(userId: string | null | undefined, fieldId: string): string[] {
  if (!userId) return [];
  try {
    const parsed = JSON.parse(localStorage.getItem(key(userId, fieldId)) ?? '[]');
    return Array.isArray(parsed) ? parsed.filter((v): v is string => typeof v === 'string').slice(0, 3) : [];
  } catch {
    return [];
  }
}

export function rememberChoice(userId: string | null | undefined, fieldId: string, value: string) {
  if (!userId || !value) return;
  try {
    const next = [value, ...recentChoices(userId, fieldId).filter((v) => v !== value)].slice(0, 3);
    localStorage.setItem(key(userId, fieldId), JSON.stringify(next));
  } catch {
    // Not remembered - no harm done.
  }
}

/** The last choice, if any. */
export function lastChoice(userId: string | null | undefined, fieldId: string): string | null {
  return recentChoices(userId, fieldId)[0] ?? null;
}
