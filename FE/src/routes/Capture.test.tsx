import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode } from 'react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from '../api/client'
import { Capture } from './Capture'

/**
 * The writes are mocked rather than the transport: what these scenarios are about is the screen's
 * behaviour around the calls, and mocking `fetch` would only restate `writes.test.ts`.
 */
vi.mock('../api/writes', () => ({
  captureReceipt: vi.fn(),
  captureFiscal: vi.fn(),
  recordPurchase: vi.fn(),
}))

/**
 * The scanner is replaced by one the test drives: jsdom has no camera, and what is specified here
 * is the screen around a read, not the reading (D40).
 */
vi.mock('../capture/scanner', () => ({ startScanning: vi.fn() }))

vi.mock('../api/queries', async importActual => ({
  ...(await importActual<typeof import('../api/queries')>()),
  useCategories: () => ({ data: [{ id: 3, code: 'groceries', name: 'Groceries' }] }),
  useUnits: () => ({ data: [{ id: 5, code: 'kg', name: 'Kilogram', symbol: 'kg' }] }),
}))

const { captureFiscal, captureReceipt, recordPurchase } = await import('../api/writes')
const { startScanning } = await import('../capture/scanner')

const capture = vi.mocked(captureReceipt)
const fiscal = vi.mocked(captureFiscal)
const record = vi.mocked(recordPurchase)
const scanning = vi.mocked(startScanning)

const payload =
  'https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636&tin=02365928'

/** Every scanner the screen started, with what it reads into and whether it was stopped. */
let scans: { read: (payload: string) => void; stop: ReturnType<typeof vi.fn> }[] = []

/** A rear camera streaming, whose reads the test delivers by hand. */
function liveCamera() {
  scanning.mockImplementation((_video, onRead) => {
    const scan = { read: onRead, stop: vi.fn() }
    scans.push(scan)
    return Promise.resolve({ stop: scan.stop })
  })
}

/** No live camera — unsupported, refused or absent all look the same to the screen. */
function noLiveCamera() {
  scanning.mockResolvedValue(null)
}

/** A capture whose extraction never settles, so the waiting state can be observed. */
function neverSettles() {
  capture.mockReturnValue(new Promise(() => {}))
}

function image(name = 'receipt.jpg') {
  return new File(['bytes'], name, { type: 'image/jpeg' })
}

function photographControl() {
  return screen.getByLabelText(/photograph the receipt/i)
}

function renderAt(options: { strict?: boolean } = {}) {
  const router = createMemoryRouter(
    [
      { path: '/capture', element: <Capture /> },
      { path: '/', element: <p>Home</p> },
    ],
    { initialEntries: ['/capture'] },
  )

  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const tree = (
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )

  return { client, router, ...render(options.strict ? <StrictMode>{tree}</StrictMode> : tree) }
}

/** Arrives on the capture screen with no live camera and takes a photograph through its control. */
async function photographed(file: File) {
  const rendered = renderAt()
  await userEvent.upload(await screen.findByLabelText(/photograph the receipt/i), file)

  return rendered
}

