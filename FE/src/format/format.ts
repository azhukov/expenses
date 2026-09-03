/**
 * Presentation only. Nothing here rounds or recomputes an amount: a displayed value is the stored
 * value, differently written (D13). The locale is a constant so that there is one place to change
 * when it becomes configurable, and the `Intl` instances are built once rather than per render.
 */
const LOCALE = 'en-GB'
const CURRENCY = 'EUR'

const amount = new Intl.NumberFormat(LOCALE, {
  style: 'currency',
  currency: CURRENCY,
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
})

const date = new Intl.DateTimeFormat(LOCALE, {
  day: 'numeric',
  month: 'short',
  year: 'numeric',
})

/** An amount in euro, always with two decimal places. Zero is shown, never omitted. */
export function formatAmount(value: number): string {
  return amount.format(value)
}

const TIMESTAMP = /^(\d{4})-(\d{2})-(\d{2})(?:[T ](\d{2}):(\d{2})(?::(\d{2}))?(?:\.(\d+))?)?/

/**
 * `occurredAt` carries no offset: it is wall-clock time for a single-timezone deployment. Reading
 * the components out and building a local `Date` is the only way to be sure of that across
 * browsers — a bare `new Date(string)` is where a 21:00 purchase silently becomes yesterday (D12).
 */
export function parseOccurredAt(occurredAt: string): Date {
  const parts = TIMESTAMP.exec(occurredAt)

  if (parts === null) {
    return new Date(Number.NaN)
  }

  const [, year, month, day, hours, minutes, seconds, fraction] = parts

  return new Date(
    Number(year),
    Number(month) - 1,
    Number(day),
    Number(hours ?? '0'),
    Number(minutes ?? '0'),
    Number(seconds ?? '0'),
    fraction === undefined ? 0 : Number(fraction.slice(0, 3).padEnd(3, '0')),
  )
}

function daysApart(occurred: Date, now: Date): number {
  const occurredDay = new Date(occurred.getFullYear(), occurred.getMonth(), occurred.getDate())
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate())

  return Math.round((today.getTime() - occurredDay.getTime()) / 86_400_000)
}

/**
 * When a purchase occurred, said the way a reader would say it. Today and yesterday are named;
 * anything older is a date, because "3 days ago" stops being easier to place than the date itself.
 */
export function describeWhen(occurredAt: string, now: Date = new Date()): string {
  const occurred = parseOccurredAt(occurredAt)

  switch (daysApart(occurred, now)) {
    case 0:
      return 'Today'
    case 1:
      return 'Yesterday'
    default:
      return date.format(occurred)
  }
}
