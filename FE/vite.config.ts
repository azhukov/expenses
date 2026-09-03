import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The client always calls same-origin `/api/...` paths, in development as in a same-origin
// deployment. The API has no CORS configuration and this change adds none (D9), so the dev
// server proxies to it instead, stripping the prefix the API does not know about.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api': {
        target: 'http://localhost:5082',
        changeOrigin: true,
        rewrite: path => path.replace(/^\/api/, ''),
      },
    },
  },
  test: {
    environment: 'jsdom',
    // The layout requirements are CSS ones, so the stylesheets have to reach jsdom to be asserted on.
    css: true,
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