beforeEach(() => {
  scans = []
  capture.mockReset()
  fiscal.mockReset()
  record.mockReset()
  scanning.mockReset()
  noLiveCamera()
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('The fiscal code is read before a photograph is asked for', () => {
  it('scans the rear camera as soon as the screen is reached', async () => {
    liveCamera()

    renderAt()

    await waitFor(() => expect(scanning).toHaveBeenCalledTimes(1))
    expect(scanning.mock.calls[0][0]).toBeInstanceOf(HTMLVideoElement)
  })

  it('submits the first code read without a further tap, and stops scanning', async () => {
    liveCamera()
    fiscal.mockReturnValue(new Promise(() => {}))
    renderAt()
    await waitFor(() => expect(scans).toHaveLength(1))

    act(() => scans[0].read(payload))

    await waitFor(() => expect(fiscal).toHaveBeenCalledWith(payload))
    expect(await screen.findByRole('status')).toHaveTextContent(/fetching the invoice/i)
    expect(scans[0].stop).toHaveBeenCalled()
  })

  it('offers a photograph throughout scanning, as a real camera input', async () => {
    liveCamera()
    renderAt()
    await waitFor(() => expect(scans).toHaveLength(1))

    const control = photographControl()

    // The finger lands on the input itself, so iOS opens the camera for that tap (D3).
    expect(control).toHaveAttribute('type', 'file')
    expect(control).toHaveAttribute('accept', 'image/*')
    expect(control).toHaveAttribute('capture', 'environment')
    expect(control.closest('label')).not.toBeNull()
  })

  it('stops scanning when the user chooses a photograph', async () => {
    liveCamera()
    neverSettles()
    renderAt()
    await waitFor(() => expect(scans).toHaveLength(1))

    const file = image()
    await userEvent.upload(photographControl(), file)

    expect(scans[0].stop).toHaveBeenCalled()
    await waitFor(() => expect(capture).toHaveBeenCalledWith(file, undefined))
  })

  it('offers the photograph control without an error when there is no live camera', async () => {
    noLiveCamera()

    renderAt()

    expect(await screen.findByLabelText(/photograph the receipt/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('releases the camera and submits nothing when the screen is left during scanning', async () => {
    liveCamera()
    const { unmount } = renderAt()
    await waitFor(() => expect(scans).toHaveLength(1))

    unmount()

    expect(scans[0].stop).toHaveBeenCalled()
    expect(fiscal).not.toHaveBeenCalled()
    expect(capture).not.toHaveBeenCalled()
  })

  it('keeps one scanner live and posts one payload under StrictMode', async () => {
    liveCamera()
    fiscal.mockReturnValue(new Promise(() => {}))
    renderAt({ strict: true })
    await waitFor(() => expect(scans.length).toBeGreaterThan(0))

    // StrictMode mounts, unmounts and mounts again; every scanner but the last is released.
    const live = scans.filter(scan => scan.stop.mock.calls.length === 0)
    expect(live).toHaveLength(1)

    act(() => live[0].read(payload))

    await waitFor(() => expect(fiscal).toHaveBeenCalledTimes(1))
  })

  it('suggests a photograph when nothing has been read for a while', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true })
    try {
      liveCamera()
      renderAt()
      await waitFor(() => expect(scans).toHaveLength(1))
      expect(screen.queryByText(/try a photograph/i)).not.toBeInTheDocument()

      await act(() => vi.advanceTimersByTimeAsync(10_000))

      expect(screen.getByText(/try a photograph/i)).toBeInTheDocument()
    } finally {
      vi.useRealTimers()
    }
  })
})

describe('A photograph is taken only when the code produces no invoice', () => {
  async function scanned(outcome: () => Promise<unknown>) {
    liveCamera()
    fiscal.mockImplementation(outcome as never)
    renderAt()
    await waitFor(() => expect(scans).toHaveLength(1))
    act(() => scans[0].read(payload))
  }

  it('presents a fetched invoice for review and asks for no photograph', async () => {
    await scanned(() => Promise.resolve(extracted({ tempKey: null, fiscalPayload: payload })))

    expect(await screen.findByRole('button', { name: /confirm/i })).toBeInTheDocument()
    expect(screen.queryByLabelText(/photograph the receipt/i)).not.toBeInTheDocument()
  })

  it('asks for a photograph, and carries the payload with it, when the invoice could not be fetched', async () => {
    await scanned(() =>
      Promise.resolve(
        failed({
          tempKey: null,
          supplied: { ...noFiscal, ikof: '32AA324CFF5030271E16D59F7F8EF636' },
        }),
      ),
    )

    expect(await screen.findByText(/could not fetch the invoice/i)).toBeInTheDocument()

    neverSettles()
    const file = image()
    await userEvent.upload(photographControl(), file)

    await waitFor(() => expect(capture).toHaveBeenCalledWith(file, payload))
  })

  it('asks for a photograph, without the payload, when the code was not a fiscal code', async () => {
    await scanned(() => Promise.resolve(failed({ tempKey: null })))

    neverSettles()
    const file = image()
    await userEvent.upload(await screen.findByLabelText(/photograph the receipt/i), file)

    await waitFor(() => expect(capture).toHaveBeenCalledWith(file, undefined))
  })

  it('offers to retry the same payload, or a photograph, when the ledger cannot be reached', async () => {
    await scanned(() => Promise.reject(new LedgerError('The ledger could not be reached.')))

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
    expect(photographControl()).toBeInTheDocument()

    fiscal.mockReturnValue(new Promise(() => {}))
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    await waitFor(() => expect(fiscal).toHaveBeenCalledTimes(2))
    expect(fiscal.mock.calls[1][0]).toBe(payload)
  })
})

describe('A captured image is uploaded on arrival', () => {
  it('submits the photograph to the capture endpoint without a further tap', async () => {
    neverSettles()
    const file = image()

    await photographed(file)

    await waitFor(() => expect(capture).toHaveBeenCalledTimes(1))
    expect(capture.mock.calls[0][0]).toBe(file)
  })

  it('indicates that extraction is running while the request is outstanding', async () => {
    neverSettles()

    await photographed(image())

    expect(await screen.findByRole('status')).toHaveTextContent(/reading the receipt/i)
  })

  it('shows no candidate lines, amount or date while the request is outstanding', async () => {
    neverSettles()

    await photographed(image())

    await screen.findByRole('status')
    expect(screen.queryByRole('textbox', { name: /description/i })).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/total amount/i)).not.toBeInTheDocument()
    expect(screen.queryByLabelText(/date/i)).not.toBeInTheDocument()
  })
})

describe('The upload cannot be reached', () => {
  it('reports that the receipt could not be uploaded', async () => {
    capture.mockRejectedValue(new LedgerError('The ledger could not be reached.'))

    await photographed(image())

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
  })

  it('retries the same file rather than losing it', async () => {
    capture.mockRejectedValue(new LedgerError('The ledger could not be reached.'))
    const file = image()

    await photographed(file)
    await screen.findByRole('alert')

    capture.mockReturnValue(new Promise(() => {}))
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    await waitFor(() => expect(capture).toHaveBeenCalledTimes(2))
    expect(capture.mock.calls[1][0]).toBe(file)
  })
})

describe('The same image is never uploaded twice for one visit', () => {
  it('uploads once even when the screen renders again', async () => {
    neverSettles()
    const file = image()

    const { rerender } = await photographed(file)
    await waitFor(() => expect(capture).toHaveBeenCalledTimes(1))

    rerender(<p>re-rendered</p>)

    expect(capture).toHaveBeenCalledTimes(1)
  })
})

const noFiscal = { ikof: null, jikr: null, issuerTaxNumber: null, createdAt: null, total: null }

function candidate(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    lineNumber: 1,
    description: 'Bread',
    amount: 2.5,
    quantity: 1,
    unitPrice: 2.5,
    listUnitPrice: null,
    discountAmount: null,
    taxRatePercent: null,
    categoryRaw: 'Pan',
    unitRaw: 'ud',
    categoryId: 3,
    unitId: 5,
    provenance: {},
    reportedConfidence: {},
    ...overrides,
  }
}

