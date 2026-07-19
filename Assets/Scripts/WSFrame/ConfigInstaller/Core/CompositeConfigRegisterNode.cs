using System.Collections.Generic;
using UnityEngine;

namespace WS_Modules.ConfigInstaller
{
    /// <summary>
    /// 抽象组合节点。按 Inspector 中 children 的顺序依次执行子节点。
    /// </summary>
    public abstract class CompositeConfigRegisterNode : ConfigRegisterNodeBase
    {
        [SerializeField, Tooltip("当前组合节点包含的子节点。执行 Register 时会按列表顺序调用每个子节点，空引用会被跳过。")]
        private List<ConfigRegisterNodeBase> children = new List<ConfigRegisterNodeBase>();

        public override void Register()
        {
            foreach (var child in children)
            {
                if (child == null)
                {
                    continue;
                }

                child.Register();
            }
        }
    }
}
