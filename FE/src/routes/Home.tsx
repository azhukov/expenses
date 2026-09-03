import { useMemo } from 'react'

import { LedgerError } from '../api/client'
import { useMerchants, usePurchases } from '../api/queries'
import { CaptureControl } from '../capture/CaptureControl'
import { MonthHeader } from '../components/MonthHeader'
import { RecentList } from '../components/RecentList'
import { ReviewBanner } from '../components/ReviewBanner'
import { currentMonthRange, monthTotal, recentPurchases, reviewCount } from '../month/month'

import styles from './Home.module.css'

function messageOf(error: unknown): string {
  return error instanceof LedgerError ? error.message : 'The ledger could not be reached.'
}

/**
 * A launcher, not a dashboard. Capture is the point of the screen; the month's figures and the
 * recent few are what make it possible to tell whether a purchase was already logged (D1).
 *
 * One request answers all three questions: the total, the review count and the recent slice are
 * derived from the same month response (D5). The merchant dictionary is a separate query on
 * purpose — its failure degrades a row to what the receipt printed, and must not fail the page (D6).
 */
export function Home() {
  const range = useMemo(() => currentMonthRange(), [])
  const purchases = usePurchases(range)
  const merchants = useMerchants()

  const month = purchases.data ?? []

  return (
    <div className={styles.page} data-testid="page">
      <MonthHeader total={monthTotal(month)} />
      <ReviewBanner count={reviewCount(month)} />

      <main className={styles.recent} data-testid="recent">
        <RecentList
          purchases={recentPurchases(month)}
          merchants={merchants.data ?? []}
          isPending={purchases.isPending}
          failure={purchases.isError ? messageOf(purchases.error) : null}
          onRetry={() => void purchases.refetch()}
        />
      </main>

      <div className={styles.capture} data-testid="capture-dock">
        <CaptureControl />
      </div>
    </div>
  )
}
