import { Link, useNavigate } from 'react-router-dom'
import SearchBox from '../components/SearchBox'
import { useTitle } from '../useTitle'

export default function NotFound() {
  useTitle('Page not found')
  const navigate = useNavigate()
  return (
    <div className="not-found">
      <h1>Page not found</h1>
      <p className="muted">This address doesn't lead anywhere. Check the link, or search for what you were after.</p>
      <div className="filters">
        <SearchBox onSubmit={(q) => navigate(q ? `/?q=${encodeURIComponent(q)}` : '/')} />
      </div>
      <Link to="/" className="button">Back to Browse</Link>
    </div>
  )
}
