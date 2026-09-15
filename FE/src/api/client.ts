import type { ErrorResponse } from './types'

/**
 * A failure reading the ledger, carrying the ledger's own words where it gave any. The client
 * deliberately has no error vocabulary of its own: replacing a specific message with a generic one
 * is how a user ends up told "something went wrong" about a problem the API had already named.
 */
export class LedgerError extends Error {
  /** The API's error code, or null where the failure never reached the API. */
  readonly code: string | null
  readonly status: number | null
  readonly correlationId: string | null

  constructor(
    message: string,
    options: { code?: string | null; status?: number | null; correlationId?: string | null } = {},
  ) {
    super(message)
    this.name = 'LedgerError'
    this.code = options.code ?? null
    this.status = options.status ?? null
    this.correlationId = options.correlationId ?? null
  }
}

declare global {
  interface Window {
    /** Set by `/config.js`, which the server serving the client writes from its own environment. */
    __EXPENSES_CONFIG__?: { apiUrl: string }
  }
}

/**
 * The absolute URL of one ledger path, under the address the serving server was started with. Read
 * on every call rather than once at import, so nothing holds an address the page was not given.
 * Absent only where a host served the bundle without `/config.js`, and that is named rather than
 * left to fail as a request to nowhere.
 */
export function ledgerUrl(path: string): string {
  const address = window.__EXPENSES_CONFIG__?.apiUrl

  if (!address) {
    throw new LedgerError('The ledger address is not configured.')
  }

  return `${address}${path}`
}

function isErrorResponse(body: unknown): body is ErrorResponse {
  return (
    typeof body === 'object' &&
    body !== null &&
    typeof (body as ErrorResponse).message === 'string' &&
    typeof (body as ErrorResponse).code === 'string'
  )
}

/**
 * One typed GET. Anything richer than this would be abstracting over a second backend that does
 * not exist.
 */
export async function read<T>(path: string, init?: RequestInit): Promise<T> {
  const url = ledgerUrl(path)
  let response: Response

  try {
    response = await fetch(url, {
      headers: { accept: 'application/json' },
      ...init,
    })
  } catch {
    throw new LedgerError('The ledger could not be reached.')
  }

  if (!response.ok) {
    throw await failureOf(response)
  }

  return (await response.json()) as T
}

/**
 * The failure side of a response, in the ledger's own words where it gave any. Shared by every
 * call that talks to the API, so that a write never grows an error vocabulary a read does not have.
 */
export async function failureOf(response: Response): Promise<LedgerError> {
  const body: unknown = await response.json().catch(() => null)

  if (isErrorResponse(body)) {
    return new LedgerError(body.message, {
      code: body.code,
      status: response.status,
      correlationId: body.correlationId ?? null,
    })
  }

  return new LedgerError('The ledger could not be read.', { status: response.status })
}

/**
 * One typed JSON POST, the write counterpart of {@link read}. Multipart uploads do not come
 * through here: a body that is not JSON is a different call, not a parameter of this one.
 */
export async function send<T>(path: string, body: unknown, init?: RequestInit): Promise<T> {
  const url = ledgerUrl(path)
  let response: Response

  try {
    response = await fetch(url, {
      method: 'POST',
      headers: { accept: 'application/json', 'content-type': 'application/json' },
      body: JSON.stringify(body),
      ...init,
    })
  } catch {
    throw new LedgerError('The ledger could not be reached.')
  }

  if (!response.ok) {
    throw await failureOf(response)
  }

  return (await response.json()) as T
}
