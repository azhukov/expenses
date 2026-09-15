/** The environment variable the serving server reads the ledger address from. */
export const LEDGER_ADDRESS_SETTING = 'API_URL'

/**
 * Where the ledger is, as the server that serves the client was told it. Read by `vite.config.ts`
 * when a dev or preview server starts, so this module must stay free of anything browser-only.
 *
 * Refusing here, at startup, is the point: a client served with no usable address would load and
 * then fail every request with a message that says nothing about why.
 *
 * The trailing slash is trimmed so that every path, which starts with one, joins to it cleanly.
 */
export function parseLedgerAddress(value: string | undefined): string {
  const trimmed = value?.trim() ?? ''

  if (trimmed === '') {
    throw new Error(
      `${LEDGER_ADDRESS_SETTING} is not set. Set it to the API's address, e.g. ${LEDGER_ADDRESS_SETTING}=http://localhost:5082.`,
    )
  }

  let url: URL

  try {
    url = new URL(trimmed)
  } catch {
    throw refused(trimmed)
  }

  if (url.protocol !== 'http:' && url.protocol !== 'https:') {
    throw refused(trimmed)
  }

  return trimmed.replace(/\/+$/, '')
}

function refused(value: string) {
  return new Error(
    `${LEDGER_ADDRESS_SETTING}='${value}' is not an absolute http or https URL, e.g. http://localhost:5082.`,
  )
}
