import { describe, expect, it } from 'vitest'

import type { CaptureResult } from '../api/types'

import { type EditableLine, type Edits, emptyLine, validate } from './review'

/**
 * Scenarios from browser-client: "The review screen refuses an incomplete purchase" and the parts
 * of "Invalid values are marked on the inputs at fault" that are decidable without rendering.
 */
function line(overrides: Partial<EditableLine> = {}): EditableLine {
  return {
    ...emptyLine(1),
    description: 'Bananas',
    amount: '1.06',
    quantity: '0.482',
    unitCode: 'KG',
    ...overrides,
  }
}

function edits(overrides: Partial<Edits> = {}): Edits {
  return {
    lines: [line()],
    amount: '1.06',
    merchant: '',
    occurredAt: '2026-08-19T14:03',
    dateEdited: false,
    ...overrides,
  }
}

const noFiscalDate: CaptureResult['extracted'] = {
  ikof: null,
  jikr: null,
  issuerTaxNumber: null,
  createdAt: null,
  total: null,
}

describe('validate', () => {
  it('reports every required value when nothing is filled in', () => {
    const errors = validate(edits({ lines: [emptyLine(1)], amount: '', occurredAt: '' }))

    expect(errors.amount).toBeDefined()
    expect(errors.occurredAt).toBeDefined()
    expect(errors.byLine.get(1)?.description).toBeDefined()
    expect(errors.byLine.get(1)?.amount).toBeDefined()
    expect(errors.byLine.get(1)?.quantity).toBeDefined()
    expect(errors.byLine.get(1)?.unitCode).toBeDefined()
  })

  it('reports every offending value at once, across lines', () => {
    const errors = validate(
      edits({
        lines: [line({ key: 1, amount: '' }), line({ key: 2, description: '', unitCode: '' })],
      }),
    )

    expect(errors.byLine.get(1)?.amount).toBeDefined()
    expect(errors.byLine.get(2)?.description).toBeDefined()
    expect(errors.byLine.get(2)?.unitCode).toBeDefined()
  })

  it('refuses a purchase with no lines', () => {
    const errors = validate(edits({ lines: [] }))

    expect(errors.lines).toBeDefined()
  })

  it('accepts a description of 200 characters and refuses 201', () => {
    const at = validate(edits({ lines: [line({ description: 'x'.repeat(200) })] }))
    const over = validate(edits({ lines: [line({ description: 'x'.repeat(201) })] }))

    expect(at.byLine.get(1)?.description).toBeUndefined()
    expect(over.byLine.get(1)?.description).toBeDefined()
  })

  it('measures the description limit after trimming', () => {
    const errors = validate(edits({ lines: [line({ description: `  ${'x'.repeat(200)}  ` })] }))

    expect(errors.byLine.get(1)?.description).toBeUndefined()
  })

  it('requires a unit but not a category', () => {
    const withoutUnit = validate(edits({ lines: [line({ unitCode: '' })] }))
    const withoutCategory = validate(edits({ lines: [line({ categoryCode: '' })] }))

    expect(withoutUnit.byLine.get(1)?.unitCode).toBeDefined()

    // A category is not among the faults a line can have at all, which is the point: nothing
    // decides twice whether it is optional.
    expect(hasAny(withoutCategory)).toBe(false)
  })

  it('does not block confirmation on the values that are optional', () => {
    const errors = validate(edits({ merchant: '', lines: [line({ categoryCode: '' })] }))

    expect(hasAny(errors)).toBe(false)
  })

  it('accepts a date the receipt established and the user left alone', () => {
    const errors = validate(
      edits({ occurredAt: '2026-08-19T14:03', dateEdited: false }),
      '2026-08-19 14:03:00',
    )

    expect(errors.occurredAt).toBeUndefined()
  })

  it('reports a missing date where the receipt carried none', () => {
    const errors = validate(edits({ occurredAt: '', dateEdited: true }), noFiscalDate.createdAt)

    expect(errors.occurredAt).toBeDefined()
  })

  it.each([
    ['blank', ''],
    ['not a number', 'twelve'],
    ['negative', '-1'],
  ])('refuses an amount that is %s', (_name, value) => {
    const purchase = validate(edits({ amount: value }))
    const expense = validate(edits({ lines: [line({ amount: value })] }))

    expect(purchase.amount).toBeDefined()
    expect(expense.byLine.get(1)?.amount).toBeDefined()
  })

  it.each([
    ['blank', ''],
    ['not a number', 'two'],
    ['negative', '-2'],
  ])('refuses a quantity that is %s', (_name, value) => {
    const errors = validate(edits({ lines: [line({ quantity: value })] }))

    expect(errors.byLine.get(1)?.quantity).toBeDefined()
  })

  it('accepts a zero amount and a fractional quantity', () => {
    const errors = validate(
      edits({ amount: '0', lines: [line({ amount: '0', quantity: '0.482' })] }),
    )

    expect(hasAny(errors)).toBe(false)
  })
})

function hasAny(errors: ReturnType<typeof validate>): boolean {
  return (
    errors.amount !== undefined ||
    errors.occurredAt !== undefined ||
    errors.lines !== undefined ||
    [...errors.byLine.values()].some(line => Object.keys(line).length > 0)
  )
}
