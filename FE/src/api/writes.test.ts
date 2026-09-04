import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from './client'
import { captureReceipt, recordPurchase } from './writes'

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
})

afterEach(() => {
  fetchMock.mockReset()
  vi.unstubAllGlobals()
})

function respondWith(body: unknown, init: { ok: boolean; status: number }) {
  fetchMock.mockResolvedValue({ ok: init.ok, status: init.status, json: async () => body })
}

const captured = {
  tempKey: '0f2b0a3c-0000-4000-8000-000000000001',
  state: 'Extracted',
  failureReason: null,
  result: null,
  validation: null,
  supplied: { ikof: null, jikr: null, issuerTaxNumber: null, createdAt: null, total: null },
  extracted: { ikof: null, jikr: null, issuerTaxNumber: null, createdAt: null, total: null },
  fiscalSource: 'None',
}

function image() {
  return new File(['bytes'], 'receipt.jpg', { type: 'image/jpeg' })
}

function sentForm() {
  const init = fetchMock.mock.calls[0][1] as RequestInit
  return init.body as FormData
}

describe('Capturing a receipt image', () => {
  it('posts the image as a file part to the capture endpoint', async () => {
    respondWith(captured, { ok: true, status: 200 })
    const file = image()

    await captureReceipt(file)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/receipts/capture')
    expect(init.method).toBe('POST')
    expect(sentForm().get('file')).toBe(file)
  })

  it('returns the capture result the ledger reported', async () => {
    respondWith(captured, { ok: true, status: 200 })

    await expect(captureReceipt(image())).resolves.toEqual(captured)
  })

  it('sends fiscal identifiers decoded at capture when there are any', async () => {
    respondWith(captured, { ok: true, status: 200 })

    await captureReceipt(image(), { ikof: 'abc', jikr: 'def' })

    expect(sentForm().get('fiscalIkof')).toBe('abc')
    expect(sentForm().get('fiscalJikr')).toBe('def')
  })

  it('sends no fiscal fields when none were decoded', async () => {
    respondWith(captured, { ok: true, status: 200 })

    await captureReceipt(image())

    expect(sentForm().has('fiscalIkof')).toBe(false)
    expect(sentForm().has('fiscalJikr')).toBe(false)
  })

  it('surfaces a rejected upload in the ledger error the rest of the client uses', async () => {
    respondWith(
      { code: 'receipt.image_too_large', message: 'A receipt image may be at most 15 MB.', fields: {} },
      { ok: false, status: 400 },
    )

    const failure = await captureReceipt(image()).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect(failure).toMatchObject({ code: 'receipt.image_too_large', status: 400 })
  })

  it('invents no error code when the upload never reaches the ledger', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    const failure = await captureReceipt(image()).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect((failure as LedgerError).code).toBeNull()
    expect((failure as LedgerError).message).toMatch(/could not be reached/i)
  })
})

const recorded = {
  id: 41,
  occurredAt: '2026-09-03T10:15:00',
  amount: 12.5,
  merchantId: null,
  merchantRaw: 'Mercadona',
  hasReceipt: true,
  expenses: [],
  totalSaving: 0,
  savingPercentage: null,
  alreadyRecorded: false,
  merchant: null,
  merchantNewlyAdded: false,
}

const command = {
  amount: 12.5,
  expenses: [{ description: 'Bread', amount: 12.5 }],
  merchant: { text: 'Mercadona' },
  occurredAt: '2026-09-03T10:15:00',
  capture: {
    tempKey: captured.tempKey,
    state: 'Extracted' as const,
    failureReason: null,
    fiscalExtractedSource: 'None' as const,
  },
}

describe('Recording a purchase', () => {
  it('posts the command as JSON to the purchases endpoint', async () => {
    respondWith(recorded, { ok: true, status: 201 })

    await recordPurchase(command)

    const [path, init] = fetchMock.mock.calls[0] as [string, RequestInit]
    expect(path).toBe('/api/purchases')
    expect(init.method).toBe('POST')
    expect(JSON.parse(init.body as string)).toEqual(command)
  })

  it('returns the created purchase', async () => {
    respondWith(recorded, { ok: true, status: 201 })

    await expect(recordPurchase(command)).resolves.toEqual(recorded)
  })

  it('returns a purchase that was already recorded rather than treating it as a failure', async () => {
    respondWith({ ...recorded, alreadyRecorded: true }, { ok: true, status: 200 })

    await expect(recordPurchase(command)).resolves.toMatchObject({ alreadyRecorded: true })
  })

  it('surfaces a rejected recording in the ledger error the rest of the client uses', async () => {
    respondWith(
      {
        code: 'purchase.reconciliation_mismatch',
        message: 'The lines do not sum to the amount.',
        fields: {},
      },
      { ok: false, status: 400 },
    )

    const failure = await recordPurchase(command).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(LedgerError)
    expect(failure).toMatchObject({
      code: 'purchase.reconciliation_mismatch',
      message: 'The lines do not sum to the amount.',
    })
  })
})
