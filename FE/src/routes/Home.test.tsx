import type { UseQueryResult } from '@tanstack/react-query'
import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from '../api/client'
import { useCategories, useMerchants, usePurchases } from '../api/queries'
import type { ExpenseView, MerchantView, PurchaseView } from '../api/types'
import { Capture } from './Capture'
import { Home } from './Home'

vi.mock('../api/queries', () => ({
  usePurchases: vi.fn(),
  useMerchants: vi.fn(),
  useCategories: vi.fn(),
}))

const now = new Date()

function inThisMonth(day: number, hour = 10): string {
  const month = String(now.getMonth() + 1).padStart(2, '0')
  const dayOfMonth = String(day).padStart(2, '0')

  return `${now.getFullYear()}-${month}-${dayOfMonth}T${String(hour).padStart(2, '0')}:00:00`
}

function expense(id: number): ExpenseView {
  return {
    id,
    description: 'Something',
    quantity: 1,
    amount: 1.2,
    unitId: null,
    unitPrice: null,
    categoryId: null,
    categoryRaw: null,
    unitRaw: null,
    listUnitPrice: null,
    discountAmount: null,
    discountPercentage: null,
  }
}

function purchase(overrides: Partial<PurchaseView>): PurchaseView {
  return {
    id: 1,
    occurredAt: inThisMonth(2),
    amount: 10,
    merchantId: null,
    merchantRaw: null,
    hasReceiptImage: false,
    fiscal: null,
    expenses: [],
    totalSaving: 0,
    savingPercentage: null,
    extractionState: null,
    ...overrides,
  }
}

const merchants: MerchantView[] = [
  { id: 7, name: 'Mercadona', taxId: null, parentId: null, parentName: null, isActive: true },
]

const purchases: PurchaseView[] = [
  purchase({
    id: 3,
    occurredAt: inThisMonth(3, 21),
    amount: 30.25,
    merchantId: 7,
    merchantRaw: 'MERCADONA S.A.',
    hasReceiptImage: true,
    extractionState: 'Extracted',
    expenses: [expense(1), expense(2)],
  }),
  purchase({ id: 2, occurredAt: inThisMonth(2), amount: 12.5, merchantRaw: 'CAFE BAR PEPE' }),
  purchase({ id: 1, occurredAt: inThisMonth(1), amount: 7 }),
]

type PurchasesQuery = ReturnType<typeof usePurchases>
type ReferenceQuery = ReturnType<typeof useMerchants>

const refetch = vi.fn()

function purchasesQuery(state: Partial<PurchasesQuery>): PurchasesQuery {
  return {
    data: undefined,
    isPending: false,
    isError: false,
    error: null,
    refetch,
    ...state,
  } as unknown as PurchasesQuery
}

function referenceQuery<T>(state: Partial<UseQueryResult<T[], Error>>): UseQueryResult<T[], Error> {
  return {
    data: undefined,
    isPending: false,
    isError: false,
    error: null,
    ...state,
  } as unknown as UseQueryResult<T[], Error>
}

function givenPurchases(state: Partial<PurchasesQuery>) {
  vi.mocked(usePurchases).mockReturnValue(purchasesQuery(state))
}

function givenMerchants(state: Partial<ReferenceQuery>) {
  vi.mocked(useMerchants).mockReturnValue(referenceQuery(state))
}

function homeRouter() {
  return createMemoryRouter(
    [
      { path: '/', element: <Home /> },
      { path: '/capture', element: <Capture /> },
    ],
    { initialEntries: ['/'] },
  )
}

function renderHome() {
  return render(<RouterProvider router={homeRouter()} />)
}

function captureControl() {
  return screen.getByRole('link', { name: /capture a receipt/i })
}

beforeEach(() => {
  refetch.mockReset()
  givenPurchases({ data: purchases })
  givenMerchants({ data: merchants })
  vi.mocked(useCategories).mockReturnValue(referenceQuery({ data: [] }))
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Capture is reachable on arrival', () => {
  it('shows the capture action', () => {
    renderHome()

    expect(captureControl()).toBeInTheDocument()
  })

  it('makes it the only control on the screen', () => {
    const { container } = renderHome()

    const controls = container.querySelectorAll(
      'button, a, input, select, textarea, [role="button"]',
    )

    expect(controls).toHaveLength(1)
    expect(controls[0]).toBe(captureControl())
  })
})

describe('Capture survives a failure to read the ledger', () => {
  it('keeps the capture action when purchases could not be read', () => {
    givenPurchases({ isError: true, error: new LedgerError('The ledger could not be reached.') })

    renderHome()

    expect(captureControl()).toBeInTheDocument()
  })

  it('keeps the capture action while purchases are loading', () => {
    givenPurchases({ isPending: true })

    renderHome()

    expect(captureControl()).toBeInTheDocument()
  })
})

describe('Loading is visible', () => {
  it('says it is loading before data arrives', () => {
    givenPurchases({ isPending: true })

    renderHome()

    expect(screen.getByRole('status')).toHaveTextContent(/loading/i)
  })
})

