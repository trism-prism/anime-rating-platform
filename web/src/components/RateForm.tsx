import { useEffect, useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { useAuth } from '../auth'
import type { MyRating } from '../types'

const FIELDS = ['story', 'art', 'characters', 'enjoyment'] as const
type Field = (typeof FIELDS)[number]

const blank: Record<Field, number> = { story: 5, art: 5, characters: 5, enjoyment: 5 }

export default function RateForm({ titleId, onChanged }: { titleId: number; onChanged: () => void }) {
  const { user, emailVerified } = useAuth()
  const [scores, setScores] = useState(blank)
  const [review, setReview] = useState('')
  const [hasRating, setHasRating] = useState(false)
  const [msg, setMsg] = useState('')
  const [error, setError] = useState('')
  const [busy, setBusy] = useState(false)

  // prefill with the user's existing rating for this title
  useEffect(() => {
    if (!user) return
    let stale = false
    api<MyRating[]>('/me/ratings')
      .then((list) => {
        const mine = list.find((r) => r.titleId === titleId)
        if (stale || !mine) return
        setScores({ story: mine.story, art: mine.art, characters: mine.characters, enjoyment: mine.enjoyment })
        setReview(mine.reviewText ?? '')
        setHasRating(true)
      })
      .catch(() => {})
    return () => {
      stale = true
    }
  }, [titleId, user])

  if (!user) {
    return <p><Link to="/login" className="link">Log in</Link> to rate this title.</p>
  }

  async function save(e: FormEvent) {
    e.preventDefault()
    setBusy(true)
    setMsg('')
    setError('')
    try {
      await api(`/titles/${titleId}/rating`, { method: 'PUT', body: JSON.stringify({ ...scores, reviewText: review }) })
      setHasRating(true)
      setMsg('Saved.')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  async function remove() {
    setBusy(true)
    setMsg('')
    setError('')
    try {
      await api(`/titles/${titleId}/rating`, { method: 'DELETE' })
      setScores(blank)
      setReview('')
      setHasRating(false)
      setMsg('Rating deleted.')
      onChanged()
    } catch (err) {
      setError((err as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <form className="rate-form" onSubmit={save}>
      {FIELDS.map((f) => (
        <label key={f} className="rate-row">
          <span className="score-label">{f[0].toUpperCase() + f.slice(1)}</span>
          <input
            type="range"
            min={1}
            max={10}
            value={scores[f]}
            onChange={(e) => setScores({ ...scores, [f]: Number(e.target.value) })}
          />
          <span className="score-value">{scores[f]}</span>
        </label>
      ))}
      <textarea
        placeholder="Review (optional)"
        rows={4}
        value={review}
        disabled={!emailVerified}
        onChange={(e) => setReview(e.target.value)}
      />
      {!emailVerified && (
        <p className="muted hint"><Link to="/settings" className="link">Verify your email in Settings</Link> to write a review</p>
      )}
      <div className="row">
        <button type="submit" disabled={busy}>{hasRating ? 'Update rating' : 'Save rating'}</button>
        {hasRating && <button type="button" disabled={busy} onClick={remove}>Delete</button>}
        {msg && <span className="muted">{msg}</span>}
        {error && <span className="error">{error}</span>}
      </div>
    </form>
  )
}