function extracted(overrides: Record<string, unknown> = {}) {
  return {
    tempKey: '0f2b0a3c-0000-4000-8000-000000000001',
    state: 'Extracted',
    failureReason: null,
    result: {
      engineName: 'test',
      engineVersion: '1',
      stepsRun: ['vision'],
      candidates: [candidate(), candidate({ lineNumber: 2, description: 'Milk', amount: 1.5 })],
      total: 4,
      taxRatePercent: null,
      taxAmount: null,
      merchantName: 'Mercadona',
      merchantTaxId: null,
      provenance: {},
      reportedConfidence: {},
    },
    validation: { checks: [] },
    supplied: noFiscal,
    extracted: { ...noFiscal, createdAt: '2026-09-01T10:15:00' },
    fiscalSource: 'DecodedFromCode',
    fiscalPayload: 'https://mapr.tax.gov.me/ic/#/verify?iic=abc&crtd=2026-09-01T10:15:00',
    alreadyRecorded: null,
    ...overrides,
  }
}

async function reviewOf(result: Record<string, unknown> = extracted()) {
  capture.mockResolvedValue(result as never)
  await photographed(image())

  return await screen.findByRole('button', { name: /confirm/i })
}

function lines() {
  return screen.getAllByTestId('line')
}

describe('A successful capture is presented for review', () => {
  it('shows the candidate lines the extraction proposed', async () => {
    await reviewOf()

    expect(lines()).toHaveLength(2)
    expect(within(lines()[0]).getByLabelText(/description/i)).toHaveValue('Bread')
    expect(within(lines()[1]).getByLabelText(/description/i)).toHaveValue('Milk')
  })

  it('shows the amount, merchant and occurred-at date', async () => {
    await reviewOf()

    expect(screen.getByLabelText(/total amount/i)).toHaveValue(4)
    expect(screen.getByLabelText(/merchant/i)).toHaveValue('Mercadona')
    expect(screen.getByLabelText(/date/i)).toHaveValue('2026-09-01T10:15')
  })

  it('presents a capture needing review the same way as an extracted one', async () => {
    await reviewOf(extracted({ state: 'NeedsReview' }))

    expect(lines()).toHaveLength(2)
  })

  it('lets the description, amount and quantity of a line be changed', async () => {
    await reviewOf()
    const line = lines()[0]

    await userEvent.clear(within(line).getByLabelText(/description/i))
    await userEvent.type(within(line).getByLabelText(/description/i), 'Rye bread')
    await userEvent.clear(within(line).getByLabelText(/^amount$/i))
    await userEvent.type(within(line).getByLabelText(/^amount$/i), '3.25')
    await userEvent.clear(within(line).getByLabelText(/quantity/i))
    await userEvent.type(within(line).getByLabelText(/quantity/i), '2')

    expect(within(line).getByLabelText(/description/i)).toHaveValue('Rye bread')
    expect(within(line).getByLabelText(/^amount$/i)).toHaveValue(3.25)
    expect(within(line).getByLabelText(/quantity/i)).toHaveValue(2)
  })

  it('lets the category of a line be chosen from the dictionary', async () => {
    await reviewOf()
    const category = within(lines()[0]).getByLabelText(/category/i)

    await userEvent.selectOptions(category, 'groceries')

    expect(category).toHaveValue('groceries')
  })

  it('adds a line', async () => {
    await reviewOf()

    await userEvent.click(screen.getByRole('button', { name: /add a line/i }))

    expect(lines()).toHaveLength(3)
    expect(within(lines()[2]).getByLabelText(/description/i)).toHaveValue('')
  })

  it('removes a line', async () => {
    await reviewOf()

    await userEvent.click(within(lines()[0]).getByRole('button', { name: /remove/i }))

    expect(lines()).toHaveLength(1)
    expect(within(lines()[0]).getByLabelText(/description/i)).toHaveValue('Milk')
  })
})

