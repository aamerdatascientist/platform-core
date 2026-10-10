/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  darkMode: ['class', '[data-mode="dark"]'],
  theme: {
    extend: {
      colors: {
        // Pre-"Structural steel" palette - some of these (ink, sidebar, border via the
        // old `line`) are superseded below by the token system, kept only for any
        // reference not yet swapped over; not otherwise removed.
        paper: '#FFFFFF',
        'ink-muted': '#6B7280',
        line: '#E3E6EF',
        signal: { DEFAULT: '#4361EE', dark: '#2D3FBF', light: '#7B92FF' },
        moss: '#2F7D4F',
        clay: '#B3402C',
        'sidebar-border': '#1D2C4D',
        'sidebar-muted': '#6B7590',
        'sidebar-text': '#E4E8F5',

        // "Structural steel" tokens - CSS custom properties defined in index.css,
        // switching by the `data-mode` attribute on <html> (see src/theme/mode.ts).
        // Deliberately reuses `sidebar`/`ink` as key names (overriding the hardcoded
        // hex values above in this same object) - this is the real app-wide rollout,
        // not a parallel/opt-in palette, so every existing `bg-sidebar`/`text-ink`
        // usage now resolves through the token system instead of a fixed hex.
        sidebar: 'var(--sidebar)',
        'sidebar-ink': 'var(--sidebar-ink)',
        'sidebar-ink-strong': 'var(--sidebar-ink-strong)',
        panel: 'var(--panel)',
        bg: 'var(--bg)',
        ink: 'var(--ink)',
        'ink-soft': 'var(--ink-soft)',
        accent: 'var(--accent)',
        'accent-ink': 'var(--accent-ink)',
        accent2: 'var(--accent2)',
        border: 'var(--border)',
        success: 'var(--success)',
        danger: 'var(--danger)',
      },
      // Tajawal for everything that is read, in both languages - one family that covers
      // Arabic and Latin, so the app looks the same on every device instead of falling back
      // to whatever Arabic font the device happens to have (chosen by Aamer 2026-10-10 over
      // the previous Calibri-bold body + Archivo headings, which read as heavy and blocky).
      // `display` is kept as its own key (headings) so a separate heading face can be
      // reintroduced later in one place. IBM Plex Mono stays for codes and tabular figures;
      // Tajawal follows it in the stack so Arabic inside a mono element still gets Tajawal.
      fontFamily: {
        display: ['Tajawal', 'system-ui', 'sans-serif'],
        sans: ['Tajawal', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'Tajawal', 'ui-monospace', 'monospace'],
      },
      // One type scale for the whole app, sized by the job the text does. Tajawal draws
      // smaller than Calibri at the same pixel size, and Arabic needs more room than
      // uppercase Latin, so every step is a little larger than Tailwind's default and the
      // two smallest roles have names instead of one-off pixel values.
      //   micro  - tags and tiny annotations          label - uppercase section labels, table heads
      //   xs     - captions and secondary text        sm    - body text, tables, inputs, buttons
      //   lg     - emphasised lines                   xl    - page titles
      //   2xl/3xl - headline figures (dashboard tiles)
      fontSize: {
        micro: ['0.75rem', { lineHeight: '1rem' }],
        label: ['0.78125rem', { lineHeight: '1.125rem' }],
        xs: ['0.8125rem', { lineHeight: '1.25rem' }],
        sm: ['0.9375rem', { lineHeight: '1.5rem' }],
        base: ['1rem', { lineHeight: '1.625rem' }],
        lg: ['1.125rem', { lineHeight: '1.75rem' }],
        xl: ['1.5rem', { lineHeight: '2rem' }],
        '2xl': ['1.75rem', { lineHeight: '2.25rem' }],
        '3xl': ['2.125rem', { lineHeight: '2.5rem' }],
      },
      borderRadius: {
        DEFAULT: '2px',
      },
      boxShadow: {
        recessed: 'var(--shadow-recessed)',
      },
      keyframes: {
        layerBuild: {
          '0%': { opacity: '0', transform: 'translateY(22px) scale(0.85)' },
          '28%': { opacity: '1', transform: 'translateY(0) scale(1)' },
          '72%': { opacity: '1', transform: 'translateY(0) scale(1)' },
          '100%': { opacity: '0', transform: 'translateY(-10px) scale(0.92)' },
        },
      },
      animation: {
        'layer-build': 'layerBuild 1.8s cubic-bezier(0.4,0,0.2,1) infinite',
      },
    },
  },
  plugins: [],
};