describe('An empty ledger', () => {
  it('states that nothing has been recorded yet, with capture present', () => {
    givenPurchases({ data: [] })

    renderHome()

    expect(screen.getByText(/nothing recorded yet/i)).toBeInTheDocument()
    expect(captureControl()).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('reports the total as zero rather than omitting it', () => {
    givenPurchases({ data: [] })

    renderHome()

    expect(screen.getByText('€0.00')).toBeInTheDocument()
  })
})

describe('Recent purchases are shown', () => {
  it('lists them most recent first', () => {
    renderHome()

    const rows = screen.getAllByRole('listitem')

    expect(within(rows[0]).getByText('Mercadona')).toBeInTheDocument()
    expect(within(rows[1]).getByText('CAFE BAR PEPE')).toBeInTheDocument()
    expect(within(rows[2]).getByText(/unknown merchant/i)).toBeInTheDocument()
  })

  it('shows a merchant, an amount, when it occurred and its number of expense lines', () => {
    renderHome()

    const row = screen.getAllByRole('listitem')[0]

    expect(within(row).getByText('Mercadona')).toBeInTheDocument()
    expect(within(row).getByText('€30.25')).toBeInTheDocument()
    expect(within(row).getByTestId('when')).not.toBeEmptyDOMElement()
    expect(within(row).getByText(/2 lines/i)).toBeInTheDocument()
  })

  it('shows the month total', () => {
    renderHome()

    expect(screen.getByText('€49.75')).toBeInTheDocument()
  })
})

describe('A purchase carrying a receipt is distinguishable', () => {
  it('marks the entry that has one and not the entry that has none', () => {
    renderHome()

    const rows = screen.getAllByRole('listitem')

    expect(within(rows[0]).getByText(/receipt/i)).toBeInTheDocument()
    expect(within(rows[1]).queryByText(/receipt/i)).not.toBeInTheDocument()
  })

  it('marks a purchase read from its fiscal code alone, which has no image', () => {
    givenPurchases({
      data: [
        purchase({
          id: 9,
          merchantRaw: 'MEGAPROMET',
          hasReceiptImage: false,
          extractionState: 'Extracted',
          fiscal: {
            ikofSupplied: '32AA324CFF5030271E16D59F7F8EF636',
            ikofExtracted: null,
            jikrSupplied: null,
            jikrExtracted: 'a1b2c3d4-0000-0000-0000-000000000000',
            extractedSource: 'RetrievedFromService',
            payload: 'https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636',
            payloadSource: 'SuppliedAtUpload',
            corroboration: 'Unverified',
          },
        }),
      ],
    })

    renderHome()

    expect(within(screen.getByRole('listitem')).getByText(/receipt/i)).toBeInTheDocument()
  })
})

describe('Entries are not controls', () => {
  it('gives no indication that an entry can be activated', () => {
    renderHome()

    for (const row of screen.getAllByRole('listitem')) {
      expect(row).not.toHaveAttribute('role')
      expect(row).not.toHaveAttribute('tabindex')
      expect(row.querySelector('button, a')).toBeNull()
    }
  })

  it('does nothing when an entry is tapped', async () => {
    renderHome()

    const row = screen.getAllByRole('listitem')[0]
    await userEvent.click(row)

    expect(screen.getAllByRole('listitem')).toHaveLength(3)
    expect(screen.getByText('Mercadona')).toBeInTheDocument()
  })
})

describe('Purchases needing review exist', () => {
  it('reports how many there are', () => {
    givenPurchases({
      data: [
        purchase({ id: 5, hasReceiptImage: true, extractionState: 'NeedsReview' }),
        purchase({ id: 6, hasReceiptImage: true, extractionState: 'NeedsReview' }),
        purchase({ id: 7, hasReceiptImage: true, extractionState: 'Extracted' }),
      ],
    })

    renderHome()

    expect(screen.getByText(/2 receipts need review/i)).toBeInTheDocument()
  })

  it('names the single one in the singular', () => {
    givenPurchases({
      data: [purchase({ id: 5, hasReceiptImage: true, extractionState: 'NeedsReview' })],
    })

    renderHome()

    expect(screen.getByText(/1 receipt needs review/i)).toBeInTheDocument()
  })
})

describe('Nothing needs review', () => {
  it('shows nothing in its place rather than a zero', () => {
    renderHome()

    expect(screen.queryByText(/needs? review/i)).not.toBeInTheDocument()
  })
})

describe('No breakdown accompanies the total', () => {
  it('renders no chart, category breakdown or period comparison', () => {
    const { container } = renderHome()

    expect(container.querySelector('svg, canvas')).toBeNull()
    expect(screen.queryByText(/last month|previous month|compared/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/breakdown|by category/i)).not.toBeInTheDocument()
  })
})

describe('The ledger cannot be reached', () => {
  it('reports the failure, offers a retry, and keeps capture usable', () => {
    givenPurchases({ isError: true, error: new LedgerError('The ledger could not be reached.') })

    renderHome()

    expect(screen.getByRole('alert')).toHaveTextContent(/could not be loaded/i)
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
    expect(captureControl()).toBeInTheDocument()
  })
})

describe('The ledger reports a specific error', () => {
  it('shows the message the ledger sent rather than a generic one', () => {
    givenPurchases({
      isError: true,
      error: new LedgerError('The requested range is not a range.', {
        code: 'purchase.invalid_range',
      }),
    })

    renderHome()

    const failure = screen.getByRole('alert')

    expect(failure).toHaveTextContent('The requested range is not a range.')
    expect(failure).not.toHaveTextContent('purchase.invalid_range')
  })
})

describe('Retrying succeeds', () => {
  it('asks the ledger again and then shows the purchases with no failure reported', async () => {
    givenPurchases({ isError: true, error: new LedgerError('The ledger could not be reached.') })

    const { rerender } = renderHome()

    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    expect(refetch).toHaveBeenCalled()

    givenPurchases({ data: purchases })
    rerender(<RouterProvider router={homeRouter()} />)

    expect(await screen.findByText('Mercadona')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('Reference data cannot be read', () => {
  it('still lists purchases on their verbatim text and reports no ledger failure', () => {
    givenMerchants({ isError: true, error: new LedgerError('The ledger could not be reached.') })

    renderHome()

    expect(screen.getByText('MERCADONA S.A.')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(captureControl()).toBeInTheDocument()
  })
})