describe('The capture result is not re-requested', () => {
  it('confirms with the temporary key and outcome the original response carried', async () => {
    record.mockResolvedValue({ id: 9 } as never)
    const confirm = await reviewOf(
      extracted({
        state: 'NeedsReview',
        supplied: noFiscal,
        extracted: { ...noFiscal, ikof: 'extracted-ikof', jikr: 'extracted-jikr' },
        fiscalSource: 'DecodedFromCode',
        fiscalPayload:
          'https://mapr.tax.gov.me/ic/#/verify?iic=extracted-ikof&crtd=2026-09-01T10:15:00',
      }),
    )

    // This capture decoded no creation timestamp, so the date is entered: the screen no longer
    // submits without one ("The review screen refuses an incomplete purchase").
    await userEvent.type(screen.getByLabelText(/^date/i), '2026-09-01T10:15')
    await userEvent.clear(within(lines()[0]).getByLabelText(/description/i))
    await userEvent.type(within(lines()[0]).getByLabelText(/description/i), 'Edited')
    await userEvent.click(confirm)

    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    expect(record.mock.calls[0][0].capture).toEqual({
      tempKey: '0f2b0a3c-0000-4000-8000-000000000001',
      state: 'NeedsReview',
      failureReason: null,

      // Three members where there were six: the payload states the invoice code and the creation
      // timestamp, so neither is sent as a field of its own (D30, D32).
      jikr: 'extracted-jikr',
      fiscalSource: 'DecodedFromCode',
      fiscalPayload:
        'https://mapr.tax.gov.me/ic/#/verify?iic=extracted-ikof&crtd=2026-09-01T10:15:00',
    })
    expect(capture).toHaveBeenCalledTimes(1)
  })
})

