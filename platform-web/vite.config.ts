import { defineConfig, type Plugin } from 'vite';
import react from '@vitejs/plugin-react';

/**
 * One id per build, baked into the app and written to /version.json. An open tab compares the
 * two now and then and loads the new version on its next page change (components/AppUpdate.ts) -
 * a tab left open across a deploy otherwise keeps running the old code (owner, 2026-11-13).
 */
const buildId = new Date().toISOString();

function versionFile(): Plugin {
  return {
    name: 'asas-version-file',
    apply: 'build',
    generateBundle() {
      this.emitFile({ type: 'asset', fileName: 'version.json', source: JSON.stringify({ buildId }) });
    },
  };
}

export default defineConfig({
  plugins: [react(), versionFile()],
  define: { __BUILD_ID__: JSON.stringify(buildId) },
  server: {
    port: 5173,
  },
});
