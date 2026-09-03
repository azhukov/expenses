import type { MerchantView, PurchaseView } from '../api/types'
import { PurchaseRow } from './PurchaseRow'

import styles from './RecentList.module.css'

interface Props {
  purchases: PurchaseView[]
  merchants: MerchantView[]
  isPending: boolean
  failure: string | null
  onRetry: () => void
}

/**
 * The supporting half of the launcher. Every state it can be in — loading, empty, failed — leaves
 * the capture control above it untouched, because capturing does not depend on any of this (D1).
 */
export function RecentList({ purchases, merchants, isPending, failure, onRetry }: Props) {
  if (isPending) {
    return (
      <p className={styles.note} role="status">
        Loading recent purchases…
      </p>
    )
  }

  if (failure !== null) {
    return (
      <div className={styles.note} role="alert">
        {/* The ledger's own words, not a second vocabulary invented here. */}
        <p>Purchases could not be loaded. {failure}</p>
        <button className={styles.retry} type="button" onClick={onRetry}>
          Try again
        </button>
      </div>
    )
  }

  if (purchases.length === 0) {
    return <p className={styles.note}>Nothing recorded yet.</p>
  }

  return (
    <ul className={styles.list}>
      {purchases.map(purchase => (
        <PurchaseRow key={purchase.id} purchase={purchase} merchants={merchants} />
      ))}
    </ul>
  )
}
