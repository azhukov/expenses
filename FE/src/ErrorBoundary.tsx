import { Component, type ErrorInfo, type ReactNode } from 'react'

interface Props {
  children: ReactNode
}

interface State {
  failed: boolean
}

/**
 * The root boundary. A render that throws leaves the app blank otherwise, which on a phone is
 * indistinguishable from the page never having loaded.
 */
export class ErrorBoundary extends Component<Props, State> {
  state: State = { failed: false }

  static getDerivedStateFromError(): State {
    return { failed: true }
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Unhandled render failure', error, info)
  }

  render() {
    if (this.state.failed) {
      return (
        <div role="alert" style={{ padding: '1rem' }}>
          <p>Something went wrong.</p>
          <button type="button" onClick={() => window.location.reload()}>
            Reload
          </button>
        </div>
      )
    }

    return this.props.children
  }
}
