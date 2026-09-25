import { describe, expect, it } from 'vitest'

import type { ExtractionState, PurchaseView } from '../api/types'
import { currentMonthRange, monthTotal, recentPurchases, reviewCount } from './month'

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
