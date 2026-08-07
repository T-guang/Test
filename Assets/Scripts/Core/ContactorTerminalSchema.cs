using System.Collections.Generic;

namespace ElectricalSim.Core
{
    /// <summary>
    /// 接触器触点类型枚举：主触点、常开辅助触点、常闭辅助触点。
    /// </summary>
    public enum ContactorContactType
    {
        /// <summary>主触点（L1/T1、L2/T2、L3/T3），线圈得电时闭合，失电时断开。</summary>
        Main,

        /// <summary>常开辅助触点（13/14、33/34），线圈得电时闭合，失电时断开。</summary>
        NormallyOpen,

        /// <summary>常闭辅助触点（21/22），线圈失电时闭合，得电时断开。</summary>
        NormallyClosed
    }

    /// <summary>
    /// 接触器主触点、常开辅助触点、常闭辅助触点端子配对的唯一权威 Schema。
    /// 业务代码不再散写 33/34、13/14、21/22、L1/T1 等触点组合，统一通过本 Schema 查询。
    /// 不依赖场景、Workspace、GameObject；不使用 displayName 或对象名称；集合对外只读。
    /// </summary>
    public static class ContactorTerminalSchema
    {
        /// <summary>
        /// 主触点配对（L1/T1、L2/T2、L3/T3），线圈得电时闭合，失电时断开。
        /// </summary>
        public static IReadOnlyList<ContactorContactPair> MainContactPairs { get; }

        /// <summary>
        /// 常开辅助触点配对（13/14、33/34），线圈得电时闭合，失电时断开。
        /// </summary>
        public static IReadOnlyList<ContactorContactPair> NormallyOpenContactPairs { get; }

        /// <summary>
        /// 常闭辅助触点配对（21/22），线圈失电时闭合，得电时断开。
        /// </summary>
        public static IReadOnlyList<ContactorContactPair> NormallyClosedContactPairs { get; }

        /// <summary>
        /// 全部受控触点配对（主 + 常开 + 常闭），按 Main、NO、NC 顺序排列。
        /// </summary>
        public static IReadOnlyList<ContactorContactPair> AllControlledContactPairs { get; }

        static ContactorTerminalSchema()
        {
            MainContactPairs = System.Array.AsReadOnly(new[]
            {
                new ContactorContactPair(TerminalConstants.L1, TerminalConstants.T1, ContactorContactType.Main),
                new ContactorContactPair(TerminalConstants.L2, TerminalConstants.T2, ContactorContactType.Main),
                new ContactorContactPair(TerminalConstants.L3, TerminalConstants.T3, ContactorContactType.Main)
            });

            NormallyOpenContactPairs = System.Array.AsReadOnly(new[]
            {
                new ContactorContactPair(TerminalConstants.AuxNO13, TerminalConstants.AuxNO14, ContactorContactType.NormallyOpen),
                new ContactorContactPair(TerminalConstants.AuxNO33, TerminalConstants.AuxNO34, ContactorContactType.NormallyOpen)
            });

            NormallyClosedContactPairs = System.Array.AsReadOnly(new[]
            {
                new ContactorContactPair(TerminalConstants.AuxNC21, TerminalConstants.AuxNC22, ContactorContactType.NormallyClosed)
            });

            var all = new List<ContactorContactPair>(
                MainContactPairs.Count + NormallyOpenContactPairs.Count + NormallyClosedContactPairs.Count);
            AddRange(all, MainContactPairs);
            AddRange(all, NormallyOpenContactPairs);
            AddRange(all, NormallyClosedContactPairs);
            AllControlledContactPairs = all.AsReadOnly();
        }

        /// <summary>
        /// 按线圈得电状态枚举应闭合的端子配对。
        /// 得电：返回主触点 + 常开辅助触点；失电：返回常闭辅助触点。
        /// </summary>
        public static IEnumerable<ContactorContactPair> EnumerateClosedPairs(bool coilEnergized)
        {
            if (coilEnergized)
            {
                foreach (var pair in MainContactPairs)
                {
                    yield return pair;
                }

                foreach (var pair in NormallyOpenContactPairs)
                {
                    yield return pair;
                }
            }
            else
            {
                foreach (var pair in NormallyClosedContactPairs)
                {
                    yield return pair;
                }
            }
        }

        /// <summary>
        /// 无向判断给定两端子是否构成主触点配对（A↔B 与 B↔A 等价）。
        /// </summary>
        public static bool IsMainPair(string a, string b)
            => ContainsPair(MainContactPairs, a, b);

        /// <summary>
        /// 无向判断给定两端子是否构成常开辅助触点配对（A↔B 与 B↔A 等价）。
        /// </summary>
        public static bool IsNormallyOpenPair(string a, string b)
            => ContainsPair(NormallyOpenContactPairs, a, b);

        /// <summary>
        /// 无向判断给定两端子是否构成常闭辅助触点配对（A↔B 与 B↔A 等价）。
        /// </summary>
        public static bool IsNormallyClosedPair(string a, string b)
            => ContainsPair(NormallyClosedContactPairs, a, b);

        /// <summary>
        /// 无向查找给定两端子对应的配对定义。命中返回 true 并输出配对；未命中返回 false。
        /// </summary>
        public static bool TryFindPair(string a, string b, out ContactorContactPair pair)
        {
            for (var i = 0; i < AllControlledContactPairs.Count; i++)
            {
                var current = AllControlledContactPairs[i];
                if (current.MatchesUndirected(a, b))
                {
                    pair = current;
                    return true;
                }
            }

            pair = default;
            return false;
        }

        private static bool ContainsPair(IReadOnlyList<ContactorContactPair> pairs, string a, string b)
        {
            for (var i = 0; i < pairs.Count; i++)
            {
                if (pairs[i].MatchesUndirected(a, b))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddRange(List<ContactorContactPair> target, IReadOnlyList<ContactorContactPair> source)
        {
            for (var i = 0; i < source.Count; i++)
            {
                target.Add(source[i]);
            }
        }
    }

    /// <summary>
    /// 接触器端子配对的不可变值类型。
    /// <see cref="StartTerminalId"/>/<see cref="EndTerminalId"/> 为端子 id，
    /// <see cref="ContactType"/> 标识触点类型；<see cref="MatchesUndirected"/> 支持无向比较。
    /// </summary>
    public readonly struct ContactorContactPair
    {
        /// <summary>起点端子 id（如 L1、13、21）。</summary>
        public string StartTerminalId { get; }

        /// <summary>终点端子 id（如 T1、14、22）。</summary>
        public string EndTerminalId { get; }

        /// <summary>触点类型（Main / NormallyOpen / NormallyClosed）。</summary>
        public ContactorContactType ContactType { get; }

        public ContactorContactPair(string startTerminalId, string endTerminalId, ContactorContactType contactType)
        {
            StartTerminalId = startTerminalId;
            EndTerminalId = endTerminalId;
            ContactType = contactType;
        }

        /// <summary>
        /// 无向匹配： (Start,End) 或 (End,Start) 均视为命中。
        /// 比较使用 <see cref="System.StringComparison.Ordinal"/>。
        /// </summary>
        public bool MatchesUndirected(string x, string y)
            => (string.Equals(x, StartTerminalId, System.StringComparison.Ordinal) &&
                string.Equals(y, EndTerminalId, System.StringComparison.Ordinal)) ||
               (string.Equals(x, EndTerminalId, System.StringComparison.Ordinal) &&
                string.Equals(y, StartTerminalId, System.StringComparison.Ordinal));
    }
}
