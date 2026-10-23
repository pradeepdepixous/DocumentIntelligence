import { NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

const navItemClass = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-2 text-sm font-medium rounded-md ${
    isActive ? "bg-gray-900 text-white" : "text-gray-600 hover:bg-gray-100"
  }`;

export default function Layout() {
  const { account, isAdmin, logout } = useAuth();

  return (
    <div className="min-h-screen bg-gray-50">
      <header className="border-b border-gray-200 bg-white">
        <div className="mx-auto flex max-w-6xl items-center justify-between px-6 py-4">
          <div className="flex items-center gap-3">
            {account?.picture_url && (
              <img src={account.picture_url} alt="" className="h-8 w-8 rounded-full" referrerPolicy="no-referrer" />
            )}
            <div>
              <h1 className="text-lg font-semibold text-gray-900">Pixous Document Intelligence</h1>
              {account && <p className="text-xs text-gray-500">{account.email}</p>}
            </div>
          </div>
          <button onClick={logout} className="text-sm text-gray-500 hover:text-gray-900">
            Logout
          </button>
        </div>
        <nav className="mx-auto flex max-w-6xl gap-1 px-6 pb-3">
          <NavLink to="/" end className={navItemClass}>
            Overview
          </NavLink>
          <NavLink to="/api-access" className={navItemClass}>
            API Access
          </NavLink>
          <NavLink to="/playground" className={navItemClass}>
            Playground
          </NavLink>
          <NavLink to="/docs" className={navItemClass}>
            API Docs
          </NavLink>
          {isAdmin && (
            <NavLink to="/admin" className={navItemClass}>
              Admin
            </NavLink>
          )}
        </nav>
      </header>

      <main className="mx-auto max-w-6xl px-6 py-8">
        <Outlet />
      </main>
    </div>
  );
}
