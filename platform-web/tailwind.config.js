/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        paper: '#FFFFFF',
        ink: '#151B2E',
        'ink-muted': '#6B7280',
        line: '#E3E6EF',
        signal: { DEFAULT: '#4361EE', dark: '#2D3FBF', light: '#7B92FF' },
        moss: '#2F7D4F',
        clay: '#B3402C',
        sidebar: '#0F1729',
        'sidebar-border': '#1D2C4D',
        'sidebar-muted': '#6B7590',
        'sidebar-text': '#E4E8F5',
      },
      fontFamily: {
        display: ['"Space Grotesk"', 'system-ui', 'sans-serif'],
        sans: ['"IBM Plex Sans"', 'system-ui', 'sans-serif'],
        mono: ['"IBM Plex Mono"', 'ui-monospace', 'monospace'],
      },
    },
  },
  plugins: [],
};
