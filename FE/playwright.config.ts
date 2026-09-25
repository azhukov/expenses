import { defineConfig, devices } from '@playwright/test'

/**
 * The end-to-end gate. Unlike the vitest suites, nothing in the client or the ledger is mocked: the
 * browser drives the built client, which calls the real HTTP host cross-origin at `API_URL`, and
 * that host reads and writes a real PostgreSQL. Only the government fiscal portal is stood in for,
 * by a stub in the stack. The stack is `docker compose`'s (see docker-compose.e2e.yml); this
 * config only serves the client and points the browser at it.
 *
 * `E2E_BASE_URL` overrides the served client — set it when the app is already running (`./up.sh`)
 * and Playwright should attach rather than start its own server.
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://localhost:4173'

// The preview server would refuse to start without it anyway, but only after Playwright has waited
// out its webServer timeout for a server that was never going to answer.
if (!process.env.E2E_BASE_URL && !process.env.API_URL) {
  throw new Error(
    'API_URL is not set. The preview server this suite starts needs the API address, e.g. API_URL=http://localhost:5082.',
  )
}

export default defineConfig({
  testDir: './e2e',
  // A capture posts an image and waits out extraction, which the endpoint runs before answering.
  timeout: 60_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  // The suite writes to one shared ledger, so a second worker would see the first one's purchases.
  workers: 1,
  // Never in CI: a test that only passes on the second attempt is a test that found something.
  retries: 0,
  forbidOnly: !!process.env.CI,
  reporter: process.env.CI ? [['github'], ['html', { open: 'never' }]] : [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [
    {
      // The client is a phone-first capture screen (D3), so the viewport it is tested at is one.
      name: 'mobile-chrome',
      use: { ...devices['Pixel 7'] },
    },
  ],
  // Skipped when E2E_BASE_URL says something is already serving the client.
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        // The built client, not the dev server: what CI ships is what the browser should drive. It
        // inherits this environment, API_URL included, and serves that address as /config.js.
        command: 'npm run preview -- --port 4173 --strictPort',
        url: baseURL,
        reuseExistingServer: !process.env.CI,
        timeout: 120_000,
      },
})
