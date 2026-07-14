using System.Collections.Generic;
using System.Linq;

namespace ElectricalSim.Practice.Netlist
{
    /// <summary>
    /// 为练习网表维护端子连通等价分组的通用并查集；PracticeNetlist 用它判断两端子是否属于同一电气节点。
    /// 元素会在 Add、Find 或 Union 时登记，Find 会执行路径压缩；本类不拥有元件语义、端子规则或评分职责。
    /// 修改根节点合并或未知元素处理会改变网表连通比较，必须回归重复 Union、多分组隔离和连接比对。
    /// </summary>
    public sealed class UnionFind<T>
    {
        private readonly Dictionary<T, T> parent = new Dictionary<T, T>();

        public void Add(T item)
        {
            if (!parent.ContainsKey(item))
            {
                parent[item] = item;
            }
        }

        // Find 会将访问路径直接压缩到当前根；不要改为仅查询，否则连通分组遍历的性能与代表项行为都会变化。
        public T Find(T item)
        {
            if (!parent.ContainsKey(item))
            {
                Add(item);
            }

            if (EqualityComparer<T>.Default.Equals(parent[item], item))
            {
                return item;
            }

            parent[item] = Find(parent[item]);
            return parent[item];
        }

        public void Union(T first, T second)
        {
            Add(first);
            Add(second);

            var firstRoot = Find(first);
            var secondRoot = Find(second);
            if (!EqualityComparer<T>.Default.Equals(firstRoot, secondRoot))
            {
                parent[firstRoot] = secondRoot;
            }
        }

        public bool AreConnected(T first, T second)
        {
            if (!parent.ContainsKey(first) || !parent.ContainsKey(second))
            {
                return false;
            }

            return EqualityComparer<T>.Default.Equals(Find(first), Find(second));
        }

        // 先复制键集合再调用 Find，避免路径压缩过程中枚举 Dictionary.Keys 产生集合修改风险。
        public IReadOnlyList<IReadOnlyList<T>> GetGroups()
        {
            var groups = new Dictionary<T, List<T>>();
            foreach (var item in parent.Keys.ToList())
            {
                var root = Find(item);
                if (!groups.TryGetValue(root, out var group))
                {
                    group = new List<T>();
                    groups[root] = group;
                }

                group.Add(item);
            }

            return groups.Values.Select(g => (IReadOnlyList<T>)g).ToList();
        }
    }
}
