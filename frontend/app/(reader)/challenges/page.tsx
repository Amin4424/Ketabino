'use client';

import { useState, useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Trophy, Award, CheckCircle, Circle, Play, Coins, AlertCircle, Flag } from 'lucide-react';
import { api } from '@/lib/api';
import { useWallet } from '@/hooks/useWallet';
import { useNotifications } from '@/hooks/useNotifications';
import { Button } from '@/components/ui/Button';
import { Badge } from '@/components/ui/Badge';
import { formatCoins } from '@/utils/format';

interface Challenge {
  id: number;
  title: string;
  description: string;
  targetType: string;
  targetCount: number;
  coinReward: number;
  endDate: string;
  currentProgress: number;
  isCompleted: boolean;
  claimedAt: string | null;
  joined: boolean;
}

export default function ChallengesPage() {
  const { wallet, refetch: refetchWallet } = useWallet();
  const { refetch: refetchNotifications } = useNotifications();
  const [challenges, setChallenges] = useState<Challenge[]>([]);
  const [loading, setLoading] = useState(true);
  const [actionLoading, setActionLoading] = useState<number | null>(null);
  const [toast, setToast] = useState<{ message: string; type: 'success' | 'error' } | null>(null);

  useEffect(() => {
    fetchChallenges();
  }, []);

  async function fetchChallenges() {
    try {
      setLoading(true);
      const data = await api.get<Challenge[]>('/challenge');
      setChallenges(data);
    } catch (err) {
      showToast('خطا در دریافت لیست چالش‌ها', 'error');
    } finally {
      setLoading(false);
    }
  }

  function showToast(message: string, type: 'success' | 'error') {
    setToast({ message, type });
    setTimeout(() => setToast(null), 3000);
  }

  async function handleJoin(id: number) {
    try {
      setActionLoading(id);
      const res = await api.post<{ message: string }>(`/challenge/${id}/join`, {});
      showToast(res.message, 'success');
      await fetchChallenges();
    } catch (err: any) {
      showToast(err.message || 'خطا در عضویت در چالش', 'error');
    } finally {
      setActionLoading(null);
    }
  }

  async function handleTestProgress(id: number) {
    try {
      setActionLoading(id);
      const res = await api.post<{ message: string; progress: number; isCompleted: boolean }>(`/challenge/${id}/progress`, 1);
      showToast(res.message, 'success');
      await fetchChallenges();
    } catch (err: any) {
      showToast(err.message || 'خطا در بروزرسانی پیشرفت', 'error');
    } finally {
      setActionLoading(null);
    }
  }

  async function handleClaim(id: number) {
    try {
      setActionLoading(id);
      const res = await api.post<{ message: string; reward: number }>(`/challenge/${id}/claim`, {});
      showToast(res.message, 'success');
      await fetchChallenges();
      await refetchWallet(); // refresh coins in header & page
      await refetchNotifications(); // refresh notifications bell
    } catch (err: any) {
      showToast(err.message || 'خطا در دریافت جایزه', 'error');
    } finally {
      setActionLoading(null);
    }
  }

  async function handleDone(id: number) {
    try {
      setActionLoading(id);
      const res = await api.post<{ message: string }>(`/challenge/${id}/done`, {});
      showToast(res.message, 'success');
      await fetchChallenges();
      await refetchNotifications(); // notification will be sent from backend
    } catch (err: any) {
      showToast(err.message || 'خطا در ثبت انجام چالش', 'error');
    } finally {
      setActionLoading(null);
    }
  }

  const containerVariants = {
    hidden: { opacity: 0 },
    show: {
      opacity: 1,
      transition: { staggerChildren: 0.1 }
    }
  };

  const itemVariants = {
    hidden: { opacity: 0, y: 20 },
    show: { opacity: 1, y: 0 }
  };

  return (
    <div style={{ maxWidth: 900, margin: '110px auto 40px', padding: '0 20px', direction: 'rtl' }}>
      
      {/* Toast Notification */}
      <AnimatePresence>
        {toast && (
          <motion.div
            initial={{ opacity: 0, y: 50, scale: 0.9 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 50, scale: 0.9 }}
            style={{
              position: 'fixed',
              bottom: 24,
              left: 24,
              zIndex: 999,
              background: toast.type === 'success' ? 'var(--accent-emerald-glow)' : 'var(--accent-rose-glow)',
              border: `1.5px solid ${toast.type === 'success' ? 'var(--accent-emerald)' : 'var(--accent-rose)'}`,
              borderRadius: 'var(--radius-lg)',
              padding: '12px 24px',
              color: toast.type === 'success' ? 'var(--accent-emerald)' : 'var(--accent-rose)',
              fontWeight: 600,
              boxShadow: '0 8px 32px rgba(0,0,0,0.35)',
              display: 'flex',
              alignItems: 'center',
              gap: 8,
              fontSize: '0.92rem'
            }}
          >
            {toast.type === 'success' ? <CheckCircle size={18} /> : <AlertCircle size={18} />}
            {toast.message}
          </motion.div>
        )}
      </AnimatePresence>

      {/* Header section */}
      <div style={{ textAlign: 'center', marginBottom: 40 }}>
        <motion.div
          initial={{ scale: 0.8, opacity: 0 }}
          animate={{ scale: 1, opacity: 1 }}
          transition={{ duration: 0.5 }}
          style={{
            width: 72,
            height: 72,
            borderRadius: '50%',
            background: 'linear-gradient(135deg, var(--accent-gold), #dfb84c)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            margin: '0 auto 16px',
            boxShadow: '0 8px 32px rgba(201,168,76,0.3)'
          }}
        >
          <Trophy size={36} style={{ color: '#0A0A0F' }} />
        </motion.div>
        <motion.h1
          initial={{ opacity: 0, y: -10 }}
          animate={{ opacity: 1, y: 0 }}
          style={{ fontSize: '1.9rem', fontWeight: 850, marginBottom: 8 }}
          className="text-gradient-gold"
        >
          چالش‌های مطالعه کتابینو
        </motion.h1>
        <motion.p
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          style={{ color: 'var(--text-secondary)', fontSize: '0.95rem', maxWidth: 500, margin: '0 auto', lineHeight: 1.7 }}
        >
          با پیوستن به چالش‌های فعال کتابخوانی و بالا بردن میزان مطالعه خود، سکه رایگان پاداش بگیرید و فصول بعدی را باز کنید!
        </motion.p>
      </div>

      {loading ? (
        <div style={{ display: 'flex', justifyContent: 'center', padding: '60px 0' }}>
          <motion.div
            animate={{ rotate: 360 }}
            transition={{ duration: 1, repeat: Infinity, ease: 'linear' }}
            style={{ width: 32, height: 32, border: '3px solid var(--border-default)', borderTopColor: 'var(--accent-gold)', borderRadius: '50%' }}
          />
        </div>
      ) : (
        <motion.div
          variants={containerVariants}
          initial="hidden"
          animate="show"
          style={{ display: 'flex', flexDirection: 'column', gap: 16 }}
        >
          {challenges.length === 0 ? (
            <div style={{ textAlign: 'center', padding: '48px 24px', background: 'var(--bg-card)', borderRadius: 'var(--radius-lg)', border: '1px solid var(--border-default)' }}>
              <Award size={36} style={{ color: 'var(--text-muted)', marginBottom: 8 }} />
              <p style={{ color: 'var(--text-secondary)' }}>در حال حاضر چالش فعالی وجود ندارد.</p>
            </div>
          ) : (
            challenges.map((c) => {
              const joined = c.joined || c.currentProgress > 0 || c.claimedAt !== null;
              const hasClaimed = c.claimedAt !== null;
              const percent = Math.min(Math.round((c.currentProgress / c.targetCount) * 100), 100);

              return (
                <motion.div
                  key={c.id}
                  variants={itemVariants}
                  style={{
                    background: 'var(--bg-card)',
                    border: '1px solid var(--border-default)',
                    borderRadius: 'var(--radius-lg)',
                    padding: '20px 24px',
                    display: 'flex',
                    flexDirection: 'column',
                    gap: 16,
                    position: 'relative',
                    overflow: 'hidden'
                  }}
                >
                  {/* Card Header */}
                  <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                    <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                      {hasClaimed ? (
                        <CheckCircle size={22} style={{ color: 'var(--accent-emerald)' }} />
                      ) : c.isCompleted ? (
                        <Award size={22} style={{ color: 'var(--accent-gold)' }} />
                      ) : joined ? (
                        <Play size={18} style={{ color: 'var(--accent-violet)' }} />
                      ) : (
                        <Circle size={18} style={{ color: 'var(--text-muted)' }} />
                      )}
                      <div>
                        <h3 style={{ fontWeight: 800, fontSize: '1.05rem', margin: 0 }}>{c.title}</h3>
                        <p style={{ color: 'var(--text-secondary)', fontSize: '0.82rem', margin: '4px 0 0' }}>
                          پایان چالش: {new Date(c.endDate).toLocaleDateString('fa-IR')}
                        </p>
                      </div>
                    </div>
                    <div className="flex items-center gap-3 self-start sm:self-auto">
                      <div style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: 6,
                        background: 'var(--accent-gold-glow)',
                        border: '1px solid var(--accent-gold-dim)',
                        borderRadius: 'var(--radius-md)',
                        padding: '4px 10px',
                        fontSize: '0.85rem',
                        fontWeight: 700,
                        color: 'var(--accent-gold)'
                      }}>
                        <Coins size={14} />
                        <span>{c.coinReward} سکه پاداش</span>
                      </div>
                      <Badge variant={hasClaimed ? 'emerald' : c.isCompleted ? 'gold' : joined ? 'default' : 'violet'}>
                        {hasClaimed ? 'دریافت شده' : c.isCompleted ? 'آماده دریافت' : joined ? 'در حال انجام' : 'شرکت نکرده'}
                      </Badge>
                    </div>
                  </div>

                  {/* Description */}
                  <p style={{ color: 'var(--text-secondary)', fontSize: '0.88rem', lineHeight: 1.6, margin: 0 }}>
                    {c.description}
                  </p>

                  {/* Progress Section */}
                  {joined && (
                    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
                      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: '0.82rem', color: 'var(--text-secondary)' }}>
                        <span>میزان پیشرفت: {c.currentProgress} از {c.targetCount} ({percent}٪)</span>
                        {c.isCompleted && !hasClaimed && <span style={{ color: 'var(--accent-gold)', fontWeight: 600 }}>تبریک! چالش به پایان رسید.</span>}
                      </div>
                      <div style={{ height: 8, background: 'var(--bg-elevated)', borderRadius: 4, overflow: 'hidden' }}>
                        <motion.div
                          initial={{ width: 0 }}
                          animate={{ width: `${percent}%` }}
                          transition={{ duration: 0.6, ease: 'easeOut' }}
                          style={{
                            height: '100%',
                            background: c.isCompleted 
                              ? 'linear-gradient(90deg, var(--accent-gold), #dfb84c)' 
                              : 'linear-gradient(90deg, var(--accent-violet), #a78bfa)'
                          }}
                        />
                      </div>
                    </div>
                  )}

                  {/* Card Actions */}
                  <div style={{ display: 'flex', gap: 8, marginTop: 4 }} className="flex-col sm:flex-row">
                    {!joined ? (
                      <Button
                        onClick={() => handleJoin(c.id)}
                        disabled={actionLoading === c.id}
                        className="w-full sm:w-auto"
                      >
                        {actionLoading === c.id ? 'در حال ثبت…' : 'شرکت در چالش'}
                      </Button>
                    ) : !c.isCompleted ? (
                      <>
                        <Button
                          variant="outline"
                          onClick={() => handleTestProgress(c.id)}
                          disabled={actionLoading === c.id}
                          className="w-full sm:w-auto"
                        >
                          {actionLoading === c.id ? 'در حال ثبت…' : 'ثبت پیشرفت (تست +۱)'}
                        </Button>
                        <Button
                          onClick={() => handleDone(c.id)}
                          disabled={actionLoading === c.id}
                          className="w-full sm:w-auto"
                          style={{
                            background: 'linear-gradient(135deg, var(--accent-violet), #7c3aed)',
                            color: '#fff',
                            fontWeight: 700
                          }}
                        >
                          <Flag size={15} />
                          {actionLoading === c.id ? 'در حال ثبت…' : '✅ انجام دادم'}
                        </Button>
                        <p style={{ fontSize: '0.78rem', color: 'var(--text-muted)', alignSelf: 'center', margin: 0 }} className="text-center sm:text-right">
                          (برای شبیه‌سازی پیشرفت از «ثبت پیشرفت» و برای اعلام اتمام از «انجام دادم» استفاده کنید)
                        </p>
                      </>
                    ) : !hasClaimed ? (
                      <Button
                        onClick={() => handleClaim(c.id)}
                        disabled={actionLoading === c.id}
                        className="w-full sm:w-auto"
                        style={{
                          background: 'linear-gradient(135deg, var(--accent-gold), #dfb84c)',
                          color: '#0A0A0F',
                          fontWeight: 700
                        }}
                      >
                        {actionLoading === c.id ? 'در حال دریافت…' : 'دریافت سکه‌های پاداش چالش'}
                      </Button>
                    ) : (
                      <Button
                        disabled
                        variant="ghost"
                        className="w-full sm:w-auto"
                        style={{ color: 'var(--accent-emerald)' }}
                      >
                        سکه پاداش با موفقیت دریافت شد
                      </Button>
                    )}
                  </div>
                </motion.div>
              );
            })
          )}
        </motion.div>
      )}
    </div>
  );
}
