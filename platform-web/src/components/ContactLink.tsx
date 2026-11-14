import type { MouseEvent, ReactNode } from 'react';
import { Link } from 'react-router-dom';

export type ContactField = 'whatsapp' | 'telegram';
/** Where the admin came from, for the way back after saving (admin flows F3-4). */
export type ContactOrigin = 'messages' | 'assignments' | 'users';

/** `/admin/users?edit={id}&focus={field}&from={page}` - opens that user's editor on the missing field (F3-2). */
export function userContactUrl(userId: string, focus: ContactField, from: ContactOrigin) {
  return `/admin/users?${new URLSearchParams({ edit: userId, focus, from })}`;
}

/**
 * A missing-contact warning that is also a link to fix it (admin flows F3-1): same text, colour and
 * size as the warning it replaces (pass its classes), underlined on hover and keyboard focus, with
 * an accessible name like "Add WhatsApp number for Hazim". It never toggles the row or checkbox it
 * sits in. `onNavigate` lets a page with unsaved changes ask first (F3-3).
 */
export function ContactLink({
  to,
  label,
  className = '',
  onNavigate,
  children,
}: {
  to: string;
  label: string;
  className?: string;
  onNavigate?: (to: string) => void;
  children: ReactNode;
}) {
  const click = (e: MouseEvent) => {
    e.stopPropagation();
    if (onNavigate) {
      e.preventDefault();
      onNavigate(to);
    }
  };
  return (
    <Link
      to={to}
      aria-label={label}
      title={label}
      onClick={click}
      className={`rounded-[4px] decoration-1 underline-offset-2 hover:underline focus-visible:underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${className}`}
    >
      {children}
    </Link>
  );
}
