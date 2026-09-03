import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError, read } from './client'

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

  it('calls the API through a same-origin /api path', async () => {
    respondWith([], { ok: true, status: 200 })

    await read('/categories')

    expect(fetchMock).toHaveBeenCalledWith('/api/categories', expect.anything())
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
