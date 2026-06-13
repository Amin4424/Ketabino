'use client';
// File: components/shared/Header.tsx
import { useState, useSyncExternalStore, useRef, useEffect } from 'react';
import { Link } from 'next-view-transitions';
import { motion, AnimatePresence } from 'framer-motion';
import { useRouter } from 'next/navigation';
import { useTheme } from 'next-themes';
import { Search, X, Wallet, User, LogOut, BookOpen, LayoutDashboard, Moon, Sun, Bell, Home, Users, Trophy } from 'lucide-react';
import { useAuth } from '@/context/AuthContext';
import { useWallet } from '@/hooks/useWallet';
import { useNotifications } from '@/hooks/useNotifications';
import { formatCoins, formatRelativeTime } from '@/utils/format';

const subscribeToClientMount = () => () => undefined;
const getClientMountedSnapshot = () => true;
const getServerMountedSnapshot = () => false;

// Shared style for all mobile icon buttons — keeps them visually identical
const mobileIconBtn: React.CSSProperties = {
  width: 38,
  height: 38,
  // display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  borderRadius: 'var(--radius-md)',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-elevated)',
  color: 'var(--text-primary)',
  cursor: 'pointer',
  flexShrink: 0,
};

export function Header() {
  const router = useRouter();
  const { user, isAuthenticated, logout } = useAuth();
  const { wallet } = useWallet();
  const { resolvedTheme, setTheme } = useTheme();
  const { notifications, markAsRead } = useNotifications();
  const [searchQuery, setSearchQuery] = useState('');
  const [dropdownOpen, setDropdownOpen] = useState(false);
  const [notifOpen, setNotifOpen] = useState(false);
  const [mobileSearchOpen, setMobileSearchOpen] = useState(false);
  const mobileSearchInputRef = useRef<HTMLInputElement>(null);

  const mounted = useSyncExternalStore(
    subscribeToClientMount,
    getClientMountedSnapshot,
    getServerMountedSnapshot,
  );
  const isDarkTheme = mounted ? resolvedTheme === 'dark' : false;
  const nextTheme = isDarkTheme ? 'light' : 'dark';

  useEffect(() => {
    if (mobileSearchOpen) {
      setTimeout(() => mobileSearchInputRef.current?.focus(), 50);
    }
  }, [mobileSearchOpen]);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      const target = e.target as HTMLElement;
      if (!target.closest('[data-dropdown="user"]')) setDropdownOpen(false);
      if (!target.closest('[data-dropdown="notif"]')) setNotifOpen(false);
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  function handleSearch(e: React.FormEvent) {
    e.preventDefault();
    if (searchQuery.trim()) {
      router.push(`/home?search=${encodeURIComponent(searchQuery)}`);
      setMobileSearchOpen(false);
      setSearchQuery('');
    }
  }

  const unreadCount = notifications.filter(n => !n.isRead).length;

  return (
    <header className="fixed top-6 left-1/2 -translate-x-1/2 z-50 w-[90%] max-w-[900px]">
      <div
        style={{ padding: '6px 14px' }}
        className="flex items-center justify-between rounded-xl backdrop-blur-2xl bg-white/5 border border-white/10 shadow-[0_8px_32px_0_rgba(0,0,0,0.37)] relative overflow-visible"
      >

        {/* ═══════════════════════════════════════
            Mobile search overlay — covers full bar
            origin: left (search icon is on the left in mobile)
        ════════════════════════════════════════ */}
        <AnimatePresence>
          {mobileSearchOpen && (
            <motion.div
              key="mobile-search"
              initial={{ opacity: 0, scaleX: 0.92 }}
              animate={{ opacity: 1, scaleX: 1 }}
              exit={{ opacity: 0, scaleX: 0.92 }}
              transition={{ duration: 0.2, ease: 'easeOut' }}
              className="absolute inset-0 z-10 flex items-center gap-2 px-3 rounded-xl md:hidden"
              style={{
                background: 'var(--bg-elevated)',
                border: '1px solid var(--border-default)',
                transformOrigin: 'left center',
              }}
            >
              <button
                type="button"
                className="flex"
                onClick={() => { setMobileSearchOpen(false); setSearchQuery(''); }}
                style={{ ...mobileIconBtn, border: 'none', background: 'transparent', color: 'var(--text-muted)', flexShrink: 0 }}
              >
                <X size={18} />
              </button>
              <form onSubmit={handleSearch} className="flex-1">
                <input
                  ref={mobileSearchInputRef}
                  type="search"
                  placeholder="جستجوی کتاب یا نویسنده…"
                  value={searchQuery}
                  onChange={e => setSearchQuery(e.target.value)}
                  className="w-full bg-transparent outline-none"
                  style={{
                    color: 'var(--text-primary)',
                    fontSize: '0.9rem',
                    fontFamily: 'inherit',
                    border: 'none',
                  }}
                />
              </form>
              <Search size={16} style={{ color: 'var(--text-muted)', flexShrink: 0 }} />
            </motion.div>
          )}
        </AnimatePresence>

        {/* ═══════════════════════════════════════
            MOBILE layout  (< md)
            [Search]  ···gap···  [Home] [Bell] [User]
        ════════════════════════════════════════ */}

        {/* Search icon — far RIGHT on mobile */}
        <button
          type="button"
          onClick={() => setMobileSearchOpen(true)}
          className="flex md:hidden"
          style={mobileIconBtn}
          aria-label="جستجو"
        >
          <Search size={17} />
        </button>

        {/* ═══════════════════════════════════════
            DESKTOP layout  (md+)
            [Logo]  [SearchBar]  ···  [Theme][Bell][Wallet][User]
        ════════════════════════════════════════ */}

        {/* Logo — desktop only */}
        <Link
          href="/home"
          className="hidden md:flex"
          style={{ paddingLeft: 8, alignItems: 'center', gap: 8, textDecoration: 'none', flexShrink: 0 }}
        >
          <div style={{ width: 36, height: 36, borderRadius: '50%', background: 'linear-gradient(135deg, #C9A84C, #8B5CF6)', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 18 }}>
            📚
          </div>
          <span className="text-gradient-gold" style={{ fontWeight: 700, fontSize: '1.1rem' }}>کتابینو</span>
        </Link>

        {/* Desktop search bar */}
        <form
          onSubmit={handleSearch}
          className="hidden md:block"
          style={{ paddingLeft: 8, flex: 1, maxWidth: 420 }}
        >
          <div style={{ position: 'relative' }}>
            <Search size={16} style={{ position: 'absolute', top: '50%', right: 12, transform: 'translateY(-50%)', color: 'var(--text-muted)' }} />
            <input
              type="search"
              placeholder="جستجوی کتاب یا نویسنده…"
              value={searchQuery}
              onChange={e => setSearchQuery(e.target.value)}
              style={{
                width: '100%', padding: '9px 40px 9px 14px',
                background: 'var(--bg-elevated)',
                border: '1px solid var(--border-default)',
                borderRadius: 'var(--radius-md)',
                color: 'var(--text-primary)',
                fontSize: '0.9rem',
                outline: 'none',
                fontFamily: 'inherit',
              }}
            />
          </div>
        </form>

        {/* ═══════════════════════════════════════
            Right-side actions
            Desktop: theme + bell + wallet + user
            Mobile:  home + bell + user  (theme is in user dropdown)
        ════════════════════════════════════════ */}
        <div style={{ marginRight: 'auto', display: 'flex', alignItems: 'center', gap: 8 }}>

          {/* Theme toggle — desktop only */}
          <button
            type="button"
            onClick={() => setTheme(nextTheme)}
            disabled={!mounted}
            aria-label={isDarkTheme ? 'تغییر به تم روشن' : 'تغییر به تم تاریک'}
            className="hidden md:inline-flex"
            style={{
              width: 38, height: 38,
              alignItems: 'center', justifyContent: 'center',
              borderRadius: 'var(--radius-md)',
              border: '1px solid var(--border-default)',
              background: 'var(--bg-elevated)',
              color: 'var(--text-primary)',
              cursor: mounted ? 'pointer' : 'default',
              opacity: mounted ? 1 : 0.65,
              boxShadow: 'var(--shadow-card)',
              transition: 'transform 0.15s ease, border-color 0.15s ease, background 0.15s ease',
              flexShrink: 0,
            }}
            onMouseEnter={e => {
              if (!mounted) return;
              e.currentTarget.style.transform = 'translateY(-1px)';
              e.currentTarget.style.borderColor = 'var(--accent-gold-dim)';
              e.currentTarget.style.background = 'var(--bg-card)';
            }}
            onMouseLeave={e => {
              e.currentTarget.style.transform = 'translateY(0)';
              e.currentTarget.style.borderColor = 'var(--border-default)';
              e.currentTarget.style.background = 'var(--bg-elevated)';
            }}
          >
            {mounted && isDarkTheme ? (
              <Sun size={18} style={{ color: 'var(--accent-gold)' }} />
            ) : (
              <Moon size={18} style={{ color: 'var(--accent-violet)' }} />
            )}
          </button>

          {/* Home icon — mobile only */}
          <Link
            href="/home"
            className="flex md:hidden"
            style={{ ...mobileIconBtn, textDecoration: 'none' }}
            aria-label="خانه"
          >
            <Home size={17} />
          </Link>

          {isAuthenticated ? (
            <>
              {/* Notification bell */}
              <div style={{ position: 'relative', flexShrink: 0 }} data-dropdown="notif">
                <button
                  className="flex"
                  onClick={() => { setNotifOpen(o => !o); setDropdownOpen(false); }}
                  style={mobileIconBtn}
                >
                  <Bell size={17} />
                  {unreadCount > 0 && (
                    <span style={{
                      position: 'absolute', top: -4, right: -4,
                      background: 'var(--accent-rose)', color: '#fff',
                      fontSize: '0.65rem', fontWeight: 800,
                      padding: '2px 5px', borderRadius: 10,
                      pointerEvents: 'none',
                    }}>
                      {unreadCount}
                    </span>
                  )}
                </button>

                <AnimatePresence>
                  {notifOpen && (
                    <motion.div
                      initial={{ opacity: 0, y: -8, scale: 0.95 }}
                      animate={{ opacity: 1, y: 0, scale: 1 }}
                      exit={{ opacity: 0, y: -8, scale: 0.95 }}
                      transition={{ duration: 0.18 }}
                      style={{
                        position: 'absolute', top: '100%', left: -50, marginTop: 8,
                        background: 'var(--bg-elevated)', border: '1px solid var(--border-default)',
                        borderRadius: 'var(--radius-md)', padding: 8,
                        width: 280, maxHeight: 350, overflowY: 'auto',
                        boxShadow: 'var(--shadow-card)', zIndex: 200,
                      }}
                    >
                      <h4 style={{ padding: '8px 12px', margin: 0, fontSize: '0.9rem', borderBottom: '1px solid var(--border-subtle)' }}>اعلان‌ها</h4>
                      {notifications.length === 0 ? (
                        <p style={{ padding: '20px 12px', textAlign: 'center', color: 'var(--text-muted)', fontSize: '0.85rem' }}>هیچ اعلانی ندارید.</p>
                      ) : (
                        notifications.map(n => (
                          <div
                            key={n.id}
                            onClick={() => !n.isRead && markAsRead(n.id)}
                            style={{
                              padding: 12, borderBottom: '1px solid var(--border-subtle)',
                              cursor: n.isRead ? 'default' : 'pointer',
                              background: n.isRead ? 'transparent' : 'rgba(201,168,76,0.05)',
                              opacity: n.isRead ? 0.7 : 1,
                            }}
                          >
                            <p style={{ fontSize: '0.85rem', fontWeight: n.isRead ? 400 : 700, marginBottom: 4, display: 'flex', justifyContent: 'space-between' }}>
                              <span>{n.title}</span>
                              {!n.isRead && <span style={{ width: 8, height: 8, borderRadius: '50%', background: 'var(--accent-gold)', flexShrink: 0, display: 'inline-block' }} />}
                            </p>
                            <p style={{ fontSize: '0.8rem', color: 'var(--text-secondary)' }}>{n.message}</p>
                            <span style={{ fontSize: '0.7rem', color: 'var(--text-muted)', marginTop: 4, display: 'block' }}>{formatRelativeTime(n.createdAt)}</span>
                          </div>
                        ))
                      )}
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>

              {/* Wallet pill — desktop only */}
              {wallet && (
                <Link href="/profile" className="hidden md:flex" style={{ textDecoration: 'none' }}>
                  <div style={{
                    display: 'flex', alignItems: 'center', gap: 6,
                    background: 'var(--accent-gold-glow)',
                    border: '1px solid var(--accent-gold-dim)',
                    borderRadius: 'var(--radius-md)',
                    padding: '6px 12px', cursor: 'pointer', flexShrink: 0,
                  }}>
                    <Wallet size={14} style={{ color: 'var(--accent-gold)' }} />
                    <span style={{ color: 'var(--accent-gold)', fontSize: '0.85rem', fontWeight: 600 }}>
                      {formatCoins(wallet.balance)}
                    </span>
                  </div>
                </Link>
              )}

              {/* User dropdown */}
              <div style={{ position: 'relative', flexShrink: 0 }} data-dropdown="user">
                <button
                  onClick={() => { setDropdownOpen(o => !o); setNotifOpen(false); }}
                  // On mobile use the same uniform icon style; on desktop keep the wider pill
                  className="flex md:hidden"
                  style={mobileIconBtn}
                  aria-label="پروفایل"
                >
                  <User size={17} />
                </button>
                {/* Desktop user button (wider pill with name) */}
                <button
                  onClick={() => { setDropdownOpen(o => !o); setNotifOpen(false); }}
                  className="hidden md:flex"
                  style={{
                    alignItems: 'center', gap: 8,
                    background: 'var(--bg-elevated)',
                    border: '1px solid var(--border-default)',
                    borderRadius: 'var(--radius-md)',
                    padding: '7px 14px',
                    cursor: 'pointer', color: 'var(--text-primary)',
                    fontFamily: 'inherit', fontSize: '0.9rem', flexShrink: 0,
                  }}
                >
                  <User size={15} />
                  <span>{user?.name}</span>
                </button>

                <AnimatePresence>
                  {dropdownOpen && (
                    <motion.div
                      initial={{ opacity: 0, y: -8, scale: 0.95 }}
                      animate={{ opacity: 1, y: 0, scale: 1 }}
                      exit={{ opacity: 0, y: -8, scale: 0.95 }}
                      transition={{ duration: 0.18 }}
                      style={{
                        position: 'absolute', top: '100%', left: 0, marginTop: 8,
                        background: 'var(--bg-elevated)',
                        border: '1px solid var(--border-default)',
                        borderRadius: 'var(--radius-md)',
                        padding: 8, minWidth: 190,
                        boxShadow: 'var(--shadow-card)', zIndex: 200,
                      }}
                    >
                      {/* ── Mobile-only items inside dropdown ── */}

                      {/* Wallet row — mobile only */}
                      {wallet && (
                        <Link
                          href="/profile"
                          onClick={() => setDropdownOpen(false)}
                          className="md:hidden"
                          style={{ textDecoration: 'none', display: 'block', marginBottom: 4 }}
                        >
                          <div style={{
                            display: 'flex', alignItems: 'center', gap: 8,
                            padding: '9px 12px', borderRadius: 'var(--radius-sm)',
                            background: 'var(--accent-gold-glow)',
                            border: '1px solid var(--accent-gold-dim)',
                          }}>
                            <Wallet size={15} style={{ color: 'var(--accent-gold)' }} />
                            <span style={{ color: 'var(--accent-gold)', fontSize: '0.85rem', fontWeight: 600 }}>
                              {formatCoins(wallet.balance)}
                            </span>
                          </div>
                        </Link>
                      )}

                      {/* Theme toggle row — mobile only */}
                      <button
                        type="button"
                        onClick={() => { if (mounted) setTheme(nextTheme); }}
                        disabled={!mounted}
                        className="md:hidden"
                        style={{
                          display: 'flex', alignItems: 'center', gap: 10,
                          padding: '9px 12px', borderRadius: 'var(--radius-sm)',
                          color: 'var(--text-secondary)', background: 'none', border: 'none',
                          cursor: mounted ? 'pointer' : 'default', width: '100%',
                          fontFamily: 'inherit', fontSize: '0.9rem',
                          marginBottom: 4,
                          opacity: mounted ? 1 : 0.65,
                        }}
                        onMouseEnter={e => (e.currentTarget.style.background = 'var(--bg-card)')}
                        onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                      >
                        {mounted && isDarkTheme
                          ? <Sun size={15} style={{ color: 'var(--accent-gold)' }} />
                          : <Moon size={15} style={{ color: 'var(--accent-violet)' }} />
                        }
                        {mounted && isDarkTheme ? 'تم روشن' : 'تم تاریک'}
                      </button>

                      <div className="md:hidden" style={{ margin: '4px 0 8px', borderTop: '1px solid var(--border-subtle)' }} />

                      {/* Common items (both mobile and desktop) */}
                      {[
                        { icon: <BookOpen size={15} />, label: 'کتابخانه من', href: '/profile?tab=library' },
                        { icon: <Users size={15} />, label: 'حلقه‌های کتابخوانی', href: '/book-clubs' },
                        { icon: <Trophy size={15} />, label: 'چالش‌های مطالعه', href: '/challenges' },
                        ...(user?.role === 'Author' ? [{ icon: <LayoutDashboard size={15} />, label: 'استودیوی نویسنده', href: '/author' }] : []),
                        ...(user?.role === 'Admin' ? [{ icon: <LayoutDashboard size={15} />, label: 'پنل مدیریت', href: '/admin' }] : []),
                      ].map(item => (
                        <Link
                          key={item.href}
                          href={item.href}
                          onClick={() => setDropdownOpen(false)}
                          style={{
                            display: 'flex', alignItems: 'center', gap: 10,
                            padding: '9px 12px', borderRadius: 'var(--radius-sm)',
                            color: 'var(--text-secondary)', textDecoration: 'none',
                            transition: 'background 0.15s',
                          }}
                          onMouseEnter={e => (e.currentTarget.style.background = 'var(--bg-card)')}
                          onMouseLeave={e => (e.currentTarget.style.background = 'transparent')}
                        >
                          {item.icon}{item.label}
                        </Link>
                      ))}

                      <div style={{ margin: '6px 0', borderTop: '1px solid var(--border-subtle)' }} />

                      <button
                        onClick={() => { logout(); setDropdownOpen(false); router.push('/login'); }}
                        style={{
                          display: 'flex', alignItems: 'center', gap: 10,
                          padding: '9px 12px', borderRadius: 'var(--radius-sm)',
                          color: 'var(--accent-rose)', background: 'none', border: 'none',
                          cursor: 'pointer', width: '100%',
                          fontFamily: 'inherit', fontSize: '0.9rem',
                        }}
                      >
                        <LogOut size={15} />خروج
                      </button>
                    </motion.div>
                  )}
                </AnimatePresence>
              </div>
            </>
          ) : (
            <Link href="/login" style={{
              padding: '8px 18px',
              background: 'linear-gradient(135deg, #C9A84C, #8A6F2E)',
              borderRadius: 'var(--radius-md)', color: '#0A0A0F',
              fontWeight: 700, textDecoration: 'none', fontSize: '0.9rem',
              flexShrink: 0,
            }}>
              ورود
            </Link>
          )}
        </div>
      </div>
    </header>
  );
}
