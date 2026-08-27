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
      fontFamily: {
        display: ['Archivo', 'system-ui', 'sans-serif'],
        sans: ['"IBM Plex Sans"', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'ui-monospace', 'monospace'],
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
