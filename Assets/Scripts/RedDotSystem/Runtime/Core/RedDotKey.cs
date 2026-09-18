using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace RPG.RedDotSystemNS
{
    /// <summary>
    /// 红点树中的一个静态节点 Asset。
    /// 节点身份由 Unity Asset 引用决定，层级由 Parent 引用决定。
    /// </summary>
    [CreateAssetMenu(fileName = "RedDotKey", menuName = "RPG/Red Dot/Key", order = 0)]
    public sealed class RedDotKey : ScriptableObject
    {
        #region 配置字段

        [SerializeField]
        [Tooltip("当前层级的节点名称。同一父节点下不能重复。")]
        private string segmentName = "New";

        [SerializeField]
        [Tooltip("直接父节点；为空时表示根节点。")]
        private RedDotKey parent;

        [SerializeField]
        [Tooltip("同一父节点下的显示顺序。")]
        private int siblingOrder;

        #endregion

        #region 公开属性

        /// <summary>获取当前层级的节点名称。</summary>
        public string SegmentName => segmentName;

        /// <summary>获取直接父节点；根节点返回空。</summary>
        public RedDotKey Parent => parent;

        /// <summary>获取同级显示顺序。</summary>
        public int SiblingOrder => siblingOrder;

        /// <summary>获取由 Parent 链和 SegmentName 推导出的完整显示路径。</summary>
        public string DerivedPath => BuildDerivedPath();

        #endregion

        #region 路径与显示

        /// <summary>返回当前节点的完整显示路径。</summary>
        /// <returns>由根节点到当前节点组成的斜杠分隔路径。</returns>
        public override string ToString() => DerivedPath;

        /// <summary>
        /// 按节点引用链构造显示路径，并在配置错误时暴露循环引用。
        /// </summary>
        /// <returns>当前节点的完整显示路径。</returns>
        private string BuildDerivedPath()
        {
            var visitedKeys = new HashSet<RedDotKey>();
            var segments = new List<string>();
            RedDotKey currentKey = this;

            while (currentKey != null)
            {
                if (!visitedKeys.Add(currentKey))
                {
                    throw new InvalidOperationException(
                        $"[RedDotKey] Parent 链存在循环：{name}。请在节点设置页修复配置。");
                }

                segments.Add(currentKey.segmentName ?? string.Empty);
                currentKey = currentKey.parent;
            }

            segments.Reverse();
            var builder = new StringBuilder();
            for (int index = 0; index < segments.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append('/');
                }

                builder.Append(segments[index]);
            }

            return builder.ToString();
        }

        #endregion
    }
}
