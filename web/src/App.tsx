import { BrowserRouter, Route, Routes } from 'react-router-dom'
import Layout from './components/Layout'
import Browse from './pages/Browse'
import TitleDetail from './pages/TitleDetail'
import ComingSoon from './pages/ComingSoon'
import Login from './pages/Login'
import Register from './pages/Register'
import CheckEmail from './pages/CheckEmail'
import ConfirmEmail from './pages/ConfirmEmail'
import { AuthProvider } from './auth'

export default function App() {
  return (
    <AuthProvider>
    <BrowserRouter>
      <Routes>
        <Route element={<Layout />}>
          <Route path="/" element={<Browse />} />
          <Route path="/titles/:id" element={<TitleDetail />} />
          {/* these pages arrive in later PRs */}
          <Route path="/trending" element={<ComingSoon name="Trending" />} />
          <Route path="/rankings" element={<ComingSoon name="Rankings" />} />
          <Route path="/my-list" element={<ComingSoon name="My List" />} />
          <Route path="/login" element={<Login />} />
          <Route path="/register" element={<Register />} />
          <Route path="/check-email" element={<CheckEmail />} />
          <Route path="/confirm-email" element={<ConfirmEmail />} />
          <Route path="*" element={<ComingSoon name="Not found" />} />
        </Route>
      </Routes>
    </BrowserRouter>
    </AuthProvider>
  )
}
