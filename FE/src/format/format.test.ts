import { describe, expect, it } from 'vitest'

import { describeWhen, formatAmount, parseOccurredAt } from './format'

describe('An amount is formatted', () => {
  it('shows euro with two decimal places', () => {
    expect(formatAmount(12.5)).toBe('€12.50')
  })

  it('keeps two decimal places on a whole amount', () => {
    expect(formatAmount(7)).toBe('€7.00')
  })
})

describe('Formatting does not change values', () => {
  it.each([0, 0.05, 7, 12.5, 1234.56, 99999.99])('parses %s back to itself', amount => {
    const digits = formatAmount(amount).replace(/[^0-9.]/g, '')

    expect(Number(digits)).toBe(amount)
  })

  it('reports nothing recorded as zero rather than omitting it', () => {
    expect(formatAmount(0)).toBe('€0.00')
  })
})

describe('A recent date is described in relative terms', () => {
  const now = new Date(2026, 8, 3, 10, 0, 0)

  it('describes a purchase occurring today as today', () => {
    expect(describeWhen('2026-09-03T08:15:00', now)).toBe('Today')
  })

  it('describes a purchase occurring yesterday as yesterday', () => {
    expect(describeWhen('2026-09-02T23:59:00', now)).toBe('Yesterday')
  })
})

describe('An older date is shown as a date', () => {
  const now = new Date(2026, 8, 3, 10, 0, 0)

  it('shows the date for a purchase two days ago', () => {
    expect(describeWhen('2026-09-01T12:00:00', now)).toBe('1 Sept 2026')
  })

  it('shows the date for a purchase in a previous month', () => {
    expect(describeWhen('2026-08-14T12:00:00', now)).toBe('14 Aug 2026')
  })
})

describe('An offset-less timestamp is local wall-clock time', () => {
  it('keeps the hour it was written with', () => {
    const occurred = parseOccurredAt('2026-09-03T21:00:00')

    expect(occurred.getFullYear()).toBe(2026)
    expect(occurred.getMonth()).toBe(8)
    expect(occurred.getDate()).toBe(3)
    expect(occurred.getHours()).toBe(21)
  })

  it('does not shift a late purchase across a day boundary', () => {
    const now = new Date(2026, 8, 3, 23, 30, 0)

    expect(describeWhen('2026-09-03T21:00:00', now)).toBe('Today')
  })

  it('tolerates the fractional seconds the backend may serialise', () => {
    const occurred = parseOccurredAt('2026-09-03T21:00:00.1234567')

    expect(occurred.getHours()).toBe(21)
    expect(occurred.getDate()).toBe(3)
  })
})
