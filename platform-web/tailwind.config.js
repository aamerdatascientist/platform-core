/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{ts,tsx}'],
  theme: {
    extend: {
      colors: {
        paper: '#F7F7F5',
        ink: '#1C1C1A',
        'ink-muted': '#6B6B66',
        line: '#E4E3DE',
        signal: { DEFAULT: '#F5A623', dark: '#B8860B' },
        moss: '#2F7D4F',
        clay: '#B3402C',
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
