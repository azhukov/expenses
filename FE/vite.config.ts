import type { IncomingMessage, ServerResponse } from 'node:http'

import basicSsl from '@vitejs/plugin-basic-ssl'
import react from '@vitejs/plugin-react'
import { loadEnv, type Plugin } from 'vite'
import { defineConfig } from 'vitest/config'

import { LEDGER_ADDRESS_SETTING, parseLedgerAddress } from './src/api/ledgerAddress.ts'

// `FE_HTTPS=1` serves the client over HTTPS with a self-signed certificate. A phone reaches the dev
// server on a LAN address, which — unlike localhost — is not a secure context, and the camera is
// only offered to a secure one. Off by default so the end-to-end suite keeps its plain-HTTP origin.
const https = !!process.env.FE_HTTPS

/**
 * The client calls the API directly, at the address `API_URL` gives when this server starts — in
 * the shell, or in a git-ignored `.env.local`. It reaches the page as `/config.js`, not through
 * `import.meta.env`, because `vite preview` serves a bundle built earlier: an address compiled into
 * it would ignore the environment the preview server is started with (D1). Any other host of
 * `dist/` has to serve the same script from its own environment.
 *
 * A missing or malformed address stops the server here, rather than serving a client that fails
 * every request. There is no proxy: the API decides which origins may call it (CORS, D2).
 */
function ledgerAddress(): Plugin {
  let mode = 'development'
  let root = process.cwd()

  const serve = () => {
    const env = loadEnv(mode, root, '')
    const script = `window.__EXPENSES_CONFIG__ = ${JSON.stringify({
      apiUrl: parseLedgerAddress(
        process.env[LEDGER_ADDRESS_SETTING] ?? env[LEDGER_ADDRESS_SETTING],
      ),
    })}\n`

    return (request: IncomingMessage, response: ServerResponse, next: () => void) => {
      if (request.url?.split('?')[0] !== '/config.js') {
        next()
        return
      }

      response.setHeader('Content-Type', 'text/javascript; charset=utf-8')
      response.setHeader('Cache-Control', 'no-store')
      response.end(script)
    }
  }

  return {
    name: 'expenses-ledger-address',
    configResolved(config) {
      mode = config.mode
      root = config.root
    },
    configureServer(server) {
      server.middlewares.use(serve())
    },
    configurePreviewServer(server) {
      server.middlewares.use(serve())
    },
  }
}

export default defineConfig({
  plugins: [
    react(),
    ...(https ? [basicSsl()] : []),
    // Vitest starts a Vite server of its own; unit tests set the address in src/test/setup.ts.
    ...(process.env.VITEST ? [] : [ledgerAddress()]),
  ],
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
