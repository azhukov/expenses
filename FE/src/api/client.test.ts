import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError, read, send } from './client'

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

function respondWith(body: unknown, init: { ok: boolean; status: number }) {
  fetchMock.mockResolvedValue({
    ok: init.ok,
    status: init.status,
    json: async () => body,
  })
}

describe('Reading the ledger', () => {
  it('returns the decoded body of a successful read', async () => {
    respondWith([{ id: 1, name: 'Mercadona' }], { ok: true, status: 200 })

    await expect(read('/merchants')).resolves.toEqual([{ id: 1, name: 'Mercadona' }])
  })
})

describe('Requests go to the configured address', () => {
  it('sends a read to the address the serving server was started with', async () => {
    respondWith([], { ok: true, status: 200 })

    await read('/categories')

    expect(fetchMock).toHaveBeenCalledWith('http://ledger.test:9000/categories', expect.anything())
  })

  it('names the missing address rather than calling anywhere when none was served', async () => {
    const served = window.__EXPENSES_CONFIG__
    delete window.__EXPENSES_CONFIG__

    try {
      const failure = await read('/categories').catch((error: unknown) => error)

      expect(failure).toBeInstanceOf(LedgerError)
      expect((failure as LedgerError).message).toBe('The ledger address is not configured.')
      expect(fetchMock).not.toHaveBeenCalled()
    } finally {
      window.__EXPENSES_CONFIG__ = served
    }
  })
})

describe('An empty ledger is not a failure', () => {
  it('resolves an empty result as data', async () => {
    respondWith([], { ok: true, status: 200 })

    await expect(read('/purchases')).resolves.toEqual([])
  })
})

describe('The ledger reports a specific error', () => {
  it('surfaces the message the error response carries', async () => {
    respondWith(
      {
        code: 'purchase.not_found',
        message: 'No purchase with that identifier.',
        fields: {},
        correlationId: null,
      },
      { ok: false, status: 404 },
    )

    await expect(read('/purchases/9')).rejects.toThrow('No purchase with that identifier.')
  })

  it('keeps the ledger error code rather than replacing it', async () => {
    respondWith(
      { code: 'purchase.not_found', message: 'No purchase with that identifier.', fields: {} },
      { ok: false, status: 404 },
    )

    await expect(read('/purchases/9')).rejects.toMatchObject({ code: 'purchase.not_found' })
  })
})

describe('Ledger errors are still shown cross-origin', () => {
  it('asks for a response it can read, so a cross-origin error keeps its message', async () => {
    respondWith(
      { code: 'purchase.not_found', message: 'No purchase with that identifier.', fields: {} },
      { ok: false, status: 404 },
    )

    await expect(read('/purchases/9')).rejects.toThrow('No purchase with that identifier.')

    // An opaque `no-cors` response has no status or body, so every failure would read as the
    // ledger being unreachable; credentials would need a CORS policy the API deliberately lacks.
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(init.mode ?? 'cors').toBe('cors')
    expect(init.credentials ?? 'same-origin').not.toBe('include')
  })
})

describe('A failure with no error response', () => {
  it('reports a generic failure when the body cannot be read', async () => {
    fetchMock.mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new SyntaxError('Unexpected token')
      },
    })

    await expect(read('/purchases')).rejects.toThrow(/could not be read/i)
  })

  it('invents no error code for a transport failure', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    const failure = await read('/purchases').catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect((failure as LedgerError).code).toBeNull()
    expect((failure as LedgerError).message).toMatch(/could not be reached/i)
  })
})

describe('Writing to the ledger', () => {
  it('posts the body as JSON and decodes the response the way a read does', async () => {
    respondWith({ id: 7, amount: 12.5 }, { ok: true, status: 201 })

    await expect(send('/purchases', { amount: 12.5 })).resolves.toEqual({ id: 7, amount: 12.5 })

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('http://ledger.test:9000/purchases')
    expect(init.method).toBe('POST')
    expect(init.body).toBe(JSON.stringify({ amount: 12.5 }))
    expect(new Headers(init.headers).get('content-type')).toBe('application/json')
  })

  it('surfaces the message and code a rejected write carries', async () => {
    respondWith(
      {
        code: 'purchase.reconciliation_mismatch',
        message: 'The lines do not sum to the amount.',
        fields: {},
        correlationId: 'abc',
      },
      { ok: false, status: 400 },
    )

    const failure = await send('/purchases', {}).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect(failure).toMatchObject({
      message: 'The lines do not sum to the amount.',
      code: 'purchase.reconciliation_mismatch',
      status: 400,
      correlationId: 'abc',
    })
  })

  it('invents no error code when a write never reaches the ledger', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    const failure = await send('/purchases', {}).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect((failure as LedgerError).code).toBeNull()
    expect((failure as LedgerError).message).toMatch(/could not be reached/i)
  })
})
