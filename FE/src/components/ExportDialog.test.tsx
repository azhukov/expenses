import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from '../api/client'
import { ExportDialog } from './ExportDialog'

/**
 * The export is mocked at the call, and saving at the prop: jsdom cannot download, and what is
 * specified is what was asked for and what was saved under which name.
 */
vi.mock('../api/reads', () => ({ exportPurchases: vi.fn() }))

const { exportPurchases } = await import('../api/reads')

const exported = vi.mocked(exportPurchases)

/** During October 2026, so the default month and the stepper's limit are fixed. */
const october = new Date(2026, 9, 14, 10, 0, 0)

const csv = new Blob(['﻿purchase_id\r\n'], { type: 'text/csv' })

function renderDialog() {
  const onClose = vi.fn()
  const save = vi.fn()

  render(<ExportDialog onClose={onClose} save={save} now={october} />)

  return { onClose, save }
}

function exportButton() {
  return screen.getByRole('button', { name: /^export/i })
}

function choose(name: RegExp) {
  return userEvent.click(screen.getByRole('radio', { name }))
}

function setDate(label: RegExp, value: string) {
  fireEvent.change(screen.getByLabelText(label), { target: { value } })
}

afterEach(() => {
  vi.clearAllMocks()
})

describe('The export step chooses everything, a month or a date range', () => {
  it('starts on the current month, which cannot be stepped past', () => {
    renderDialog()

    expect(screen.getByRole('dialog', { name: /export/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /month/i })).toBeChecked()
    expect(screen.getByText('October 2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /next month/i })).toBeDisabled()
  })

  it('exports a month stepped back to, saved under its name', async () => {
    exported.mockResolvedValue(csv)
    const { save } = renderDialog()

    await userEvent.click(screen.getByRole('button', { name: /previous month/i }))
    expect(screen.getByText('September 2026')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /next month/i })).toBeEnabled()
    await userEvent.click(exportButton())

    expect(exported).toHaveBeenCalledWith({ from: '2026-09-01', to: '2026-09-30' })
    await waitFor(() => expect(save).toHaveBeenCalledWith(csv, 'expenses-2026-09.csv'))
  })

  it('exports everything with no range', async () => {
    exported.mockResolvedValue(csv)
    const { save } = renderDialog()

    await choose(/everything/i)
    await userEvent.click(exportButton())

    expect(exported).toHaveBeenCalledWith(undefined)
    await waitFor(() => expect(save).toHaveBeenCalledWith(csv, 'expenses-all.csv'))
  })

  it('exports a date range', async () => {
    exported.mockResolvedValue(csv)
    const { save } = renderDialog()

    await choose(/date range/i)
    setDate(/^from/i, '2026-10-05')
    setDate(/^to/i, '2026-10-12')
    await userEvent.click(exportButton())

    expect(exported).toHaveBeenCalledWith({ from: '2026-10-05', to: '2026-10-12' })
    await waitFor(() =>
      expect(save).toHaveBeenCalledWith(csv, 'expenses-2026-10-05-to-2026-10-12.csv'),
    )
  })

  it('cannot send an incomplete range', async () => {
    renderDialog()

    await choose(/date range/i)
    expect(exportButton()).toBeDisabled()

    setDate(/^from/i, '2026-10-05')
    expect(exportButton()).toBeDisabled()
  })

  it('refuses an inverted range before sending, marked on the inputs', async () => {
    renderDialog()

    await choose(/date range/i)
    setDate(/^from/i, '2026-10-12')
    setDate(/^to/i, '2026-10-05')

    expect(screen.getByLabelText(/^from/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByLabelText(/^to/i)).toHaveAttribute('aria-invalid', 'true')
    expect(screen.getByText(/the end of the range falls before its start/i)).toBeInTheDocument()
    expect(exportButton()).toBeDisabled()
    expect(exported).not.toHaveBeenCalled()
  })
})

describe('A confirmed export is saved as a file', () => {
  it('shows the export in progress and cannot be confirmed again meanwhile', async () => {
    let answer: (file: Blob) => void = () => undefined
    exported.mockReturnValue(
      new Promise<Blob>(resolve => {
        answer = resolve
      }),
    )
    const { save } = renderDialog()

    await userEvent.click(exportButton())

    expect(screen.getByRole('status')).toHaveTextContent(/exporting/i)
    expect(exportButton()).toBeDisabled()
    await userEvent.click(exportButton())
    expect(exported).toHaveBeenCalledTimes(1)

    answer(csv)
    await waitFor(() => expect(save).toHaveBeenCalledTimes(1))
  })

  it('saves the bytes the ledger returned and closes', async () => {
    exported.mockResolvedValue(csv)
    const { onClose, save } = renderDialog()

    await userEvent.click(exportButton())

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(save.mock.calls[0][0]).toBe(csv)
  })
})

describe('A failed export is reported in the export step', () => {
  it("shows the ledger's message, saves nothing and keeps the choice", async () => {
    exported.mockRejectedValue(
      new LedgerError('The end of the range falls before its start.', {
        code: 'listing.range_invalid',
        status: 400,
      }),
    )
    const { onClose, save } = renderDialog()

    await choose(/date range/i)
    setDate(/^from/i, '2026-10-05')
    setDate(/^to/i, '2026-10-12')
    await userEvent.click(exportButton())

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The end of the range falls before its start.',
    )
    expect(save).not.toHaveBeenCalled()
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByRole('radio', { name: /date range/i })).toBeChecked()
    expect(screen.getByLabelText(/^from/i)).toHaveValue('2026-10-05')
  })

  it('says the ledger could not be reached and can be confirmed again', async () => {
    exported.mockRejectedValueOnce(new LedgerError('The ledger could not be reached.'))
    exported.mockResolvedValueOnce(csv)
    const { save } = renderDialog()

    await userEvent.click(exportButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('The ledger could not be reached.')
    expect(exportButton()).toBeEnabled()

    await userEvent.click(exportButton())
    await waitFor(() => expect(save).toHaveBeenCalledWith(csv, 'expenses-2026-10.csv'))
  })
})

describe('The ledger can be exported from the home screen', () => {
  it('sends nothing when cancelled', async () => {
    const { onClose } = renderDialog()

    await userEvent.click(screen.getByRole('button', { name: /cancel/i }))

    expect(onClose).toHaveBeenCalled()
    expect(exported).not.toHaveBeenCalled()
  })
})
