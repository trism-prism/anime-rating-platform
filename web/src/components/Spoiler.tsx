import { useState, type ReactNode } from 'react'

// hides a whole text behind a button; the text is in the API response but is not rendered in the DOM until revealed
export default function Spoiler({ children, what }: { children: ReactNode; what: string }) {
  const [shown, setShown] = useState(false)
  if (shown) {
    return (
      <div>
        {children}
        <button className="link-button spoiler-toggle" onClick={() => setShown(false)}>Hide spoiler</button>
      </div>
    )
  }
  return (
    <div className="spoiler-box">
      <span className="muted">This {what} contains spoilers.</span>
      <button onClick={() => setShown(true)}>Show spoiler</button>
    </div>
  )
}
