import { Link } from 'react-router'

import styles from './CaptureControl.module.css'

/**
 * Home's capture action: a plain link to the capture screen. The camera is not opened here any
 * more. The capture screen scans the rear camera for the receipt's fiscal code first, and holds the
 * photograph control — the real file input iOS requires the finger to land on (D3) — for when that
 * produces no invoice (D40).
 */
export function CaptureControl() {
  return (
    <Link to="/capture" className={styles.control}>
      <span className={styles.caption}>Capture a receipt</span>
    </Link>
  )
}
