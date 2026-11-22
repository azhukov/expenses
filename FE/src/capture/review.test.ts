import { describe, expect, it } from 'vitest'

import type { CaptureResult, ExtractionCandidateView } from '../api/types'

import {
  confirmationOf,
  type EditableLine,
  type Edits,
  emptyLine,
  isValid,
  lineOf,
  unseenWarnings,
  validate,
  withChange,
} from './review'

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

describe('confirmationOf', () => {
  const payload =
    'https://mapr.tax.gov.me/ic/#/verify?iic=32AA324CFF5030271E16D59F7F8EF636&tin=02365928'

  function fiscalCapture(overrides: Partial<CaptureResult> = {}): CaptureResult {
    return {
      tempKey: null,
      state: 'Extracted',
      failureReason: null,
      result: null,
      validation: null,
      supplied: { ...noFiscalDate, ikof: '32AA324CFF5030271E16D59F7F8EF636' },
      extracted: { ...noFiscalDate, jikr: 'a1b2c3d4-0000-0000-0000-000000000000' },
      fiscalSource: 'RetrievedFromService',
      fiscalPayload: payload,
      alreadyRecorded: null,
      ...overrides,
    }
  }

  it('confirms a fiscal-only capture by its payload, sending no temporary key', () => {
    const request = confirmationOf(fiscalCapture(), edits())

    expect(request.capture).not.toHaveProperty('tempKey')
    expect(request.capture?.fiscalPayload).toBe(payload)
    expect(request.capture?.jikr).toBe('a1b2c3d4-0000-0000-0000-000000000000')
  })

  it('confirms an image capture by its temporary key, as before', () => {
    const request = confirmationOf(
      fiscalCapture({ tempKey: '0f2b0a3c-0000-4000-8000-000000000001' }),
      edits(),
    )

    expect(request.capture?.tempKey).toBe('0f2b0a3c-0000-4000-8000-000000000001')
  })
})

describe('Values the review screen does not show', () => {
  it('are warned about where the ledger would not keep them', () => {
    expect(
      unseenWarnings([
        line({ unitPrice: 4.4885 }),
        line({ listUnitPrice: 8.5, discountAmount: null }),
        line({ listUnitPrice: 2.16, discountAmount: -0.41 }),
        line({ listUnitPrice: 8.5, discountAmount: 4.01, unitPrice: 4.49 }),
      ]),
    ).toEqual([
      'Line 1: the unit price 4.4885 will not be kept, as it has more than two decimal places.',
      'Line 2: the discount (list price 8.5, discount none) will not be kept, as only one of the list price and the discount was read.',
      'Line 3: the discount (list price 2.16, discount -0.41) will not be kept, as it is negative.',
    ])
  })

  it('never make the edits invalid', () => {
    expect(
      isValid(validate(edits({ lines: [line({ unitPrice: 4.4885, listUnitPrice: 8.5 })] }))),
    ).toBe(true)
  })
})

/**
 * Scenarios from browser-client, "A successful capture is presented for review": "An untouched
 * inferred unit is confirmed as inferred", "Choosing a unit clears the guess" and "Editing the
 * quantity does not re-infer".
 */
describe('An inferred unit', () => {
  function inferred(overrides: Partial<ExtractionCandidateView> = {}): ExtractionCandidateView {
    return {
      lineNumber: 1,
      description: 'Deposit',
      amount: 0.2,
      quantity: 1,
      unitPrice: null,
      listUnitPrice: null,
      discountAmount: null,
      taxRatePercent: null,
      categoryRaw: null,
      unitRaw: 'Pfand',
      categoryId: null,
      unitId: 1,
      provenance: {},
      reportedConfidence: {},
      unitInferred: true,
      ...overrides,
    }
  }

  const codeOfUnit = (id: number | null) => (id === 1 ? 'PCS' : '')
  const noCategory = () => ''

  it('is carried from the candidate onto the line', () => {
    const editable = lineOf(inferred(), noCategory, codeOfUnit)

    expect(editable.unitCode).toBe('PCS')
    expect(editable.unitInferred).toBe(true)
  })

  it('is not set on a matched candidate or on a line added by hand', () => {
    expect(lineOf(inferred({ unitInferred: false }), noCategory, codeOfUnit).unitInferred).toBe(
      false,
    )
    expect(emptyLine(1).unitInferred).toBe(false)
  })

  it('is confirmed as inferred while untouched', () => {
    const request = confirmationOf(
      captureOf(),
      edits({ lines: [line({ unitCode: 'PCS', quantity: '1', unitInferred: true })] }),
    )

    expect(request.expenses[0]?.unitCode).toBe('PCS')
    expect(request.expenses[0]?.unitInferred).toBe(true)
  })

  it('is cleared when a unit is chosen', () => {
    const chosen = withChange(line({ unitCode: 'KG', unitInferred: true }), 'unitCode', 'L')

    expect(chosen.unitCode).toBe('L')
    expect(chosen.unitInferred).toBe(false)

    const request = confirmationOf(captureOf(), edits({ lines: [chosen] }))
    expect(request.expenses[0]?.unitInferred).toBe(false)
  })

  it('is not re-inferred when the quantity is edited', () => {
    const edited = withChange(
      line({ unitCode: 'PCS', quantity: '1', unitInferred: true }),
      'quantity',
      '0.482',
    )

    expect(edited.quantity).toBe('0.482')
    expect(edited.unitCode).toBe('PCS')
    expect(edited.unitInferred).toBe(true)
  })

  function captureOf(): CaptureResult {
    return {
      tempKey: '0f2b0a3c-0000-4000-8000-000000000001',
      state: 'Extracted',
      failureReason: null,
      result: null,
      validation: null,
      supplied: noFiscalDate,
      extracted: noFiscalDate,
      fiscalSource: 'DecodedFromCode',
      fiscalPayload: null,
      alreadyRecorded: null,
    }
  }
})

/**
 * Scenarios from browser-client, "The review screen refuses an incomplete purchase": "Pieces with a
 * fractional quantity are refused", "Pieces with a whole quantity are accepted", "A fiscal line
 * printed in pieces with a fractional quantity must be corrected" and "Other units allow fractional
 * quantities".
 */
describe('Pieces need a whole quantity', () => {
  it('refuses pieces with a fractional quantity, on the unit', () => {
    const errors = validate(edits({ lines: [line({ unitCode: 'PCS', quantity: '0.482' })] }))

    expect(errors.byLine.get(1)?.unitCode).toMatch(/whole/i)
    expect(isValid(errors)).toBe(false)
  })

  it('accepts pieces with a whole quantity', () => {
    const errors = validate(
      edits({ lines: [line({ unitCode: 'PCS', quantity: '3', amount: '1.06' })] }),
    )

    expect(isValid(errors)).toBe(true)
  })

  it('refuses a fiscal line printed in pieces with a fractional quantity until it is corrected', () => {
    const errors = validate(
      edits({ lines: [line({ unitCode: 'PCS', quantity: '0.350', unitRaw: 'KOM' })] }),
    )

    expect(errors.byLine.get(1)?.unitCode).toMatch(/whole/i)
  })

  it('allows a fractional quantity in any other unit', () => {
    const errors = validate(edits({ lines: [line({ unitCode: 'PACK', quantity: '0.5' })] }))

    expect(isValid(errors)).toBe(true)
  })
})
