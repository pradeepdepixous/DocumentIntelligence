import { useRef, useState } from "react";
import { Link, NavLink, Outlet } from "react-router-dom";
import { useAuth } from "../context/AuthContext";

const navItemClass = ({ isActive }: { isActive: boolean }) =>
  `px-3 py-1.5 text-sm font-medium rounded-md ${
    isActive ? "bg-navy text-white" : "text-gray-600 hover:bg-gray-100"
  }`;

function ProfileMenu() {
  const { account, logout } = useAuth();
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const initials = (account?.email ?? "?").slice(0, 2).toUpperCase();

  return (
    <div className="relative" ref={containerRef}>
      <button
        onClick={() => setOpen((v) => !v)}
        onBlur={() => setTimeout(() => setOpen(false), 150)}
        className="flex items-center gap-2 rounded-full border border-transparent py-1 pl-1 pr-2.5 hover:border-border-subtle"
      >
        {account?.picture_url ? (
          <img src={account.picture_url} alt="" className="h-7 w-7 rounded-full" referrerPolicy="no-referrer" />
        ) : (
          <span className="flex h-7 w-7 items-center justify-center rounded-full bg-navy text-[10px] font-semibold text-white">
            {initials}
          </span>
        )}
        <span className="hidden text-sm font-medium text-gray-700 sm:inline">{account?.name || account?.email}</span>
        <span className="text-xs text-gray-400">▾</span>
      </button>

      {open && (
        <div className="absolute right-0 top-full z-10 mt-1 w-52 rounded-lg border border-border-subtle bg-white py-1 shadow-lg">
          <p className="truncate px-3 py-2 text-xs text-gray-500">{account?.email}</p>
          <div className="my-1 h-px bg-border-subtle" />
          <button onClick={logout} className="w-full px-3 py-2 text-left text-sm text-gray-700 hover:bg-gray-50">
            Log out
          </button>
        </div>
      )}
    </div>
  );
}

export default function Layout() {
  const { isAdmin } = useAuth();

  return (
    <div className="min-h-screen bg-app-bg">
      <header className="border-b border-border-subtle bg-white">
        <div className="mx-auto flex max-w-[1500px] items-center justify-between gap-4 px-6 py-2.5">
          <Link to="/" className="flex shrink-0 items-center gap-2">
            <span className="text-base font-extrabold tracking-tight text-navy">
              PI<span className="text-emerald-700">X</span>OUS
            </span>
            <span className="hidden text-sm font-semibold text-gray-400 md:inline">Document Intelligence</span>
          </Link>

          <nav className="flex gap-1">
            <NavLink to="/dashboard" end className={navItemClass}>
              Overview
            </NavLink>
            <NavLink to="/studio" className={navItemClass}>
              Document Studio
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

          <ProfileMenu />
        </div>
      </header>

      <main className="mx-auto max-w-[1500px] px-6 py-6">
        <Outlet />
      </main>
    </div>
  );
}
