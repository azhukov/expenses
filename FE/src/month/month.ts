import type { DateRange } from '../api/reads'
import type { PurchaseView } from '../api/types'
import { parseOccurredAt, type CalendarMonth } from '../format/format'

/** How many entries a launcher shows: enough to recognise a purchase just made, not a list screen. */
const RECENT = 8

function asDateOnly(date: Date): string {
  const month = String(date.getMonth() + 1).padStart(2, '0')
  const day = String(date.getDate()).padStart(2, '0')

  return `${date.getFullYear()}-${month}-${day}`
}

/**
 * The current calendar month against the device's local date. The backend stores wall-clock time,
 * so the boundary has to be computed the same way or a purchase made late on the last of the month
 * lands outside it (D12).
 */
export function currentMonthRange(now: Date = new Date()): DateRange {
  const first = new Date(now.getFullYear(), now.getMonth(), 1)
  const last = new Date(now.getFullYear(), now.getMonth() + 1, 0)

  return { from: asDateOnly(first), to: asDateOnly(last) }
}

function occurredInMonth(purchase: PurchaseView, now: Date): boolean {
  const occurred = parseOccurredAt(purchase.occurredAt)

  return occurred.getFullYear() === now.getFullYear() && occurred.getMonth() === now.getMonth()
}

/**
 * The month to date, derived from the one response rather than from an aggregation endpoint (D5).
 * The response is already scoped to the month; the filter is what keeps the figure honest if it
 * ever is not. The sum is rounded to cents because accumulating floats is where a total acquires a
 * third decimal place that no purchase had.
 */
export function monthTotal(purchases: PurchaseView[], now: Date = new Date()): number {
  const total = purchases
    .filter(purchase => occurredInMonth(purchase, now))
    .reduce((sum, purchase) => sum + purchase.amount, 0)

  return Math.round(total * 100) / 100
}

/**
 * How many receipts in the month still need a person to look at them. Scoped to the month because
 * that is what was read: a receipt needing review older than the current month is not counted, and
 * that is accepted rather than hidden (D5).
 */
export function reviewCount(purchases: PurchaseView[], now: Date = new Date()): number {
  return purchases.filter(
    purchase => purchase.extractionState === 'NeedsReview' && occurredInMonth(purchase, now),
  ).length
}

/** The few most recent, most recent first. Sorted here rather than trusted, then cut. */
export function recentPurchases(purchases: PurchaseView[]): PurchaseView[] {
  return [...purchases]
    .sort(
      (left, right) =>
        parseOccurredAt(right.occurredAt).getTime() - parseOccurredAt(left.occurredAt).getTime(),
    )
    .slice(0, RECENT)
}

/**
 * What the export step can ask for (ledger-export): everything, one calendar month, or an
 * inclusive range of `yyyy-MM-dd` dates.
 */
export type ExportChoice =
  | { kind: 'all' }
  | { kind: 'month'; month: CalendarMonth }
  | { kind: 'range'; from: string; to: string }

/** The calendar month a date falls in, against the device's local date, as the month view does. */
export function currentMonth(now: Date = new Date()): CalendarMonth {
  return { year: now.getFullYear(), month: now.getMonth() + 1 }
}

export function previousMonth({ year, month }: CalendarMonth): CalendarMonth {
  return month === 1 ? { year: year - 1, month: 12 } : { year, month: month - 1 }
}

export function nextMonth({ year, month }: CalendarMonth): CalendarMonth {
  return month === 12 ? { year: year + 1, month: 1 } : { year, month: month + 1 }
}

/** A month as the inclusive range of its first to its last day; day 0 of the next is the last. */
export function monthRange({ year, month }: CalendarMonth): DateRange {
  return {
    from: asDateOnly(new Date(year, month - 1, 1)),
    to: asDateOnly(new Date(year, month, 0)),
  }
}

/** The range a choice asks the ledger for; none at all for everything. */
export function exportRange(choice: ExportChoice): DateRange | undefined {
  switch (choice.kind) {
    case 'all':
      return undefined
    case 'month':
      return monthRange(choice.month)
    case 'range':
      return { from: choice.from, to: choice.to }
  }
}

/**
 * The name a saved export goes under, so that two downloads do not overwrite each other. A month is
 * named as one, which the server cannot do: it only ever sees the range.
 */
export function exportFileName(choice: ExportChoice): string {
  switch (choice.kind) {
    case 'all':
      return 'expenses-all.csv'
    case 'month':
      return `expenses-${String(choice.month.year)}-${String(choice.month.month).padStart(2, '0')}.csv`
    case 'range':
      return `expenses-${choice.from}-to-${choice.to}.csv`
  }
}
