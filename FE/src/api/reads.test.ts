import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  countPurchases,
  exportPurchases,
  listCategories,
  listMerchants,
  listPurchases,
  listUnits,
} from './reads'

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

    expect(requestedPath()).toBe('http://ledger.test:9000/purchases?from=2026-09-01&to=2026-09-30')
  })

  it('asks for the merchant dictionary', async () => {
    await listMerchants()

    expect(requestedPath()).toBe('http://ledger.test:9000/merchants')
  })

  it('asks for the category dictionary', async () => {
    await listCategories()

    expect(requestedPath()).toBe('http://ledger.test:9000/categories')
  })

  it('asks for the unit dictionary', async () => {
    await expect(listUnits()).resolves.toEqual([])

    expect(requestedPath()).toBe('http://ledger.test:9000/units')
  })

  it('asks how many purchases the ledger holds, across every date', async () => {
    fetchMock.mockResolvedValue({ ok: true, status: 200, json: async () => ({ count: 12 }) })

    await expect(countPurchases()).resolves.toBe(12)

    expect(requestedPath()).toBe('http://ledger.test:9000/purchases/count')
  })
})

describe('The export addresses the API', () => {
  const csv = new Blob(['purchase_id'], { type: 'text/csv' })

  beforeEach(() => {
    fetchMock.mockResolvedValue({ ok: true, status: 200, blob: async () => csv })
  })

  it('asks for everything when given no range, as CSV', async () => {
    await expect(exportPurchases()).resolves.toBe(csv)

    expect(requestedPath()).toBe('http://ledger.test:9000/purchases/export')
    expect(fetchMock.mock.calls[0][1]).toMatchObject({ headers: { accept: 'text/csv' } })
  })

  it('asks for an inclusive range', async () => {
    await exportPurchases({ from: '2026-10-05', to: '2026-10-12' })

    expect(requestedPath()).toBe(
      'http://ledger.test:9000/purchases/export?from=2026-10-05&to=2026-10-12',
    )
  })
})