const mismatch = {
  name: 'line_sum',
  outcome: 'Failed',
  description: 'The lines do not sum to the total.',
  values: { linesTotal: 4.5, total: 4 },
}

describe('Extraction problems are surfaced before confirmation', () => {
  it('names an arithmetic mismatch rather than warning generically', async () => {
    await reviewOf(extracted({ state: 'NeedsReview', validation: { checks: [mismatch] } }))

    expect(screen.getByTestId('review-reasons')).toHaveTextContent(
      /the lines do not sum to the total/i,
    )
  })

  it('leaves a mismatched capture confirmable', async () => {
    const confirm = await reviewOf(
      extracted({ state: 'NeedsReview', validation: { checks: [mismatch] } }),
    )

    expect(confirm).toBeEnabled()
  })

  it('says nothing about checks that passed or could not be performed', async () => {
    await reviewOf(
      extracted({
        state: 'Extracted',
        validation: {
          checks: [
            { ...mismatch, outcome: 'Passed' },
            {
              name: 'total_tax',
              outcome: 'NotApplicable',
              description: 'No tax was read.',
              values: {},
            },
          ],
        },
      }),
    )

    expect(screen.queryByTestId('review-reasons')).not.toBeInTheDocument()
  })

  it('marks the line whose description extraction was unsure of', async () => {
    await reviewOf(
      extracted({
        state: 'NeedsReview',
        result: {
          ...extracted().result,
          candidates: [
            candidate({ reportedConfidence: { description: 0.4 } }),
            candidate({ lineNumber: 2, description: 'Milk', amount: 1.5 }),
          ],
        },
      }),
    )

    expect(within(lines()[0]).getByTestId('low-confidence')).toHaveTextContent(/description/i)
    expect(within(lines()[1]).queryByTestId('low-confidence')).not.toBeInTheDocument()
  })

  it('marks a merchant name extraction was unsure of', async () => {
    await reviewOf(
      extracted({
        state: 'NeedsReview',
        result: { ...extracted().result, reportedConfidence: { merchant_name: 0.3 } },
      }),
    )

    expect(screen.getByTestId('review-reasons')).toHaveTextContent(/merchant name/i)
  })

  // The disagreement reason is gone with the cross-check that produced it: a payload supplied at
  // capture is preferred outright and the image is never read for a second opinion (D31).

  it('does not block confirmation when two fiscal readings differ', async () => {
    const confirm = await reviewOf(
      extracted({
        state: 'NeedsReview',
        supplied: { ...noFiscal, jikr: 'supplied-jikr' },
        extracted: { ...noFiscal, jikr: 'extracted-jikr' },
      }),
    )

    expect(confirm).toBeEnabled()
  })
})

