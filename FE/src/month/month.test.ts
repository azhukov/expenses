import { describe, expect, it } from 'vitest'

import type { ExtractionState, PurchaseView } from '../api/types'
import {
  currentMonth,
  currentMonthRange,
  exportFileName,
  exportRange,
  monthRange,
  monthTotal,
  nextMonth,
  previousMonth,
  recentPurchases,
  reviewCount,
} from './month'

function purchase(
  id: number,
  occurredAt: string,
  amount: number,
  extractionState: ExtractionState | null = null,
): PurchaseView {
  return {
    id,
    occurredAt,
    amount,
    merchantId: null,
    merchantRaw: null,
    hasReceiptImage: extractionState !== null,
    fiscal: null,
    expenses: [],
    totalSaving: 0,
    savingPercentage: null,
    extractionState,
  }
}

const now = new Date(2026, 8, 17, 10, 0, 0)

const month: PurchaseView[] = [
  purchase(4, '2026-09-16T19:30:00', 12.5, 'NeedsReview'),
  purchase(3, '2026-09-10T09:00:00', 30.25, 'Extracted'),
  purchase(2, '2026-09-01T00:15:00', 7, 'NeedsReview'),
  purchase(1, '2026-08-31T23:45:00', 100, 'Failed'),
]

describe('The month-boundary parameters follow the device date', () => {
  it('spans the first to the last day of the local month', () => {
    expect(currentMonthRange(now)).toEqual({ from: '2026-09-01', to: '2026-09-30' })
  })

  it('ends on the last day of a 31-day month', () => {
    expect(currentMonthRange(new Date(2026, 0, 5))).toEqual({
      from: '2026-01-01',
      to: '2026-01-31',
    })
  })

  it('ends on the last day of a leap February', () => {
    expect(currentMonthRange(new Date(2028, 1, 5))).toEqual({
      from: '2028-02-01',
      to: '2028-02-29',
    })
  })

  it('does not shift the boundary for a late-evening local time', () => {
    expect(currentMonthRange(new Date(2026, 8, 30, 23, 59, 0))).toEqual({
      from: '2026-09-01',
      to: '2026-09-30',
    })
  })
})

describe('The total reflects the current month', () => {
  it('sums only purchases occurring in the current calendar month', () => {
    expect(monthTotal(month, now)).toBe(49.75)
  })
})

describe('No purchases this month', () => {
  it('totals zero rather than omitting the figure', () => {
    expect(monthTotal([], now)).toBe(0)
  })

  it('totals zero when every purchase read fell outside the month', () => {
    expect(monthTotal([purchase(1, '2026-08-31T23:45:00', 100)], now)).toBe(0)
  })
})

describe('Purchases needing review exist', () => {
  it('counts only purchases whose extraction needs review', () => {
    expect(reviewCount(month, now)).toBe(2)
  })
})

describe('Nothing needs review', () => {
  it('counts zero when no purchase in the month needs review', () => {
    expect(reviewCount([purchase(3, '2026-09-10T09:00:00', 30.25, 'Extracted')], now)).toBe(0)
  })

  it('counts zero when the month is empty', () => {
    expect(reviewCount([], now)).toBe(0)
  })

  it('does not count a purchase needing review from an earlier month', () => {
    expect(reviewCount([purchase(1, '2026-08-31T23:45:00', 100, 'NeedsReview')], now)).toBe(0)
  })
})

describe('The recent slice', () => {
  it('preserves most-recent-first ordering', () => {
    expect(recentPurchases(month).map(entry => entry.id)).toEqual([4, 3, 2, 1])
  })

  it('reorders a response that did not arrive sorted', () => {
    const shuffled = [month[2], month[0], month[1]]

    expect(recentPurchases(shuffled).map(entry => entry.id)).toEqual([4, 3, 2])
  })

  it('takes only the few that fit on a launcher', () => {
    const many = Array.from({ length: 20 }, (_, index) =>
      purchase(index + 1, `2026-09-${String(index + 1).padStart(2, '0')}T10:00:00`, 1),
    )

    expect(recentPurchases(many)).toHaveLength(8)
    expect(recentPurchases(many)[0].id).toBe(20)
  })
})

describe('A month is a date range from its first to its last day', () => {
  it('spans September 2026', () => {
    expect(monthRange({ year: 2026, month: 9 })).toEqual({ from: '2026-09-01', to: '2026-09-30' })
  })

  it('ends February on the 29th in a leap year and on the 28th otherwise', () => {
    expect(monthRange({ year: 2028, month: 2 })).toEqual({ from: '2028-02-01', to: '2028-02-29' })
    expect(monthRange({ year: 2026, month: 2 })).toEqual({ from: '2026-02-01', to: '2026-02-28' })
  })

  it('takes the current month from the device date', () => {
    expect(currentMonth(new Date(2026, 9, 31, 23, 59))).toEqual({ year: 2026, month: 10 })
  })
})

describe('Months step one at a time across the year boundary', () => {
  it('steps back from January into the previous December', () => {
    expect(previousMonth({ year: 2026, month: 1 })).toEqual({ year: 2025, month: 12 })
  })

  it('steps forward from December into the next January', () => {
    expect(nextMonth({ year: 2025, month: 12 })).toEqual({ year: 2026, month: 1 })
  })

  it('steps within a year', () => {
    expect(previousMonth({ year: 2026, month: 10 })).toEqual({ year: 2026, month: 9 })
    expect(nextMonth({ year: 2026, month: 9 })).toEqual({ year: 2026, month: 10 })
  })
})

describe('What an export choice asks the ledger for', () => {
  it('asks for no range when everything is chosen', () => {
    expect(exportRange({ kind: 'all' })).toBeUndefined()
  })

  it('asks for the whole of a chosen month', () => {
    expect(exportRange({ kind: 'month', month: { year: 2026, month: 9 } })).toEqual({
      from: '2026-09-01',
      to: '2026-09-30',
    })
  })

  it('asks for a chosen range as given', () => {
    expect(exportRange({ kind: 'range', from: '2026-10-05', to: '2026-10-12' })).toEqual({
      from: '2026-10-05',
      to: '2026-10-12',
    })
  })
})

describe('Files are named for what was chosen', () => {
  it('names everything, a month and a range', () => {
    expect(exportFileName({ kind: 'all' })).toBe('expenses-all.csv')
    expect(exportFileName({ kind: 'month', month: { year: 2026, month: 10 } })).toBe(
      'expenses-2026-10.csv',
    )
    expect(exportFileName({ kind: 'range', from: '2026-10-05', to: '2026-10-12' })).toBe(
      'expenses-2026-10-05-to-2026-10-12.csv',
    )
  })

  it('pads a single-digit month', () => {
    expect(exportFileName({ kind: 'month', month: { year: 2026, month: 3 } })).toBe(
      'expenses-2026-03.csv',
    )
  })
})
