import { createBrowserRouter } from 'react-router'

import { Capture } from './Capture'
import { Home } from './Home'

/**
 * Home, and the placeholder the capture control navigates to. Routing is a router rather than a
 * rendered component because the OS back gesture is the primary navigation on a phone, so real
 * history entries matter (D8).
 */
export const router = createBrowserRouter([
  {
    path: '/',
    element: <Home />,
  },
  {
    path: '/capture',
    element: <Capture />,
  },
])
