import type { ReactNode } from 'react'

// shared frame for the policy pages; the draft notice appears on every one
export default function Legal({ title, updated, children }: { title: string; updated: string; children: ReactNode }) {
  return (
    <div className="legal">
      <h1>{title}</h1>
      <p className="muted">Last updated: {updated}</p>
      <p className="muted hint">
        This text is a draft for review. It is not legal advice, and it has not been checked by a lawyer.
      </p>
      {children}
    </div>
  )
}
