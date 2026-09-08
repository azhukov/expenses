import type { ChangeEvent } from 'react'
import { useNavigate } from 'react-router'

import styles from './CaptureControl.module.css'

/**
 * The capture control: a real file input the user's finger lands on, wrapped in a label.
 *
 * There is deliberately no programmatic `click()`, no navigation and no animation between the
 * activation and the browser's own handling of it. iOS Safari opens the camera only for the
 * interaction that asked for it; spend that gesture on anything else and nothing happens at all —
 * no error, no camera. Android is more permissive, which is what makes this the kind of bug that
 * passes every test but the one device that matters (D3).
 */
export function CaptureControl() {
  const navigate = useNavigate()

  function onSelected(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]

    // Dismissing the camera without taking a photograph leaves the screen exactly as it was.
    if (!file) {
      return
    }

    // The `File` is handed on untouched — no resize, no re-encode. A fiscal code on thermal paper
    // is dense enough that downscaling turns a decodable receipt into a probabilistic guess, and
    // the regression is invisible (D4). Uploading is the capture screen's job, not this one's.
    void navigate('/capture', { state: { file } })
  }

  return (
    <label className={styles.control}>
      <input
        className={styles.input}
        type="file"
        accept="image/*"
        capture="environment"
        onChange={onSelected}
      />
      <span className={styles.caption}>Capture a receipt</span>
    </label>
  )
}
