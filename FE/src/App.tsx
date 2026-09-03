import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RouterProvider } from 'react-router'

import { ErrorBoundary } from './ErrorBoundary'
import { router } from './routes/routes'

/**
 * Returning from the camera app or from a backgrounded tab is the ordinary way to arrive at this
 * client, so refetching on focus is what keeps the list from answering "did I already log that?"
 * with stale data (D7).
 */
function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        refetchOnWindowFocus: true,
        retry: 1,
      },
    },
  })
}

const queryClient = createQueryClient()

export function App() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    </ErrorBoundary>
  )
}
