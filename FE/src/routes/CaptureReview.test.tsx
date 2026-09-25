import { fireEvent, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { CaptureResult } from '../api/types'
import { describeWhen } from '../format/format'

import { CaptureReview } from './CaptureReview'

vi.mock('../api/queries', async importActual => ({
  ...(await importActual<typeof import('../api/queries')>()),
  useCategories: () => ({ data: [{ id: 3, code: 'groceries', name: 'Groceries' }] }),
  useUnits: () => ({ data: [{ id: 5, code: 'kg', name: 'Kilogram', symbol: 'kg' }] }),
}))

const noFiscal = { ikof: null, jikr: null, issuerTaxNumber: null, createdAt: null, total: null }

function candidate(overrides: Record<string, unknown> = {}) {
  return {
    lineNumber: 1,
    description: 'Bread',
    amount: 2.5,
    quantity: 1,
    unitPrice: 2.5,
    listUnitPrice: null,
    discountAmount: null,
    taxRatePercent: null,
    categoryRaw: null,
    unitRaw: null,
    categoryId: 3,
    unitId: 5,
    provenance: {},
    reportedConfidence: {},
    ...overrides,
  }
}

function captured(overrides: Record<string, unknown> = {}): CaptureResult {
  return {
    tempKey: '0f2b0a3c-0000-4000-8000-000000000001',
    state: 'Extracted',
    failureReason: null,
    result: {
      engineName: 'vision-gpt',
      engineVersion: '1',
      stepsRun: ['vision'],
      candidates: [candidate()],
      total: 2.5,
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
    fiscalPayload: null,
    alreadyRecorded: null,
    ...overrides,
  }
}

function show(capture: CaptureResult = captured()) {
  const onConfirm = vi.fn()

  render(
    <CaptureReview capture={capture} onConfirm={onConfirm} failure={null} isConfirming={false} />,
  )

  return { onConfirm, confirm: screen.getByRole('button', { name: /confirm/i }) }
}

function line() {
  return screen.getAllByTestId('line')[0]
}

describe('The review screen refuses an incomplete purchase', () => {
  it('does not submit, and marks the input, when a line amount is blank', async () => {
    const { onConfirm, confirm } = show()

    await userEvent.clear(within(line()).getByLabelText(/amount/i))
    await userEvent.click(confirm)

    expect(onConfirm).not.toHaveBeenCalled()
    expect(within(line()).getByLabelText(/amount/i)).toHaveAttribute('aria-invalid', 'true')
    expect(within(line()).getByText(/amount is required/i)).toBeInTheDocument()
  })

  it('leaves the valid inputs unmarked', async () => {
    const { confirm } = show()

    await userEvent.clear(within(line()).getByLabelText(/amount/i))
    await userEvent.click(confirm)

    expect(within(line()).getByLabelText(/description/i)).not.toHaveAttribute(
      'aria-invalid',
      'true',
    )
    expect(screen.getByLabelText(/merchant/i)).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('marks nothing before the first attempt to confirm', () => {
    show(captured({ result: null, state: 'Failed', failureReason: 'The image could not be read.' }))

    expect(screen.queryByRole('alert', { name: /required/i })).not.toBeInTheDocument()
    expect(screen.getByLabelText(/total amount/i)).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('reports every required value when nothing is filled in', async () => {
    // A failed capture that decoded no fiscal code either, which is the one that leaves every
    // field empty: a date the receipt established would satisfy the rule by itself.
    const { onConfirm, confirm } = show(
      captured({
        result: null,
        state: 'Failed',
        failureReason: 'The image could not be read.',
        extracted: noFiscal,
      }),
    )

    await userEvent.click(confirm)

    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/total amount/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText(/^date/i)).toHaveAttribute('aria-invalid', 'true')
    expect(within(line()).getByLabelText(/description/i)).toHaveAttribute('aria-invalid', 'true')
    expect(within(line()).getByLabelText(/unit/i)).toHaveAttribute('aria-invalid', 'true')
  })

  it('does not submit a purchase with no lines', async () => {
    const { onConfirm, confirm } = show()

    await userEvent.click(screen.getByRole('button', { name: /remove/i }))
    await userEvent.click(confirm)

    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByText(/at least one expense line/i)).toBeInTheDocument()
  })

  it('does not submit a description longer than 200 characters', async () => {
    const { onConfirm, confirm } = show()

    const description = within(line()).getByLabelText(/description/i)

    // Typed in one go rather than keystroke by keystroke: 201 characters is what is under test,
    // not the typing.
    fireEvent.change(description, { target: { value: 'x'.repeat(201) } })
    await userEvent.click(confirm)

    expect(onConfirm).not.toHaveBeenCalled()
    expect(description).toHaveAttribute('aria-invalid', 'true')
  })

  it('submits when the optional values are the only ones left empty', async () => {
    const { onConfirm, confirm } = show()

    await userEvent.clear(screen.getByLabelText(/merchant/i))
    await userEvent.selectOptions(within(line()).getByLabelText(/category/i), '')
    await userEvent.click(confirm)

    expect(onConfirm).toHaveBeenCalledTimes(1)
  })
})

describe('A missing date never reaches the ledger', () => {
  it('marks the date rather than submitting it', async () => {
    const { onConfirm, confirm } = show(captured({ extracted: noFiscal, supplied: noFiscal }))

    await userEvent.click(confirm)

    expect(onConfirm).not.toHaveBeenCalled()
    expect(screen.getByLabelText(/^date/i)).toHaveAttribute('aria-invalid', 'true')
    expect(within(line()).getByLabelText(/description/i)).toHaveValue('Bread')
  })
})

describe('Invalid values are marked on the inputs at fault', () => {
  it('clears a mark as soon as the value becomes valid, without confirming again', async () => {
    const { confirm } = show()

    const amount = within(line()).getByLabelText(/amount/i)
    await userEvent.clear(amount)
    await userEvent.click(confirm)
    expect(amount).toHaveAttribute('aria-invalid', 'true')

    await userEvent.type(amount, '2.5')

    expect(amount).not.toHaveAttribute('aria-invalid', 'true')
    expect(within(line()).queryByText(/amount is required/i)).not.toBeInTheDocument()
  })

  /**
   * The message is described by the input, not part of its name. When it lived inside the wrapping
   * label the field's accessible name became "Amount An amount is required.", which is both wrong
   * to hear and enough to lose the field to any exact query.
   */
  it('does not make the message part of the input name', async () => {
    const { confirm } = show()

    await userEvent.clear(within(line()).getByLabelText('Amount', { exact: true }))
    await userEvent.click(confirm)

    expect(within(line()).getByLabelText('Amount', { exact: true })).toHaveAttribute(
      'aria-invalid',
      'true',
    )
  })

  it('associates the message with the input it is about', async () => {
    const { confirm } = show()

    const amount = within(line()).getByLabelText(/amount/i)
    await userEvent.clear(amount)
    await userEvent.click(confirm)

    const described = amount.getAttribute('aria-describedby')
    expect(described).toBeTruthy()
    expect(document.getElementById(described!)).toHaveTextContent(/amount is required/i)
  })
})

describe('The review screen names the extraction engine', () => {
  it('names the engine the capture response reported', () => {
    show()

    expect(screen.getByTestId('engine')).toHaveTextContent('vision-gpt')
  })

  it('states that there is no reading where extraction produced none', () => {
    show(captured({ result: null, state: 'Failed', failureReason: 'The image could not be read.' }))

    expect(screen.getByTestId('engine')).toHaveTextContent(/no extraction engine reading/i)
    expect(screen.queryByText('vision-gpt')).not.toBeInTheDocument()
  })
})

describe('An invoice already recorded is flagged at review', () => {
  it('states that the invoice appears to be recorded already, with that purchase date, and still confirms', async () => {
    const occurredAt = '2026-08-29T14:59:22'
    const { onConfirm, confirm } = show(
      captured({ alreadyRecorded: { purchaseId: 41, occurredAt } }),
    )

    const warning = screen.getByTestId('already-recorded')
    expect(warning).toHaveTextContent(/recorded already/i)
    expect(warning).toHaveTextContent(describeWhen(occurredAt))

    await userEvent.click(confirm)

    expect(onConfirm).toHaveBeenCalled()
  })

  it('shows no duplicate warning when the response names no earlier purchase', () => {
    show()

    expect(screen.queryByTestId('already-recorded')).not.toBeInTheDocument()
  })
})
