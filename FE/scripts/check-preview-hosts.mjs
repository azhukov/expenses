// Scenarios from browser-client: "The preview server answers the host names it is served under" —
// "A listed public host is served" and "An unlisted host is refused". Unit tests cover how the list
// is read; this proves vite.config.ts actually hands it to the preview server.
//
// Serves the existing dist/ (run `npm run build` first) with `vite preview` on a spare port, then
// asks for it under the listed Railway host and under one that is not listed. `fetch` may not set a
// Host header, so the requests go through node:http. Exits non-zero on the first mismatch.
//
//   node scripts/check-preview-hosts.mjs

import { spawn } from 'node:child_process'
import { existsSync } from 'node:fs'
import { request } from 'node:http'
import { fileURLToPath } from 'node:url'

const root = fileURLToPath(new URL('..', import.meta.url))
const port = 4199
const listed = 'expenses-frontend-production-2e52.up.railway.app'
const ledger = 'http://ledger.test:9000'

if (!existsSync(new URL('../dist/index.html', import.meta.url))) {
  console.error('dist/ has no build. Run `npm run build` first.')
  process.exit(1)
}

const server = spawn(
  process.execPath,
  [
    fileURLToPath(new URL('../node_modules/vite/bin/vite.js', import.meta.url)),
    'preview',
    '--port',
    String(port),
    '--strictPort',
  ],
  {
    cwd: root,
    env: { ...process.env, API_URL: ledger, PREVIEW_ALLOWED_HOSTS: listed },
    stdio: ['ignore', 'pipe', 'pipe'],
  },
)

let output = ''
server.stdout.on('data', chunk => (output += chunk))
server.stderr.on('data', chunk => (output += chunk))

function get(path, host) {
  return new Promise((resolve, reject) => {
    const req = request({ host: '127.0.0.1', port, path, headers: { Host: host } }, response => {
      let body = ''
      response.on('data', chunk => (body += chunk))
      response.on('end', () => resolve({ status: response.statusCode, body }))
    })
    req.on('error', reject)
    req.end()
  })
}

async function waitForServer() {
  for (let attempt = 0; attempt < 100; attempt++) {
    if (server.exitCode !== null) {
      throw new Error(`vite preview exited early:\n${output}`)
    }

    try {
      return await get('/', 'localhost')
    } catch {
      await new Promise(resolve => setTimeout(resolve, 200))
    }
  }

  throw new Error(`vite preview did not answer on port ${port}:\n${output}`)
}

const failures = []

function expect(label, condition) {
  console.log(`${condition ? 'ok  ' : 'FAIL'} ${label}`)
  if (!condition) failures.push(label)
}

try {
  await waitForServer()

  const page = await get('/', listed)
  expect(`${listed} / is served (got ${page.status})`, page.status === 200)

  const config = await get('/config.js', listed)
  expect(
    `${listed} /config.js carries the ledger address (got ${config.status})`,
    config.status === 200 && config.body.includes(ledger),
  )

  const refused = await get('/', 'elsewhere.test')
  expect(`elsewhere.test / is refused (got ${refused.status})`, refused.status === 403)
} catch (error) {
  failures.push(String(error))
  console.error(error)
} finally {
  server.kill()
}

process.exit(failures.length === 0 ? 0 : 1)
