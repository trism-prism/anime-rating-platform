import { useCallback, useEffect, useRef, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { api, ApiError } from '../api'
import { useAuth } from '../auth'
import ReviewCard from '../components/ReviewCard'
import { useTitle } from '../useTitle'
import type { ReviewItem } from '../types'

// one review with its thread open: where a reply or reaction notification leads
export default function ReviewPage() {
  const { id = '' } = useParams()
  const { user } = useAuth()
  return <Review key={id + (user?.userId ?? '')} id={id} />
}

function Review({ id }: { id: string }) {
  const [review, setReview] = useState<ReviewItem | null>(null)
  const [missing, setMissing] = useState(false)
  const [error, setError] = useState('')
  const gen = useRef(0)
  useTitle(review ? `Review of ${review.titleName}` : null)

  const load = useCallback(() => {
    const g = ++gen.current
    api<ReviewItem>(`/reviews/${encodeURIComponent(id)}`)
      .then((r) => { if (g === gen.current) { setReview(r); setError('') } })
      .catch((e: Error) => {
        if (g !== gen.current) return
        if (e instanceof ApiError && e.status === 404) setMissing(true)
        else setError(e.message)
      })
  }, [id])

  useEffect(() => { load() }, [load])

  if (missing) {
    return (
      <div>
        <h1>Review not found</h1>
        <p className="muted">It was removed, or you can't see it.</p>
        <Link to="/" className="link">Back to browse</Link>
      </div>
    )
  }
  if (error) return <p className="error" role="alert">{error} <button onClick={() => { setError(''); load() }}>Retry</button></p>
  if (!review) return <div className="skeleton review-skeleton" aria-busy="true" aria-label="Loading review" />

  return (
    <div className="review-page">
      <p className="muted crumb"><Link to={`/titles/${review.titleId}`} className="link">All reviews of {review.titleName}</Link></p>
      <ReviewCard review={review} showTitle defaultOpen onChange={(_, r) => (r ? setReview((x) => (typeof r === 'function' ? x && r(x) : r)) : setMissing(true))} />
    </div>
  )
}
