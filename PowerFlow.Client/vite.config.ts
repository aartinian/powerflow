import { defineConfig } from 'vite';

// Build output is wired straight into the API project's wwwroot so a single
// `dotnet publish` packages the SPA. Dev server proxies API calls to the
// locally-running PowerFlow.Api (default Kestrel port 5000).
export default defineConfig({
  build: {
    outDir: '../PowerFlow.Api/wwwroot',
    emptyOutDir: true,
    sourcemap: true,
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:5000',
      '/healthz': 'http://localhost:5000',
    },
  },
});
