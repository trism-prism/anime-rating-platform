import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Browse from './pages/Browse'
import TitleDetail from './pages/TitleDetail'
import ComingSoon from './pages/ComingSoon'

export default function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Browse />} />
          <Route path="/titles/:id" element={<TitleDetail />} />
          {/* these pages arrive in later PRs */}
          <Route path="/trending" element={<ComingSoon name="Trending" />} />
          <Route path="/rankings" element={<ComingSoon name="Rankings" />} />
          <Route path="/my-list" element={<ComingSoon name="My List" />} />
          <Route path="/login" element={<ComingSoon name="Login" />} />
          <Route path="/register" element={<ComingSoon name="Register" />} />
          <Route path="*" element={<ComingSoon name="Not found" />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}
