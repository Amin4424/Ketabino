'use client';
// File: app/(author)/author/page.tsx
import { useEffect } from 'react';
import { motion } from 'framer-motion';
import { Link } from 'next-view-transitions';
import { useRouter } from 'next/navigation';
import { LayoutDashboard, BookOpen, TrendingUp, Star, Heart, Coins, Plus, Eye, Pencil } from 'lucide-react';
import { useAuthorStudio } from '@/hooks/useAuthorStudio';
import { useAuth } from '@/context/AuthContext';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { Skeleton } from '@/components/ui/Skeleton';
import { formatCoins, formatRelativeTime } from '@/utils/format';

export default function AuthorStudioPage() {
  const router = useRouter();
  const { user, isAuthenticated, isLoading: authLoading } = useAuth();
  const { books, stats, isLoading } = useAuthorStudio();

  useEffect(() => {
    if (!authLoading) {
      if (!isAuthenticated || (user && user.role !== 'Author' && user.role !== 'Admin')) {
        router.push('/home');
      }
    }
  }, [authLoading, isAuthenticated, user, router]);

  if (authLoading || isLoading) {
    return (
      <div style={{ maxWidth: 1100, margin: '0 auto', padding: '12px 20px 32px' }}>
        <Skeleton className="h-10 w-48 mb-8" />
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 14, marginBottom: 36 }}>
          {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-24 w-full" />)}
        </div>
      </div>
    );
  }

  if (!isAuthenticated || (user && user.role !== 'Author' && user.role !== 'Admin')) {
    return null;
  }

  const statCards = stats ? [
    { icon: <BookOpen size={20} />, label: 'کتاب‌ها', value: stats.totalBooks, color: '#C9A84C' },
    { icon: <TrendingUp size={20} />, label: 'فروش فصل', value: stats.totalPurchases, color: '#8B5CF6' },
    { icon: <Star size={20} />, label: 'میانگین امتیاز', value: stats.averageRating.toFixed(1), color: '#10B981' },
    { icon: <Heart size={20} />, label: 'لایک‌ها', value: stats.totalLikes, color: '#F43F5E' },
    { icon: '🪙', label: 'درآمد کل', value: formatCoins(stats.totalCoinsEarned), color: '#C9A84C', wide: true },
  ] : [];

  return (
    <div style={{ maxWidth: 1100, margin: '0 auto', padding: '12px 20px 32px' }}>

      {/* Header */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 36 }}>
        <div>
          <h1 style={{ fontWeight: 900, fontSize: '1.6rem', marginBottom: 4 }}>
            <span className="text-gradient-gold">استودیوی نویسنده</span>
          </h1>
          <p style={{ color: 'var(--text-secondary)', fontSize: '0.9rem' }}>خوش آمدید، {user?.name}</p>
        </div>
        <Button onClick={() => router.push('/author/books/new')}>
          <Plus size={16} />کتاب جدید
        </Button>
      </div>

      {/* Stats Grid */}
      {isLoading ? (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 14, marginBottom: 36 }}>
          {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-24 w-full" />)}
        </div>
      ) : (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(auto-fill, minmax(180px, 1fr))', gap: 14, marginBottom: 36 }}>
          {statCards.map((s, i) => (
            <motion.div key={i} initial={{ opacity: 0, y: 16 }} animate={{ opacity: 1, y: 0 }} transition={{ delay: i * 0.07 }}
              style={{ background: 'var(--bg-card)', border: '1px solid var(--border-default)', borderRadius: 'var(--radius-lg)', padding: '20px 18px' }}>
              <div style={{ color: s.color, marginBottom: 8, display: 'flex' }}>
                {typeof s.icon === 'string' ? <span style={{ fontSize: 20 }}>{s.icon}</span> : s.icon}
              </div>
              <p style={{ fontSize: '1.5rem', fontWeight: 900, color: s.color }}>{s.value}</p>
              <p style={{ color: 'var(--text-secondary)', fontSize: '0.82rem', marginTop: 4 }}>{s.label}</p>
            </motion.div>
          ))}
        </div>
      )}

      {/* Books List */}
      <h2 style={{ fontWeight: 700, fontSize: '1.1rem', marginBottom: 16 }}>کتاب‌های من</h2>
      {isLoading ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 w-full" />)}
        </div>
      ) : books.length === 0 ? (
        <div style={{ textAlign: 'center', padding: '60px 20px', background: 'var(--bg-card)', borderRadius: 'var(--radius-xl)', border: '1px solid var(--border-default)' }}>
          <div style={{ fontSize: 48, marginBottom: 12 }}>📚</div>
          <p style={{ color: 'var(--text-secondary)', marginBottom: 16 }}>هنوز کتابی ایجاد نکرده‌اید</p>
          <Button onClick={() => router.push('/author/books/new')}><Plus size={14} />اولین کتاب خود را بنویسید</Button>
        </div>
      ) : (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 12 }}>
          {books.map((book, i) => (
            <motion.div key={book.id} initial={{ opacity: 0, x: -10 }} animate={{ opacity: 1, x: 0 }} transition={{ delay: i * 0.05 }}
              style={{ display: 'flex', alignItems: 'center', gap: 16, padding: '16px 20px', background: 'var(--bg-card)', border: '1px solid var(--border-subtle)', borderRadius: 'var(--radius-lg)' }}>
              
              {/* Cover */}
              <div style={{ width: 52, height: 70, borderRadius: 'var(--radius-sm)', background: 'var(--bg-elevated)', overflow: 'hidden', flexShrink: 0 }}>
                {book.coverImage ? <img src={book.coverImage} alt={book.title} style={{ width: '100%', height: '100%', objectFit: 'cover' }} /> : <div style={{ width: '100%', height: '100%', display: 'flex', alignItems: 'center', justifyContent: 'center', fontSize: 22 }}>📖</div>}
              </div>

              <div style={{ flex: 1 }}>
                <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 4 }}>
                  <h3 style={{ fontWeight: 700, fontSize: '0.95rem' }}>{book.title}</h3>
                  <Badge variant={book.status === 'Published' ? 'emerald' : book.status === 'Draft' ? 'default' : 'rose'}>
                    {book.status === 'Published' ? 'منتشر شده' : book.status === 'Draft' ? 'پیش‌نویس' : 'آرشیو'}
                  </Badge>
                </div>
                <div style={{ display: 'flex', gap: 16, color: 'var(--text-muted)', fontSize: '0.8rem' }}>
                  <span><BookOpen size={12} style={{ display: 'inline' }} /> {book.chaptersCount} فصل</span>
                  <span><Heart size={12} style={{ display: 'inline' }} /> {book.likesCount}</span>
                  <span><Star size={12} style={{ display: 'inline' }} /> {book.averageRating.toFixed(1)}</span>
                </div>
              </div>

              <div style={{ display: 'flex', gap: 8 }}>
                <Link href={`/author/books/${book.id}/edit`}>
                  <Button variant="ghost" size="sm"><Pencil size={14} />ویرایش</Button>
                </Link>
                <Link href={`/books/${book.id}`}>
                  <Button variant="ghost" size="sm"><Eye size={14} />مشاهده</Button>
                </Link>
              </div>
            </motion.div>
          ))}
        </div>
      )}
    </div>
  );
}
