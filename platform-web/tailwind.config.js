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
