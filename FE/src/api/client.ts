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

/** The dev proxy and a same-origin deployment both put the API here, so the paths never change (D9). */
const BASE = '/api'

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
  let response: Response

  try {
    response = await fetch(`${BASE}${path}`, {
      headers: { accept: 'application/json' },
      ...init,
    })
  } catch {
    throw new LedgerError('The ledger could not be reached.')
  }

  if (!response.ok) {
    const body = await response.json().catch(() => null)

    if (isErrorResponse(body)) {
      throw new LedgerError(body.message, {
        code: body.code,
        status: response.status,
        correlationId: body.correlationId ?? null,
      })
    }

    throw new LedgerError('The ledger could not be read.', { status: response.status })
  }

  return (await response.json()) as T
}
