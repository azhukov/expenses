import styles from './ReviewBanner.module.css'

interface Props {
  count: number
}

/**
 * Present only when there is something to report. Where nothing needs review the banner is absent
 * entirely rather than showing a zero, because a zero is a thing to read and dismiss every time.
 */
export function ReviewBanner({ count }: Props) {
  if (count === 0) {
    return null
  }

  return (
    <p className={styles.banner}>
      {count === 1 ? '1 receipt needs review' : `${count} receipts need review`}
    </p>
  )
}
