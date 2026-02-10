using Buddy.Coroutines;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Triton.Game;
using Triton.Game.Mapping;

namespace HREngine.Bots
{
    /// <summary>
    /// Entity 扩展方法，补充旧版 Hearthbuddy.exe 中缺失的 API
    /// </summary>
    static class EntityCompat
    {
        private const int TAG_REBORN = 2898;
        private const int TAG_SPELLBURST = 2917;

        internal static bool HasSpellburst(this Entity entity)
        {
            try { return entity.GetTag((GAME_TAG)TAG_SPELLBURST) > 0; }
            catch { return false; }
        }

        internal static bool HasReborn(this Entity entity)
        {
            try { return entity.GetTag((GAME_TAG)TAG_REBORN) > 0; }
            catch { return false; }
        }

        internal static int GetLocationCooldown(this Entity entity)
        {
            // 地标冷却回合数，从 TAG_SCRIPT_DATA_NUM_2 或自定义 tag 读取
            try { return entity.GetTag(GAME_TAG.TAG_SCRIPT_DATA_NUM_2); }
            catch { return 0; }
        }

        internal static List<Card> GetQuestCards(this ZoneSecret zone)
        {
            var cards = new List<Card>();
            try
            {
                // 尝试通过反射调用新版 API
                var method = typeof(ZoneSecret).GetMethod("GetQuestCards",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                if (method != null)
                {
                    return (List<Card>)method.Invoke(zone, null);
                }

                // 回退：从奥秘区域获取所有卡牌，筛选任务卡
                var allCards = zone.GetSecretCards();
                foreach (var card in allCards)
                {
                    if (card != null)
                    {
                        var e = card.GetEntity();
                        if (e != null && e.GetTag(GAME_TAG.QUEST) > 0)
                        {
                            cards.Add(card);
                        }
                    }
                }
            }
            catch { }
            return cards;
        }
    }

    static class Extensions
    {
        /// <summary>
        /// 邪能地窖
        /// </summary>
        /// <param name="card"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        internal static async Task UnderfelRift(this HSCard card, int timeout = 500)
        {
            await card.Pickup(timeout);
        }
        /// <summary>
        /// 扩展方法，用于异步执行卡片的卡组操作。
        /// </summary>
        /// <param name="card">需要卡组操作的HSCard对象。</param>
        /// <param name="timeout">等待超时时间（毫秒），默认500毫秒。</param>
        internal static async Task DeckAction(this HSCard card, int timeout = 500)
        {
            // 异步拿起卡片，等待指定时间
            await card.Pickup(timeout);

            // 开始计时器，用于判断超时
            Stopwatch stopwatch = Stopwatch.StartNew();
            bool canTradeOrForge = false;

            // 在指定的超时时间内循环检测ZoneMgr是否已初始化
            while (stopwatch.ElapsedMilliseconds < timeout)
            {
                // 如果ZoneMgr尚未初始化，每隔50毫秒检查一次
                if (ZoneMgr.Get() == null)
                {
                    await Coroutine.Sleep(50);
                }
                else
                {
                    // ZoneMgr已初始化，标记为可进行卡组操作并退出循环
                    canTradeOrForge = true;
                    break;
                }
            }

            // 如果超时且无法进行卡组操作，直接返回
            if (!canTradeOrForge)
                return;

            // 查找卡组操作区域的碰撞器，用于确定卡片放置位置
            Collider collider = Board.Get().FindCollider("DeckActionArea");

            // 获取卡组操作区域的中心点
            var center = collider.Bounds.m_Center;
			var screenPoint = Camera.Main.WorldToScreenPoint(center);
            if (screenPoint.X > Screen.Width)
            {
                // 超出屏幕空间，从边界减随机10个像素点作为真实point
                var ranPoint = Client.Random.Next(0, 10);
                screenPoint.X = Screen.Width - ranPoint;
                center.X = Camera.Main.ScreenToWorldPoint(screenPoint).X;
            }

            // 模拟人类操作，将光标移动到卡组操作区域的中心点
            await Client.MoveCursorHumanLike(center);

            // 稍作延迟，模拟真实用户的点击行为
            await Coroutine.Sleep(1);

            // 在卡组操作区域的中心点执行左键点击操作，完成卡组操作
            Client.LeftClickAt(center);
        }

        /// <summary>
        /// 鼠标左击卡牌
        /// </summary>
        /// <param name="card"></param>
        /// <param name="timeout"></param>
        /// <returns></returns>
        internal static async Task LeftClickCard(this HSCard card, int timeout = 500)
        {
            Vector3 center = Client.CardInteractPoint(card.Card);
            await Client.MoveCursorHumanLike(center);
            Client.LeftClickAt(center);
        }

        internal static ConstructorInfo hsCardCtor = typeof(HSCard).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            null, new Type[] { typeof(Entity) }, null);
        static PerFrameCachedValue<Dictionary<int, HSCard>> cachedCardsDict;
        static int maxEntityId = 100;
        internal static Dictionary<int, HSCard> AllCardsDict
        {
            get
            {
                if (cachedCardsDict == null)
                {
                    cachedCardsDict = new PerFrameCachedValue<Dictionary<int, HSCard>>(() =>
                    {
                        var dict = new Dictionary<int, HSCard>();
                        var id = 0;
                        var paramArr = new object[1];
                        while (id++ < maxEntityId)
                        {
                            var e = TritonHs.GameState.GetEntity(id);
                            if (e != null)
                            {
                                paramArr[0] = e;
                                dict[id] = (HSCard)hsCardCtor.Invoke(paramArr);
                                maxEntityId = Math.Max(id + 30, maxEntityId);
                            }
                        }
                        return dict;
                    });
                }
                return cachedCardsDict;
            }
        }

        internal static List<HSCard> GetAllCards()
        {
            return AllCardsDict.Values.ToList();
        }
        /// <summary>
        /// 每局游戏新开时重置初始的最大ID
        /// </summary>
        internal static void ResetMaxId()
        {
            maxEntityId = 100;
        }
    }
}
