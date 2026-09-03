import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { listCategories, listMerchants, listPurchases } from './reads'

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockResolvedValue({ ok: true, status: 200, json: async () => [] })
})

afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

function requestedPath() {
  return fetchMock.mock.calls[0][0] as string
}

describe('The read functions address the API', () => {
  it('asks for purchases within an occurrence range', async () => {
    await listPurchases({ from: '2026-09-01', to: '2026-09-30' })

    expect(requestedPath()).toBe('/api/purchases?from=2026-09-01&to=2026-09-30')
  })

  it('asks for the merchant dictionary', async () => {
    await listMerchants()

    expect(requestedPath()).toBe('/api/merchants')
  })

  it('asks for the category dictionary', async () => {
    await listCategories()

    expect(requestedPath()).toBe('/api/categories')
  })
})