function failed(overrides: Record<string, unknown> = {}) {
  return {
    tempKey: '0f2b0a3c-0000-4000-8000-000000000002',
    state: 'Failed',
    failureReason: 'No extraction stage produced a result for this image.',
    result: null,
    validation: null,
    supplied: noFiscal,
    extracted: noFiscal,
    fiscalSource: 'None',
    fiscalPayload: null,
    alreadyRecorded: null,
    ...overrides,
  }
}

describe('A failed extraction still allows manual entry', () => {
  it('states the reason extraction failed', async () => {
    await reviewOf(failed())

    expect(screen.getByRole('alert')).toHaveTextContent(/no extraction stage produced a result/i)
  })

  it('offers empty fields for the date, amount and a line', async () => {
    await reviewOf(failed())

    expect(screen.getByLabelText(/total amount/i)).toHaveValue(null)
    expect(screen.getByLabelText(/date/i)).toHaveValue('')
    expect(lines()).toHaveLength(1)
    expect(within(lines()[0]).getByLabelText(/description/i)).toHaveValue('')
  })

  it('confirms hand-entered values with the temporary key, as an extracted capture would', async () => {
    record.mockResolvedValue({ id: 12 } as never)
    const confirm = await reviewOf(failed())

    await userEvent.type(screen.getByLabelText(/total amount/i), '9.99')
    await userEvent.type(screen.getByLabelText(/date/i), '2026-09-02T18:30')
    await userEvent.type(within(lines()[0]).getByLabelText(/description/i), 'Coffee')
    await userEvent.type(within(lines()[0]).getByLabelText(/^amount$/i), '9.99')
    await userEvent.type(within(lines()[0]).getByLabelText(/quantity/i), '1')
    await userEvent.selectOptions(within(lines()[0]).getByLabelText(/unit/i), 'kg')
    await userEvent.click(confirm)

    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    expect(record.mock.calls[0][0]).toMatchObject({
      amount: 9.99,
      occurredAt: '2026-09-02T18:30',
      expenses: [{ description: 'Coffee', amount: 9.99 }],
      capture: {
        tempKey: '0f2b0a3c-0000-4000-8000-000000000002',
        state: 'Failed',
        failureReason: 'No extraction stage produced a result for this image.',
      },
    })
  })
})

describe('Confirming a capture creates the purchase', () => {
  it('submits the entered date, amount and lines with the temporary key', async () => {
    record.mockResolvedValue({ id: 3, alreadyRecorded: false } as never)
    const confirm = await reviewOf()

    await userEvent.click(confirm)

    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    expect(record.mock.calls[0][0]).toMatchObject({
      amount: 4,
      merchant: { text: 'Mercadona' },
      expenses: [
        {
          description: 'Bread',
          amount: 2.5,
          quantity: 1,
          categoryCode: 'groceries',
          unitCode: 'kg',
          categoryRaw: 'Pan',
          unitRaw: 'ud',
        },
        { description: 'Milk', amount: 1.5 },
      ],
    })
  })

  it('returns to the home screen once the purchase is created', async () => {
    record.mockResolvedValue({ id: 3, alreadyRecorded: false } as never)
    const confirm = await reviewOf()

    await userEvent.click(confirm)

    expect(await screen.findByText('Home')).toBeInTheDocument()
  })

  it('invalidates the purchases the home screen reads, so the new one appears', async () => {
    record.mockResolvedValue({ id: 3, alreadyRecorded: false } as never)
    capture.mockResolvedValue(extracted() as never)

    const { client } = await photographed(image())
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    await userEvent.click(await screen.findByRole('button', { name: /confirm/i }))

    await waitFor(() =>
      expect(invalidate).toHaveBeenCalledWith(expect.objectContaining({ queryKey: ['purchases'] })),
    )
  })
})

