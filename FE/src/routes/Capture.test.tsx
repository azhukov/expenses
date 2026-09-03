import { render, screen } from '@testing-library/react'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { describe, expect, it } from 'vitest'

import { Capture } from './Capture'

function renderAt(state: unknown) {
  const router = createMemoryRouter([{ path: '/capture', element: <Capture /> }], {
    initialEntries: [{ pathname: '/capture', state }],
  })

  return render(<RouterProvider router={router} />)
}

describe('The capture screen receives the photograph', () => {
  it('names the file it was handed', async () => {
    renderAt({ file: new File(['bytes'], 'receipt.jpg', { type: 'image/jpeg' }) })

    expect(await screen.findByText('receipt.jpg')).toBeInTheDocument()
  })

  it('says nothing was handed to it when arrived at directly', async () => {
    renderAt(null)

    expect(await screen.findByText(/no photograph/i)).toBeInTheDocument()
  })
})
