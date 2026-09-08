import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

// The client always calls same-origin `/api/...` paths, in development as in a same-origin
// deployment. The API has no CORS configuration and this change adds none (D9), so the dev
// server proxies to it instead, stripping the prefix the API does not know about.
//
// `E2E_API_URL` retargets it for a run against a stack on other ports; the default is the port
// `dotnet run` and docker-compose both put the API on.
const api = {
  '/api': {
    target: process.env.E2E_API_URL ?? 'http://localhost:5082',
    changeOrigin: true,
    rewrite: (path: string) => path.replace(/^\/api/, ''),
  },
}

export default defineConfig({
  plugins: [react()],
  server: { proxy: api },
  // `vite preview` serves the built client, which is what the end-to-end suite drives. Without
  // the same proxy here, every /api call in that run would 404 against the static server.
  preview: { proxy: api },
  test: {
    // Colocated unit and component tests only. Without this, vitest's default glob would also
    // collect e2e/*.spec.ts and run a Playwright suite inside jsdom.
    include: ['src/**/*.test.{ts,tsx}'],
    environment: 'jsdom',
    // The layout requirements are CSS ones, so the stylesheets have to reach jsdom to be asserted on.
    css: true,
    globals: true,
    setupFiles: ['./src/test/setup.ts'],
  },
})