describe('The date defaults from the receipt', () => {
  it('sends no date of its own when the receipt established one and the user left it alone', async () => {
    record.mockResolvedValue({ id: 3 } as never)
    const confirm = await reviewOf()

    await userEvent.click(confirm)

    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    expect(record.mock.calls[0][0].occurredAt).toBeUndefined()
    expect(record.mock.calls[0][0].capture?.fiscalPayload).toContain('crtd=2026-09-01T10:15:00')
  })

  it('sends the date the user entered when they overrode it', async () => {
    record.mockResolvedValue({ id: 3 } as never)
    const confirm = await reviewOf()

    await userEvent.clear(screen.getByLabelText(/date/i))
    await userEvent.type(screen.getByLabelText(/date/i), '2026-09-02T08:00')
    await userEvent.click(confirm)

    await waitFor(() => expect(record).toHaveBeenCalledTimes(1))
    expect(record.mock.calls[0][0].occurredAt).toBe('2026-09-02T08:00')
  })
})

describe('A rejected confirmation is reported in place', () => {
  it('reports a reconciliation mismatch in the ledger words', async () => {
    record.mockRejectedValue(
      new LedgerError('The lines total 4.50, which is not the purchase amount of 4.00.', {
        code: 'purchase.reconciliation_mismatch',
      }),
    )
    const confirm = await reviewOf()

    await userEvent.click(confirm)

    expect(await screen.findByRole('alert')).toHaveTextContent(/which is not the purchase amount/i)
  })

  /**
   * browser-client, "A missing date never reaches the ledger". This used to assert the ledger's
   * own `purchase.occurrence_required` message; that rejection is now unreachable from this screen,
   * because the screen refuses to submit without a date and marks the input instead.
   */
  it('refuses to submit when neither the receipt nor the user gave a date', async () => {
    const confirm = await reviewOf(
      extracted({ extracted: noFiscal, supplied: noFiscal, fiscalSource: 'None' }),
    )

    await userEvent.click(confirm)

    expect(record).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/^date/i)).toHaveAttribute('aria-invalid', 'true')
  })

  it('leaves the entered lines, amount and date exactly as they were', async () => {
    record.mockRejectedValue(new LedgerError('The lines do not reconcile.', { code: 'x' }))
    const confirm = await reviewOf()

    await userEvent.clear(screen.getByLabelText(/total amount/i))
    await userEvent.type(screen.getByLabelText(/total amount/i), '4.5')
    await userEvent.clear(within(lines()[0]).getByLabelText(/description/i))
    await userEvent.type(within(lines()[0]).getByLabelText(/description/i), 'Rye bread')
    await userEvent.click(confirm)

    await screen.findByRole('alert')
    expect(screen.getByLabelText(/total amount/i)).toHaveValue(4.5)
    expect(screen.getByLabelText(/date/i)).toHaveValue('2026-09-01T10:15')
    expect(within(lines()[0]).getByLabelText(/description/i)).toHaveValue('Rye bread')
    expect(lines()).toHaveLength(2)
    expect(screen.queryByText('Home')).not.toBeInTheDocument()
  })
})

describe('Abandoning a capture leaves no trace', () => {
  it('calls nothing further when the screen is left before confirming', async () => {
    await reviewOf()

    const { unmount } = await photographed(image())
    unmount()

    expect(record).not.toHaveBeenCalled()
    expect(capture).toHaveBeenCalledTimes(2)
  })

  it('leaves the ledger unchanged, having recorded nothing', async () => {
    capture.mockResolvedValue(extracted() as never)
    const { client, router } = await photographed(image())
    const invalidate = vi.spyOn(client, 'invalidateQueries')

    await screen.findByRole('button', { name: /confirm/i })

    // The back gesture, which is the primary navigation on a phone (D8): the screen is left, and
    // nothing is asked of the ledger on the way out.
    await act(() => router.navigate('/'))

    expect(await screen.findByText('Home')).toBeInTheDocument()
    expect(record).not.toHaveBeenCalled()
    expect(invalidate).not.toHaveBeenCalled()
  })
})
