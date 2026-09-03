import { useLocation } from 'react-router'

/**
 * A placeholder, so that the navigation out of the capture control is verifiable end to end. It
 * uploads nothing: the upload, the wait across the fiscal portal call, and candidate review are a
 * later change.
 */
export function Capture() {
  const state = useLocation().state as { file?: File } | null
  const file = state?.file

  return <main>{file ? <p>{file.name}</p> : <p>No photograph was handed over.</p>}</main>
}
