// 讓桌面上的圖示「動起來」：隨機跳動 / 互相換位 / 波浪，但整體形狀維持不變
//  動畫在自己的執行緒上跑（不受主視窗忙碌影響），用精準計時每秒 45～60 格
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace DesktopColorSorter
{
    sealed class PatternAnimator : IDisposable
    {
        public static readonly string[] Modes = { "關閉", "隨機跳動", "互相換位", "波浪" };
        public static readonly string[] Speeds = { "慢", "中", "快" };

        class Hop { public int I; public double T0, Dur; public float Height; }
        class Swap { public int A, B; public double T0, Dur; }

        readonly int mode;
        readonly double speed;
        readonly HashSet<string> onlyIds;
        readonly Thread thread;
        readonly ManualResetEvent ready = new ManualResetEvent(false);
        volatile bool stopRequested;
        string startError;
        bool disposed;
        public event Action<string> Failed;
        public int Count { get; private set; }

        // 以下只在動畫執行緒上使用
        Desktop desktop;
        readonly List<DesktopItem> items = new List<DesktopItem>();
        readonly List<POINT> home = new List<POINT>();   // 每個圖示目前「該在」的位置（換位模式會一直變）
        POINT[] origin;                                   // 動畫開始時的位置，停止時放回這裡
        POINT[] shown;                                    // 目前實際顯示的位置
        uint savedFlags;
        readonly Random rnd = new Random();
        readonly List<Hop> hops = new List<Hop>();
        readonly List<Swap> swaps = new List<Swap>();
        double nextEvent;

        // onlyIds：只讓這些圖示動（例如剛排好的圖案）；空的 = 桌面上全部
        public PatternAnimator(int mode, int speedLevel, ICollection<string> onlyIds)
        {
            this.mode = mode;
            speed = speedLevel == 0 ? 0.6 : speedLevel == 2 ? 1.6 : 1.0;
            this.onlyIds = onlyIds == null ? null : new HashSet<string>(onlyIds, StringComparer.OrdinalIgnoreCase);
            thread = new Thread(Run) { IsBackground = true, Name = "DesktopAnimation" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            ready.WaitOne(8000);
            if (startError != null) throw new InvalidOperationException(startError);
        }

        void Run()
        {
            try
            {
                desktop = Desktop.Open();
                desktop.LoadItems(false);
                foreach (DesktopItem it in desktop.Items)
                    if (onlyIds == null || onlyIds.Count == 0 || onlyIds.Contains(it.ParsingName)) { items.Add(it); home.Add(it.Pos); }
                shown = home.ToArray();
                origin = home.ToArray();
                savedFlags = desktop.Flags;
                // 動畫需要把圖示放在格線之間，先暫時關掉「自動排列」和「對齊格線」
                desktop.SetFlags(Desktop.FWF_AUTOARRANGE | Desktop.FWF_SNAPTOGRID, 0);
                Count = items.Count;
            }
            catch (Exception ex)
            {
                startError = ex.Message;
                if (desktop != null) desktop.Dispose();
                ready.Set();
                return;
            }
            ready.Set();

            string failure = null;
            Native.timeBeginPeriod(1);
            try
            {
                Stopwatch clock = Stopwatch.StartNew();
                double frameMs = mode == 3 ? 1000.0 / 45 : 1000.0 / 60; // 波浪要移動全部圖示，每秒 45 格就夠順
                double next = 0;
                while (!stopRequested && items.Count > 0)
                {
                    try { Step(clock.Elapsed.TotalMilliseconds); }
                    catch (Exception ex) { failure = ex.Message; break; } // 桌面被重新整理、檔案總管重新啟動等
                    next += frameMs;
                    double wait = next - clock.Elapsed.TotalMilliseconds;
                    if (wait > 0) Thread.Sleep((int)wait);
                    else next = clock.Elapsed.TotalMilliseconds; // 落後太多就不要追
                }
            }
            finally
            {
                Native.timeEndPeriod(1);
                // 停止：把每個圖示放回動畫開始前的位置，並恢復原本的「自動排列 / 對齊格線」
                // 畫面上的位置和檔案總管記錄的位置都要放回去
                try { if (items.Count > 0) desktop.MoveFast(items.Select(i => i.Index).ToArray(), origin); } catch { }
                try { if (items.Count > 0) desktop.MoveRaw(items.Select(i => i.Pidl).ToArray(), origin); } catch { }
                try
                {
                    uint on = savedFlags & (Desktop.FWF_AUTOARRANGE | Desktop.FWF_SNAPTOGRID);
                    if (on != 0) desktop.SetFlags(on, on);
                }
                catch { }
                try { desktop.Dispose(); } catch { }
            }
            if (failure != null && Failed != null) { try { Failed(failure); } catch { } }
        }

        static double EaseInOut(double t) { t = Math.Max(0, Math.Min(1, t)); return t < 0.5 ? 2 * t * t : 1 - Math.Pow(-2 * t + 2, 2) / 2; }

        void Step(double now)
        {
            int sx = desktop.Spacing.X, sy = desktop.Spacing.Y;
            POINT[] want = home.ToArray();

            if (mode == 1) // 隨機跳動：隨機挑圖示往上跳一下再落回原位
            {
                int maxHops = Math.Max(2, items.Count / 5);
                if (now >= nextEvent && hops.Count < maxHops)
                {
                    List<int> free = Enumerable.Range(0, items.Count).Where(i => !hops.Any(h => h.I == i)).ToList();
                    if (free.Count > 0)
                        hops.Add(new Hop { I = free[rnd.Next(free.Count)], T0 = now, Dur = 420 / speed, Height = (float)(sy * (0.30 + rnd.NextDouble() * 0.22)) });
                    nextEvent = now + (40 + rnd.NextDouble() * 110) / speed;
                }
                foreach (Hop h in hops.ToList())
                {
                    double t = (now - h.T0) / h.Dur;
                    if (t >= 1) { hops.Remove(h); continue; }
                    want[h.I].Y = home[h.I].Y - (int)Math.Round(4 * h.Height * t * (1 - t)); // 拋物線
                }
            }
            else if (mode == 2) // 互相換位：好幾對圖示同時沿著弧線交換位置；所有位置還是同一組，所以形狀不變
            {
                int maxSwaps = Math.Max(1, items.Count / 6);
                if (now >= nextEvent && swaps.Count < maxSwaps && items.Count >= 2)
                {
                    List<int> free = Enumerable.Range(0, items.Count).Where(i => !swaps.Any(s => s.A == i || s.B == i)).ToList();
                    if (free.Count >= 2)
                    {
                        int a = free[rnd.Next(free.Count)];
                        free.Remove(a);
                        int b = free[rnd.Next(free.Count)];
                        swaps.Add(new Swap { A = a, B = b, T0 = now, Dur = 560 / speed });
                    }
                    nextEvent = now + (60 + rnd.NextDouble() * 140) / speed;
                }
                foreach (Swap s in swaps.ToList())
                {
                    double t = (now - s.T0) / s.Dur, e = EaseInOut(t), arc = Math.Sin(Math.PI * Math.Min(1, t)) * sy * 0.45;
                    POINT pa = home[s.A], pb = home[s.B];
                    if (t >= 1)
                    {
                        home[s.A] = pb;
                        home[s.B] = pa;
                        want[s.A] = pb;
                        want[s.B] = pa;
                        swaps.Remove(s);
                    }
                    else
                    {
                        want[s.A] = new POINT((int)Math.Round(pa.X + (pb.X - pa.X) * e), (int)Math.Round(pa.Y + (pb.Y - pa.Y) * e - arc));
                        want[s.B] = new POINT((int)Math.Round(pb.X + (pa.X - pb.X) * e), (int)Math.Round(pb.Y + (pa.Y - pb.Y) * e + arc));
                    }
                }
            }
            else if (mode == 3) // 波浪：從左到右一波一波起伏
            {
                double phase = now / 1000.0 * Math.PI * 2 * 0.9 * speed;
                for (int i = 0; i < items.Count; i++)
                    want[i].Y = home[i].Y - (int)Math.Round(Math.Sin(phase - home[i].X / (double)sx * 0.8) * sy * 0.26);
            }

            // 直接移動桌面清單裡的圖示（立刻重畫）；Windows 11 的一般移動方式在位置一直變的時候會延後才畫，看起來就像沒動
            List<int> idx = new List<int>();
            List<POINT> pts = new List<POINT>();
            for (int i = 0; i < items.Count; i++)
                if (want[i].X != shown[i].X || want[i].Y != shown[i].Y) { idx.Add(items[i].Index); pts.Add(want[i]); shown[i] = want[i]; }
            if (idx.Count > 0) desktop.MoveFast(idx.ToArray(), pts.ToArray());
        }

        // 停止並把每個圖示放回動畫開始前的位置
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            stopRequested = true;
            if (thread.IsAlive && Thread.CurrentThread != thread) thread.Join(3000);
        }
    }
}
