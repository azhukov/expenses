import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from '../api/client'
import { ClearLedgerDialog } from './ClearLedgerDialog'

/**
 * The count and the clear are mocked at the call, not at the hook: refetching the count after a
 * conflict and invalidating the month after a clear are part of what is specified here.
 */
vi.mock('../api/reads', () => ({ countPurchases: vi.fn() }))
vi.mock('../api/writes', () => ({ clearLedger: vi.fn() }))

const { countPurchases } = await import('../api/reads')
const { clearLedger } = await import('../api/writes')

const count = vi.mocked(countPurchases)
const clear = vi.mocked(clearLedger)

const mismatch = new LedgerError('The ledger holds 13 purchases, not 12. Nothing was deleted.', {
  code: 'purchase.clear.count_mismatch',
  status: 409,
})

function renderDialog() {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const invalidate = vi.spyOn(client, 'invalidateQueries')
  const onClose = vi.fn()

  render(
    <QueryClientProvider client={client}>
      <ClearLedgerDialog onClose={onClose} />
    </QueryClientProvider>,
  )

  return { invalidate, onClose }
}

function dialog() {
  return screen.getByRole('alertdialog', { name: /clear all purchases/i })
}

function confirmButton() {
  return screen.queryByRole('button', { name: /^delete/i })
}

afterEach(() => {
  vi.clearAllMocks()
})

describe('Clearing is confirmed against the number of purchases', () => {
  it('shows the number to be deleted across all dates and says it cannot be undone', async () => {
    count.mockResolvedValue(12)

    renderDialog()

    expect(await screen.findByText(/all 12 recorded purchases/i)).toBeInTheDocument()
    expect(dialog()).toHaveTextContent(/every month/i)
    expect(dialog()).toHaveTextContent(/categories, units and merchants are kept/i)
    expect(dialog()).toHaveTextContent(/cannot be undone/i)
    expect(confirmButton()).toHaveTextContent('Delete 12 purchases')
  })

  it('names a single purchase in the singular', async () => {
    count.mockResolvedValue(1)

    renderDialog()

    expect(await screen.findByRole('button', { name: 'Delete 1 purchase' })).toBeInTheDocument()
  })

  it('moves focus to cancel, the safe choice', async () => {
    count.mockResolvedValue(12)

    renderDialog()

    await screen.findByText(/all 12 recorded purchases/i)
    expect(screen.getByRole('button', { name: /cancel/i })).toHaveFocus()
  })

  it('clears exactly the number shown, then refreshes the month and closes', async () => {
    count.mockResolvedValue(12)
    clear.mockResolvedValue(12)
    const { invalidate, onClose } = renderDialog()

    await userEvent.click(await screen.findByRole('button', { name: 'Delete 12 purchases' }))

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(clear).toHaveBeenCalledWith(12)
    expect(invalidate).toHaveBeenCalledWith({ queryKey: ['purchases'] })
  })

  it('sends nothing when cancelled', async () => {
    count.mockResolvedValue(12)
    const { onClose } = renderDialog()

    await screen.findByText(/all 12 recorded purchases/i)
    await userEvent.click(screen.getByRole('button', { name: /cancel/i }))

    expect(onClose).toHaveBeenCalled()
    expect(clear).not.toHaveBeenCalled()
  })

  it('says there is nothing to clear, and offers no way to confirm, when the ledger is empty', async () => {
    count.mockResolvedValue(0)

    renderDialog()

    expect(await screen.findByText(/nothing to clear/i)).toBeInTheDocument()
    expect(confirmButton()).not.toBeInTheDocument()
  })

  it('reports a count that cannot be read and offers no way to confirm', async () => {
    count.mockRejectedValue(new LedgerError('The ledger could not be reached.'))

    renderDialog()

    expect(await screen.findByRole('alert')).toHaveTextContent('The ledger could not be reached.')
    expect(confirmButton()).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /cancel|close/i })).toBeInTheDocument()
  })
})

describe('A rejected clear is reported in the confirmation step', () => {
  it('shows the new count after something was recorded meanwhile, and asks again', async () => {
    count.mockResolvedValueOnce(12).mockResolvedValue(13)
    clear.mockRejectedValueOnce(mismatch).mockResolvedValue(13)
    const { onClose } = renderDialog()

    await userEvent.click(await screen.findByRole('button', { name: 'Delete 12 purchases' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The ledger holds 13 purchases, not 12. Nothing was deleted.',
    )
    expect(await screen.findByRole('button', { name: 'Delete 13 purchases' })).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()

    await userEvent.click(screen.getByRole('button', { name: 'Delete 13 purchases' }))

    await waitFor(() => expect(onClose).toHaveBeenCalled())
    expect(clear).toHaveBeenLastCalledWith(13)
  })

  it('reports a ledger it could not reach and stays open', async () => {
    count.mockResolvedValue(12)
    clear.mockRejectedValue(new LedgerError('The ledger could not be reached.'))
    const { onClose, invalidate } = renderDialog()

    await userEvent.click(await screen.findByRole('button', { name: 'Delete 12 purchases' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('The ledger could not be reached.')
    expect(dialog()).toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()
    expect(invalidate).not.toHaveBeenCalledWith({ queryKey: ['purchases'] })
  })
})
