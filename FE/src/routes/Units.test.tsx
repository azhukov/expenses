import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { LedgerError } from '../api/client'
import type { UnitView } from '../api/types'
import { Units } from './Units'

/**
 * The dictionary read and the alias writes are mocked at the call, not at the query hook: the
 * screen's own cache handling — refetching after a change — is part of what is specified here.
 */
vi.mock('../api/reads', () => ({ listUnits: vi.fn() }))
vi.mock('../api/writes', () => ({ addUnitAlias: vi.fn(), removeUnitAlias: vi.fn() }))

const { listUnits } = await import('../api/reads')
const { addUnitAlias, removeUnitAlias } = await import('../api/writes')

const list = vi.mocked(listUnits)
const add = vi.mocked(addUnitAlias)
const remove = vi.mocked(removeUnitAlias)

function unit(code: string, name: string, symbol: string, aliases: string[]): UnitView {
  return { id: code.length, code, name, symbol, kind: 'Count', isActive: true, aliases }
}

const piece = unit('PCS', 'Piece', 'pcs', ['kd', 'kom'])
const pack = unit('PACK', 'Pack', 'pack', ['pak'])

function renderUnits() {
  const router = createMemoryRouter(
    [
      { path: '/units', element: <Units /> },
      { path: '/', element: <p>Home</p> },
    ],
    { initialEntries: ['/units'] },
  )
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })

  return {
    client,
    ...render(
      <QueryClientProvider client={client}>
        <RouterProvider router={router} />
      </QueryClientProvider>,
    ),
  }
}

async function sectionOf(name: string) {
  return screen.findByRole('region', { name: new RegExp(`^${name}`, 'i') })
}

beforeEach(() => {
  list.mockResolvedValue([piece, pack])
})

afterEach(() => {
  vi.clearAllMocks()
})

describe('Unit aliases can be managed from the client', () => {
  it('lists each unit with its display name, its symbol and its aliases', async () => {
    renderUnits()

    const section = await sectionOf('Piece')
    expect(within(section).getByText('pcs')).toBeInTheDocument()
    expect(within(section).getByText('kom')).toBeInTheDocument()
    expect(within(section).getByText('kd')).toBeInTheDocument()
  })

  it('adds an alias and shows it once the ledger holds it', async () => {
    add.mockResolvedValue({ ...pack, aliases: ['kut', 'pak'] })
    renderUnits()
    const section = await sectionOf('Pack')

    list.mockResolvedValue([piece, { ...pack, aliases: ['kut', 'pak'] }])
    await userEvent.type(within(section).getByLabelText(/new alias for pack/i), 'kut')
    await userEvent.click(within(section).getByRole('button', { name: /add/i }))

    expect(add).toHaveBeenCalledWith('PACK', 'kut')
    expect(await within(section).findByText('kut')).toBeInTheDocument()
    expect(within(section).getByLabelText(/new alias for pack/i)).toHaveValue('')
  })

  it('removes an alias and no longer shows it', async () => {
    remove.mockResolvedValue()
    renderUnits()
    const section = await sectionOf('Piece')

    list.mockResolvedValue([{ ...piece, aliases: ['kom'] }, pack])
    await userEvent.click(within(section).getByRole('button', { name: /remove kd/i }))

    expect(remove).toHaveBeenCalledWith('PCS', 'kd')
    await waitFor(() => expect(within(section).queryByText('kd')).not.toBeInTheDocument())
  })

  it('reports a conflicting alias beside the unit it was added to', async () => {
    add.mockRejectedValue(
      new LedgerError("The alias 'kom' already belongs to Piece (PCS); remove it there first.", {
        code: 'unit_alias.conflict',
        status: 409,
      }),
    )
    renderUnits()
    const section = await sectionOf('Pack')

    await userEvent.type(within(section).getByLabelText(/new alias for pack/i), 'kom')
    await userEvent.click(within(section).getByRole('button', { name: /add/i }))

    expect(await within(section).findByRole('alert')).toHaveTextContent(/already belongs to piece/i)
    expect(within(await sectionOf('Piece')).getByText('kom')).toBeInTheDocument()
    expect(within(section).queryByText('kom')).not.toBeInTheDocument()
  })

  it('keeps the listing and what was typed when a change cannot reach the ledger', async () => {
    add.mockRejectedValue(new LedgerError('The ledger could not be reached.'))
    renderUnits()
    const section = await sectionOf('Pack')

    await userEvent.type(within(section).getByLabelText(/new alias for pack/i), 'kut')
    await userEvent.click(within(section).getByRole('button', { name: /add/i }))

    expect(await within(section).findByRole('alert')).toHaveTextContent(/could not be reached/i)
    expect(within(section).getByLabelText(/new alias for pack/i)).toHaveValue('kut')
    expect(within(await sectionOf('Piece')).getByText('kom')).toBeInTheDocument()
  })

  it('says so when the units cannot be read', async () => {
    list.mockRejectedValue(new LedgerError('The ledger could not be reached.'))
    renderUnits()

    expect(await screen.findByRole('alert')).toHaveTextContent(/could not be reached/i)
  })

  it('leads back to the home screen', async () => {
    renderUnits()

    await userEvent.click(await screen.findByRole('link', { name: /back/i }))

    expect(await screen.findByText('Home')).toBeInTheDocument()
  })
})
